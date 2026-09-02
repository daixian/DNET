using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DNET;
using Newtonsoft.Json;

namespace Flurl.DNET
{
    /// <summary>
    /// 提供基于DNET事件类型的强类型异步控制器服务器。
    /// 每个实例内部创建并独占一个DNServer，独占消费PeerReceived和连接生命周期事件，
    /// 在网络线程之外完成JSON路由、连接内有序Action执行、自动事务回复和标准错误映射。
    /// </summary>
    public sealed class FlurlDnetServer : IDisposable
    {
        #region 生命周期状态

        /// <summary>
        /// 服务器尚未启动且允许配置控制器。
        /// </summary>
        private const int _configuringState = 0;

        /// <summary>
        /// 服务器已经启动。
        /// </summary>
        private const int _startedState = 1;

        /// <summary>
        /// 服务器正在停止。
        /// </summary>
        private const int _stoppingState = 2;

        /// <summary>
        /// 服务器已经停止，不能再次启动。
        /// </summary>
        private const int _stoppedState = 3;

        /// <summary>
        /// 无异步工作时复用的已完成任务。
        /// </summary>
        private static readonly Task _completedTask = Task.FromResult(true);

        /// <summary>
        /// DNET当前使用进程级PeerManager，因此同一进程只允许一个FlurlDnetServer持有活动监听租约。
        /// </summary>
        private static int _activeServerLease;

        #endregion

        #region 配置与运行成员

        /// <summary>
        /// 保护控制器注册、启动和停止状态转换的同步对象。
        /// </summary>
        private readonly object _lifecycleLock = new object();

        /// <summary>
        /// 当前实例独占的底层DNET服务器。
        /// </summary>
        private readonly DNServer _server;

        /// <summary>
        /// 构造时取得的服务器选项快照。
        /// </summary>
        private readonly FlurlDnetServerOptions _options;

        /// <summary>
        /// 调用方显式注册、等待启动时扫描的程序集。
        /// </summary>
        private readonly List<Assembly> _controllerAssemblies = new List<Assembly>();

        /// <summary>
        /// 可选外部单例控制器工厂。
        /// </summary>
        private Func<Type, object> _controllerFactory;

        /// <summary>
        /// 当前启动周期创建的控制器注册器。
        /// </summary>
        private DnetControllerRegistry _controllerRegistry;

        /// <summary>
        /// 当前启动周期使用的请求调度器。
        /// </summary>
        private DnetServerDispatcher _dispatcher;

        /// <summary>
        /// 当前幂等停止任务。
        /// </summary>
        private Task _stopTask = _completedTask;

        /// <summary>
        /// 当前服务器生命周期状态。
        /// </summary>
        private int _state = _configuringState;

        /// <summary>
        /// 标记当前实例是否已经释放。
        /// </summary>
        private int _disposed;

        /// <summary>
        /// 标记当前实例是否持有进程级活动服务器租约。
        /// </summary>
        private int _ownsActiveServerLease;

        #endregion

        /// <summary>
        /// 创建一个内部独占DNServer的控制器服务器。
        /// </summary>
        /// <param name="options">服务端选项；null时使用默认配置。</param>
        public FlurlDnetServer(FlurlDnetServerOptions options = null)
        {
            _options = (options ?? new FlurlDnetServerOptions()).Clone();
            _server = new DNServer {
                Name = "Flurl.DNET.Server",
                // 快速模式只在Socket回调内完成消息所有权转移，用户Action仍在线程池执行。
                IsFastResponse = true
            };
        }

        #region 公开状态与事件

        /// <summary>
        /// 控制器路由、JSON、业务执行或响应发送失败时触发。
        /// 订阅者在Flurl.DNET工作线程执行，其异常会被隔离。
        /// </summary>
        public event EventHandler<DnetServerRequestFailedEventArgs> RequestFailed;

        /// <summary>
        /// 获取底层监听器是否已经启动。
        /// </summary>
        public bool IsStarted => Volatile.Read(ref _disposed) == 0 && _server.IsStarted;

        /// <summary>
        /// 获取当前服务器用于业务请求和正常回复的Newtonsoft.Json设置。
        /// 应在服务器启动前完成该设置对象的配置。
        /// </summary>
        public JsonSerializerSettings JsonSettings => _options.JsonSettings;

        #endregion

        #region 控制器配置

