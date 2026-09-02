using System;
using System.Threading;
using System.Threading.Tasks;
using DNET;
using Newtonsoft.Json;

namespace Flurl.DNET
{
    /// <summary>
    /// 提供基于DNET事务ID的链式异步请求响应客户端。
    /// 每个实例内部创建并独占一个DNClient，独占消费其Received事件和接收队列，以保证并发请求响应不会被其他消费者取走。
    /// </summary>
    public sealed class FlurlDnetClient : IDisposable
    {
        /// <summary>
        /// 默认请求超时的初始值。
        /// </summary>
        private static readonly TimeSpan _initialDefaultTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 当前实例独占的底层DNET客户端。
        /// </summary>
        private readonly DNClient _client;

        /// <summary>
        /// 请求与响应事务ID调度器。
        /// </summary>
        private readonly DnetRequestDispatcher _requestDispatcher;

        /// <summary>
        /// 未匹配响应的有序异步事件泵。
        /// </summary>
        private readonly DnetUnmatchedResponsePump _unmatchedResponsePump;

        /// <summary>
        /// 保护连接操作、目标地址和连接完成源的同步对象。
        /// </summary>
        private readonly object _connectionLock = new object();

        /// <summary>
        /// 当前进行中的连接任务完成源；没有连接操作时为null。
        /// </summary>
        private TaskCompletionSource<bool> _connectCompletionSource;

        /// <summary>
        /// 当前连接或正在连接的目标主机。
        /// </summary>
        private string _host;

        /// <summary>
        /// 当前连接或正在连接的目标端口。
        /// </summary>
        private int _port;

        /// <summary>
        /// 默认超时Ticks，使用Interlocked保证32位运行时上的原子读写。
        /// </summary>
        private long _defaultTimeoutTicks;

        /// <summary>
        /// 标记当前实例是否已经释放。
        /// </summary>
        private int _disposed;

        /// <summary>
        /// 初始化一个内部独占DNClient的Flurl.DNET客户端。
        /// </summary>
        /// <param name="jsonSettings">JSON序列化设置；为null时创建Newtonsoft.Json默认设置。</param>
        public FlurlDnetClient(JsonSerializerSettings jsonSettings = null)
        {
            JsonSettings = jsonSettings ?? new JsonSerializerSettings();
            _defaultTimeoutTicks = _initialDefaultTimeout.Ticks;

            _client = new DNClient { Name = "Flurl.DNET" };
            _unmatchedResponsePump = new DnetUnmatchedResponsePump(RaiseUnmatchedResponse);
            _requestDispatcher = new DnetRequestDispatcher(_client,
                JsonSettings,
                QueueUnmatchedResponse);

            _client.ConnectSuccess += OnConnectSuccess;
            _client.Received += OnReceived;
            _client.ErrorOccurred += OnErrorOccurred;
        }

        #region 公开状态与事件

        /// <summary>
        /// 收到txrId为零、未知事务ID或超时取消后迟到的响应时触发。
        /// Response只在事件回调期间有效，全部订阅者返回后由库自动释放。
        /// </summary>
        public event EventHandler<DnetUnmatchedResponseEventArgs> UnmatchedReceived;

        /// <summary>
        /// 获取底层DNET客户端是否已经连接。
        /// </summary>
        public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _client.IsConnected;

        /// <summary>
        /// 获取当前客户端用于请求和响应的Newtonsoft.Json设置。
        /// 应在开始并发请求前完成该设置对象的配置。
        /// </summary>
        public JsonSerializerSettings JsonSettings { get; }

        /// <summary>
        /// 获取或设置没有调用WithTimeout时使用的请求超时时间，初始值为30秒。
        /// </summary>
        public TimeSpan DefaultTimeout {
            get => TimeSpan.FromTicks(Interlocked.Read(ref _defaultTimeoutTicks));
            set {
                DnetRequest.ValidateTimeout(value, nameof(value));
                Interlocked.Exchange(ref _defaultTimeoutTicks, value.Ticks);
            }
        }

        #endregion

        #region 连接生命周期

        /// <summary>
        /// 异步连接指定DNET服务器。连接成功前不能发送请求。
        /// </summary>
        /// <param name="host">服务器主机名或IP地址。</param>
        /// <param name="port">服务器端口，范围为1到65535。</param>
        /// <param name="cancellationToken">用于中断本次连接操作的取消标记。</param>
        /// <returns>连接成功时完成的任务。</returns>
        public Task ConnectAsync(string host,
            int port,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(host)) {
                throw new ArgumentException("服务器主机名不能为空。", nameof(host));
            }
            if (port <= 0 || port > ushort.MaxValue) {
                throw new ArgumentOutOfRangeException(nameof(port));
            }
            cancellationToken.ThrowIfCancellationRequested();