        /// <summary>
        /// 注册一个或多个将在Start前扫描DnetController类型的程序集。
        /// 重复传入同一程序集会被忽略。
        /// </summary>
        /// <param name="assemblies">包含控制器的程序集。</param>
        /// <returns>当前服务器，便于链式配置。</returns>
        public FlurlDnetServer AddControllers(params Assembly[] assemblies)
        {
            if (assemblies == null) throw new ArgumentNullException(nameof(assemblies));
            lock (_lifecycleLock) {
                ThrowIfDisposed();
                ThrowIfConfigurationFrozen();
                foreach (Assembly assembly in assemblies) {
                    if (assembly == null) throw new ArgumentException("控制器程序集不能为null。", nameof(assemblies));
                    if (!_controllerAssemblies.Contains(assembly)) {
                        _controllerAssemblies.Add(assembly);
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// 设置创建单例控制器的外部工厂，便于接入现有依赖注入容器。
        /// 外部工厂返回的实例由调用方管理，服务器不会释放。
        /// </summary>
        /// <param name="controllerFactory">根据控制器类型返回兼容实例的工厂。</param>
        /// <returns>当前服务器，便于链式配置。</returns>
        public FlurlDnetServer UseControllerFactory(Func<Type, object> controllerFactory)
        {
            if (controllerFactory == null) throw new ArgumentNullException(nameof(controllerFactory));
            lock (_lifecycleLock) {
                ThrowIfDisposed();
                ThrowIfConfigurationFrozen();
                _controllerFactory = controllerFactory;
            }
            return this;
        }

        #endregion

        #region 启动与停止

        /// <summary>
        /// 在绑定监听端口前扫描并验证全部控制器，然后启动独占DNServer。
        /// 一个FlurlDnetServer实例只允许成功启动一次。
        /// </summary>
        /// <param name="port">监听端口，范围为1到65535。</param>
        /// <param name="hostName">监听地址；默认Any。</param>
        /// <param name="backlog">Socket监听队列长度，必须大于零。</param>
        public void Start(int port, string hostName = "Any", int backlog = 64)
        {
            if (port <= 0 || port > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(port));
            if (string.IsNullOrWhiteSpace(hostName)) throw new ArgumentException("监听地址不能为空。", nameof(hostName));
            if (backlog <= 0) throw new ArgumentOutOfRangeException(nameof(backlog));

            lock (_lifecycleLock) {
                ThrowIfDisposed();
                if (_state != _configuringState) {
                    throw new InvalidOperationException("FlurlDnetServer只能成功启动一次。");
                }
                if (_controllerAssemblies.Count == 0) {
                    throw new InvalidOperationException("启动前必须通过AddControllers注册控制器程序集。");
                }
                if (Interlocked.CompareExchange(ref _activeServerLease, 1, 0) != 0) {
                    throw new InvalidOperationException(
                        "DNET使用进程级PeerManager，同一进程只能运行一个FlurlDnetServer。");
                }
                Volatile.Write(ref _ownsActiveServerLease, 1);

                DnetControllerRegistry registry = null;
                DnetServerDispatcher dispatcher = null;
                try {
                    registry = new DnetControllerRegistry(_controllerFactory);
                    DnetRouteTable routeTable = registry.Build(_controllerAssemblies);
                    dispatcher = new DnetServerDispatcher(_server,
                        routeTable,
                        _options,
                        RaiseRequestFailed);

                    SubscribeDispatcher(dispatcher);
                    _server.Start(port, hostName, backlog);
                    if (!_server.IsStarted) {
                        throw new FlurlDnetConnectionException(
                            $"Flurl.DNET服务器无法监听{hostName}:{port}。");
                    }

                    _controllerRegistry = registry;
                    _dispatcher = dispatcher;
                    _state = _startedState;
                } catch {
                    if (dispatcher != null) {
                        UnsubscribeDispatcher(dispatcher);
                        ObserveStop(dispatcher.StopAsync());
                    }
                    registry?.Dispose();
                    ReleaseActiveServerLease();
                    throw;
                }
            }
        }

        /// <summary>
        /// 停止接收新请求、关闭全部Peer并等待活动Action完成。
        /// 等待超过ShutdownTimeout时本任务返回，但控制器会延迟到活动Action真正结束后再释放。
        /// </summary>
        /// <returns>服务器停止监听且完成当前关闭等待后的任务。</returns>
        public Task StopAsync()
        {
            lock (_lifecycleLock) {
                if (_state == _stoppingState || _state == _stoppedState) return _stopTask;
                if (_state == _configuringState) {
                    _state = _stoppedState;
                    _stopTask = _completedTask;
                    return _stopTask;
                }

                _state = _stoppingState;
                DnetServerDispatcher dispatcher = _dispatcher;
                DnetControllerRegistry registry = _controllerRegistry;
                _stopTask = Task.Run(() => StopCoreAsync(dispatcher, registry));
                return _stopTask;
            }
        }

        /// <summary>
        /// 执行底层关闭、事件解绑、邮箱等待和控制器生命周期清理。
        /// </summary>
        /// <param name="dispatcher">当前请求调度器。</param>
        /// <param name="registry">当前控制器注册器。</param>
        private async Task StopCoreAsync(DnetServerDispatcher dispatcher,
            DnetControllerRegistry registry)
        {
            Task dispatcherStop = dispatcher?.StopAsync() ?? _completedTask;
            Exception closeException = null;
            try {
                _server.Close(false);
            } catch (Exception exception) {
                closeException = exception;
            } finally {
                if (dispatcher != null) UnsubscribeDispatcher(dispatcher);
            }

            bool stoppedWithinTimeout = dispatcherStop.IsCompleted;
            if (!stoppedWithinTimeout && _options.ShutdownTimeout > TimeSpan.Zero) {
                Task completed = await Task.WhenAny(dispatcherStop,
                        Task.Delay(_options.ShutdownTimeout))
                    .ConfigureAwait(false);
                stoppedWithinTimeout = ReferenceEquals(completed, dispatcherStop);
            }

            Exception dispatcherException = null;
            if (stoppedWithinTimeout) {
                try {
                    await dispatcherStop.ConfigureAwait(false);
                } catch (Exception exception) {
                    dispatcherException = exception;
                } finally {
                    registry?.Dispose();
                }
            } else {
                Task delayedCleanup = dispatcherStop.ContinueWith(task => {
                        if (task.IsFaulted && LogProxy.Error != null)
                            LogProxy.Error($"FlurlDnetServer:延迟停止调度器异常 {task.Exception}");
                        registry?.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                ObserveStop(delayedCleanup);
            }

            lock (_lifecycleLock) {
                _dispatcher = null;
                _controllerRegistry = null;
                _state = _stoppedState;
            }
            ReleaseActiveServerLease();
            if (closeException != null) {
                throw new FlurlDnetConnectionException("关闭Flurl.DNET服务器时发生异常。",
                    null,
                    closeException);
            }
            if (dispatcherException != null) {
                throw new FlurlDnetException("停止Flurl.DNET请求调度器时发生异常。",
                    null,
                    null,
                    dispatcherException);
            }
        }

        #endregion

        #region DNET事件绑定与诊断

        /// <summary>
        /// 把调度器作为底层DNServer连接和接收事件的唯一消费者。
        /// </summary>
        /// <param name="dispatcher">本次启动使用的调度器。</param>
        private void SubscribeDispatcher(DnetServerDispatcher dispatcher)
        {
            _server.PeerConnected += dispatcher.HandlePeerConnected;
            _server.PeerDisconnected += dispatcher.HandlePeerDisconnected;
            _server.PeerReceived += dispatcher.HandlePeerReceived;
        }

        /// <summary>
        /// 解除当前调度器的底层事件绑定。
        /// </summary>
        /// <param name="dispatcher">本次启动使用的调度器。</param>
        private void UnsubscribeDispatcher(DnetServerDispatcher dispatcher)
        {
            _server.PeerConnected -= dispatcher.HandlePeerConnected;
            _server.PeerDisconnected -= dispatcher.HandlePeerDisconnected;
            _server.PeerReceived -= dispatcher.HandlePeerReceived;
        }

        /// <summary>
        /// 逐个调用RequestFailed订阅者并隔离单个诊断处理器异常。
        /// </summary>
        /// <param name="eventArgs">请求失败信息。</param>
        private void RaiseRequestFailed(DnetServerRequestFailedEventArgs eventArgs)
        {
            EventHandler<DnetServerRequestFailedEventArgs> handlers = RequestFailed;
            if (handlers == null) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"FlurlDnetServer请求失败：{eventArgs.ErrorCode}, {eventArgs.Exception}");
                return;
            }

            foreach (EventHandler<DnetServerRequestFailedEventArgs> handler
                in handlers.GetInvocationList()) {
                try {
                    handler(this, eventArgs);
                } catch (Exception exception) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"FlurlDnetServer.RequestFailed订阅者异常 {exception}");
                }
            }
        }

        /// <summary>
        /// 观察启动回滚阶段产生的调度器停止任务。
        /// </summary>
        /// <param name="stopTask">要观察的停止任务。</param>
        private static void ObserveStop(Task stopTask)
        {
            stopTask.ContinueWith(task => {
                    if (task.IsFaulted && LogProxy.Error != null)
                        LogProxy.Error($"FlurlDnetServer:启动回滚停止异常 {task.Exception}");
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        #endregion

        #region 参数保护与释放

        /// <summary>
        /// 确保控制器配置尚未因启动或停止而冻结。
        /// </summary>
        private void ThrowIfConfigurationFrozen()
        {
            if (_state != _configuringState) {
                throw new InvalidOperationException("服务器启动或停止后不能再修改控制器配置。");
            }
        }

        /// <summary>
        /// 确保当前实例尚未释放。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) {
                throw new ObjectDisposedException(nameof(FlurlDnetServer));
            }
        }

        /// <summary>
        /// 释放当前实例持有的进程级活动服务器租约。重复调用不会影响后续服务器。
        /// </summary>
        private void ReleaseActiveServerLease()
        {
            if (Interlocked.Exchange(ref _ownsActiveServerLease, 0) == 0) return;
            Interlocked.Exchange(ref _activeServerLease, 0);
        }

        /// <summary>
        /// 幂等停止并释放底层服务器。
        /// Dispose最多同步等待ShutdownTimeout，忽略取消的Action会在完成后触发延迟控制器清理。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try {
                StopAsync().GetAwaiter().GetResult();
            } finally {
                RequestFailed = null;
                _server.Dispose();
                ReleaseActiveServerLease();
            }
        }

        #endregion
    }
}