            TaskCompletionSource<bool> completionSource;
            lock (_connectionLock) {
                ThrowIfDisposed();
                if (_client.IsConnected) {
                    if (string.Equals(_host, host, StringComparison.OrdinalIgnoreCase)
                        && _port == port) {
                        return System.Threading.Tasks.Task.FromResult(true);
                    }
                    throw new InvalidOperationException("客户端已经连接到其他地址，请先调用Disconnect。");
                }
                if (_connectCompletionSource != null) {
                    throw new InvalidOperationException("当前客户端已经有一个正在进行的连接操作。");
                }

                completionSource = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _connectCompletionSource = completionSource;
                _host = host;
                _port = port;

                try {
                    _client.Connect(host, port);
                } catch (Exception exception) {
                    _connectCompletionSource = null;
                    completionSource.TrySetException(new FlurlDnetConnectionException(
                        $"无法启动DNET连接：{host}:{port}。",
                        null,
                        exception));
                }
            }

            CancellationTokenRegistration cancellationRegistration =
                cancellationToken.Register(() => CancelConnect(completionSource));
            completionSource.Task.ContinueWith(
                (task, state) => ((CancellationTokenRegistration)state).Dispose(),
                cancellationRegistration,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return completionSource.Task;
        }

        /// <summary>
        /// 主动断开当前连接，并以连接异常结束全部正在等待的请求。
        /// 当前客户端未释放时可以再次调用ConnectAsync连接其他地址。
        /// </summary>
        public void Disconnect()
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            TaskCompletionSource<bool> completionSource;
            lock (_connectionLock) {
                completionSource = _connectCompletionSource;
                _connectCompletionSource = null;
                _client.Disconnect();
            }

            completionSource?.TrySetException(
                new FlurlDnetConnectionException("DNET连接操作被主动断开。"));
            _requestDispatcher.FailAll(request => new FlurlDnetConnectionException(
                $"DNET连接被主动断开：TxrId={request.TxrId}, EventType={request.EventType}。"));
        }

        /// <summary>
        /// 释放请求调度、事件泵和内部DNClient，并终止全部未完成操作。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            TaskCompletionSource<bool> completionSource;
            lock (_connectionLock) {
                completionSource = _connectCompletionSource;
                _connectCompletionSource = null;
            }
            completionSource?.TrySetException(
                new ObjectDisposedException(nameof(FlurlDnetClient)));

            _client.ConnectSuccess -= OnConnectSuccess;
            _client.Received -= OnReceived;
            _client.ErrorOccurred -= OnErrorOccurred;

            _requestDispatcher.Dispose();
            _unmatchedResponsePump.Dispose();
            _client.Dispose();
        }

        #endregion

        #region 请求创建与发送

        /// <summary>
        /// 使用事件类型和当前DefaultTimeout创建不可变请求构建器。
        /// </summary>
        /// <param name="eventType">业务事件类型。</param>
        /// <returns>新的请求构建器。</returns>
        public DnetRequest Request(int eventType)
        {
            ThrowIfDisposed();
            if (eventType == DnetFrameworkEventTypes.Error) {
                throw new ArgumentOutOfRangeException(nameof(eventType),
                    "该事件类型由Flurl.DNET保留用于远端错误响应。");
            }
            return new DnetRequest(this, eventType, DefaultTimeout);
        }

        /// <summary>
        /// 发送字节请求并等待匹配响应。
        /// </summary>
        /// <param name="data">请求正文。</param>
        /// <param name="offset">正文起始位置。</param>
        /// <param name="count">正文长度。</param>
        /// <param name="format">请求格式。</param>
        /// <param name="eventType">请求事件类型。</param>
        /// <param name="timeout">请求超时时间。</param>
        /// <param name="cancellationToken">调用方取消标记。</param>
        /// <returns>最终匹配到响应、异常或取消状态的任务。</returns>
        internal Task<DnetResponse> SendAsync(byte[] data,
            int offset,
            int count,
            Format format,
            int eventType,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return _requestDispatcher.SendAsync(data,
                offset,
                count,
                format,
                eventType,
                timeout,
                cancellationToken);
        }

        /// <summary>
        /// 发送UTF-8文本请求并等待匹配响应。
        /// </summary>
        /// <param name="text">请求文本。</param>
        /// <param name="eventType">请求事件类型。</param>
        /// <param name="timeout">请求超时时间。</param>
        /// <param name="cancellationToken">调用方取消标记。</param>
        /// <returns>最终匹配到响应、异常或取消状态的任务。</returns>
        internal Task<DnetResponse> SendStringAsync(string text,
            int eventType,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return _requestDispatcher.SendStringAsync(text,
                Format.Text,
                eventType,
                timeout,
                cancellationToken);
        }

        /// <summary>
        /// 序列化并发送JSON请求，然后等待匹配响应。
        /// </summary>
        /// <typeparam name="T">请求对象类型。</typeparam>
        /// <param name="value">请求对象。</param>
        /// <param name="eventType">请求事件类型。</param>
        /// <param name="timeout">请求超时时间。</param>
        /// <param name="cancellationToken">调用方取消标记。</param>
        /// <returns>最终匹配到响应、异常或取消状态的任务。</returns>
        internal Task<DnetResponse> SendJsonAsync<T>(T value,
            int eventType,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            string json = JsonConvert.SerializeObject(value, JsonSettings);
            return _requestDispatcher.SendStringAsync(json,
                Format.Json,
                eventType,
                timeout,
                cancellationToken);
        }

        #endregion

        #region DNET事件与未匹配响应

        /// <summary>
        /// 响应底层客户端连接成功事件并完成当前连接任务。
        /// </summary>
        /// <param name="client">触发事件的底层客户端。</param>
        private void OnConnectSuccess(DNClient client)
        {
            TaskCompletionSource<bool> completionSource;
            lock (_connectionLock) {
                completionSource = _connectCompletionSource;
                _connectCompletionSource = null;
            }
            completionSource?.TrySetResult(true);
        }

        /// <summary>
        /// 响应底层客户端接收事件并独占清空其消息队列。
        /// </summary>
        /// <param name="client">触发事件的底层客户端。</param>
        private void OnReceived(DNClient client)
        {
            try {
                _requestDispatcher.HandleReceived();
            } catch (Exception exception) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"FlurlDnetClient.OnReceived():分发响应异常 {exception}");
            }
        }

        /// <summary>
        /// 响应底层连接错误，结束连接操作以及全部等待请求。
        /// </summary>
        /// <param name="client">触发事件的底层客户端。</param>
        /// <param name="errorType">DNET错误类型。</param>
        private void OnErrorOccurred(DNClient client, ErrorType errorType)
        {
            TaskCompletionSource<bool> completionSource;
            lock (_connectionLock) {
                completionSource = _connectCompletionSource;
                _connectCompletionSource = null;
            }

            completionSource?.TrySetException(new FlurlDnetConnectionException(
                $"DNET连接失败：{_host}:{_port}, ErrorType={errorType}。",
                errorType));
            _requestDispatcher.FailAll(request => new FlurlDnetConnectionException(
                $"DNET连接中断：TxrId={request.TxrId}, EventType={request.EventType}, "
                + $"ErrorType={errorType}。",
                errorType));
        }

        /// <summary>
        /// 将未匹配响应移交事件泵；当前没有订阅者时立即回收。
        /// </summary>
        /// <param name="response">无法匹配等待请求的响应。</param>
        private void QueueUnmatchedResponse(DnetResponse response)
        {
            if (Volatile.Read(ref _disposed) != 0 || UnmatchedReceived == null) {
                response.Dispose();
                return;
            }
            _unmatchedResponsePump.Enqueue(response);
        }

        /// <summary>
        /// 在事件泵线程中依次调用每个未匹配响应订阅者，并隔离单个订阅者异常。
        /// </summary>
        /// <param name="response">本次未匹配响应。</param>
        private void RaiseUnmatchedResponse(DnetResponse response)
        {
            EventHandler<DnetUnmatchedResponseEventArgs> handlers = UnmatchedReceived;
            if (handlers == null) return;

            var eventArgs = new DnetUnmatchedResponseEventArgs(response);
            foreach (EventHandler<DnetUnmatchedResponseEventArgs> handler
                in handlers.GetInvocationList()) {
                try {
                    handler(this, eventArgs);
                } catch (Exception exception) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"FlurlDnetClient.RaiseUnmatchedResponse():订阅者异常 {exception}");
                }
            }
        }

        /// <summary>
        /// 取消当前指定的连接操作并中断底层连接尝试。
        /// </summary>
        /// <param name="completionSource">发起取消时对应的连接完成源。</param>
        private void CancelConnect(TaskCompletionSource<bool> completionSource)
        {
            bool shouldCancel;
            lock (_connectionLock) {
                shouldCancel = ReferenceEquals(_connectCompletionSource, completionSource);
                if (shouldCancel) {
                    _connectCompletionSource = null;
                    _client.Disconnect();
                }
            }
            if (!shouldCancel) return;

            completionSource.TrySetCanceled();
        }

        #endregion

        /// <summary>
        /// 确保当前客户端尚未释放。
        /// </summary>
        /// <exception cref="ObjectDisposedException">当前实例已经释放时抛出。</exception>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) {
                throw new ObjectDisposedException(nameof(FlurlDnetClient));
            }
        }
    }
}
