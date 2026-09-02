using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 通信传输的服务器类
    /// </summary>
    public class DNServer : IWorkHandler<ServerCommand>
    {
        /// <summary>
        /// 单例实例懒加载容器
        /// </summary>
        private static readonly Lazy<DNServer> _instance = new Lazy<DNServer>(() => new DNServer());

        /// <summary>
        /// 服务器状态检查定时器的执行周期，单位为毫秒。
        /// </summary>
        private const int StatusTimerPeriodMilliseconds = 1000;

        /// <summary>
        /// 标记当前线程正在执行状态定时器回调的服务器，用于避免回调内关闭时等待自身造成死锁。
        /// </summary>
        [ThreadStatic]
        private static DNServer _executingStatusTimerOwner;

        /// <summary>
        /// 单例
        /// </summary>
        public static DNServer Inst => _instance.Value;

        /// <summary>
        /// 服务器端口号
        /// </summary>
        private int _port;

        /// <summary>
        /// 工作线程
        /// </summary>
        private WorkThread<ServerCommand> _workThread;

        /// <summary>
        /// 底层的通信类
        /// </summary>
        private ServerListenerSocket _listenerSocket;

        /// <summary>
        /// 监听成功后用于驱动心跳与状态检查的定时器。
        /// </summary>
        private Timer _timer;

        /// <summary>
        /// 保护定时器创建、启用和释放过程的同步对象。
        /// </summary>
        private readonly object _timerLock = new object();

        /// <summary>
        /// 标记定时器是否允许投递状态检查，使用原子读写与线程池回调同步。
        /// </summary>
        private int _timerEnabled;

        /// <summary>
        /// 标记是否已经存在一个待处理或正在处理的状态检查，防止工作线程阻塞时无界积压。
        /// </summary>
        private int _timerCheckPending;

        /// <summary>
        /// 标记关闭或启动回滚流程正在永久停止当前定时器。
        /// </summary>
        private bool _timerLifecycleStopping;

        /// <summary>
        /// 防止线程池定时器回调重入。心跳发送必须独立于可能被业务接收阻塞的服务器工作队列。
        /// </summary>
        private int _heartbeatTimerRunning;

        /// <summary>
        /// 标记是否已经被disposed
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// 服务器启动成功标志
        /// </summary>
        public bool IsStarted {
            get {
                if (_listenerSocket != null) {
                    return _listenerSocket.IsStarted;
                }
                return false;
            }
        }

        /// <summary>
        /// 是否在接收线程中直接分发消息事件。
        /// 若为true，则在 .NET 的 IO 回调线程中立即调用接收事件,如果事件执行太慢,则容易使线程池枯竭.
        /// 若为false，则切换为工作线程处理，适用于高负载场景。
        /// </summary>
        public bool IsFastResponse { get; set; } = true;

        /// <summary>
        /// 它也可以设置一个名字吧
        /// </summary>
        public string Name { get; set; } = "Server";

        /// <summary>
        /// 事件：某个Peer发生了错误后，会自动调用关闭，这是错误及关闭事件
        /// </summary>
        public event Action<DNServer, Peer, PeerErrorType> PeerErrorOccurred;

        /// <summary>
        /// 某个Peer已经完成底层Socket初始化并开始接收数据时触发。
        /// 该事件只表示连接建立，不应在回调中执行耗时业务逻辑。
        /// </summary>
        public event Action<DNServer, Peer> PeerConnected;

        /// <summary>
        /// 某个Peer因Socket错误、心跳超时、主动删除或服务器关闭而被统一移除时触发。
        /// 每个Peer最多触发一次，触发期间仍可读取Peer的连接信息，但不应再发送业务数据。
        /// </summary>
        public event Action<DNServer, Peer, PeerErrorType> PeerDisconnected;

        /// <summary>
        /// 事件：某个Peer接收到了数据，注意如果可能会有多线程执行这个事件的时候(如IsFastResponse=True的时候)
        /// 如果特别关心回复的发送顺序,那么要对回调函数中的Peer进行加锁.
        /// </summary>
        public event Action<DNServer, Peer> PeerReceived;

        /// <summary>
        /// 启动服务器，会开启工作线程.
        /// </summary>
        /// <param name="port">端口号</param>
        /// <param name="hostName">服务器的主机IP,一般使用Any表示所有的可能IP</param>
        /// <param name="backlog">监听队列的长度</param>
        public void Start(int port, string hostName = "Any", int backlog = 64)
        {
            lock (this) {
                if (_disposed || IsStarted)
                    return;

                try {
                    if (LogProxy.Debug != null)
                        LogProxy.Debug("DNServer.Start():服务器尝试启动...");
                    _port = port;

                    PeerManager.Inst.DeleteAllPeer();

                    // 工作线程总是启动
                    if (_workThread == null) {
                        _workThread = new WorkThread<ServerCommand>("DNServerWorkThread");
                    }
                    _workThread.ClearQueue(OnCommandDiscarded);

                    // 清理以前启动失败遗留的监听对象，再创建本次启动所需的实例。
                    if (_listenerSocket != null) {
                        _listenerSocket.Dispose();
                        _listenerSocket = null;
                    }
                    _listenerSocket = new ServerListenerSocket();
                    _listenerSocket.EventAccept += OnListenerSocketAccept;

                    // msg.text1是服务器的主机IP,一般使用Any表示所有的可能IP
                    _listenerSocket.Start(hostName, port, backlog);

                    if (!_listenerSocket.IsStarted) {
                        if (LogProxy.Error != null)
                            LogProxy.Error("DNServer.Start():启动监听失败");
                        RollbackFailedStart();
                        return;
                    }

                    ResumeStatusTimer();
                    if (LogProxy.Info != null)
                        LogProxy.Info($"DNServer.Start():服务器启动成功,端口:{_port}");
                } catch (Exception e) {
                    if (LogProxy.Error != null)
                        LogProxy.Error("DNServer.Start():异常 " + e.Message);
                    RollbackFailedStart();
                }
            }
        }

        /// <summary>
        /// 关闭服务器
        /// </summary>
        /// <param name="clearEvent">是否清除事件绑定</param>
        public void Close(bool clearEvent = true)
        {
            if (LogProxy.Info != null)
                LogProxy.Info("DNServer.Close():准备关闭Socket和停止工作线程...");
            lock (this) {
                try {
                    StopStatusTimerAndWait();
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNServer.Close():停止Timer异常 {e}");
                }

                // 是否要删除所有客户端?
                PeerManager.Inst.DeleteAllPeer();

                try {
                    if (_workThread != null) {
                        _workThread.Stop();
                    }
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNServer.Close():停止工作线程异常 {e}");
                } finally {
                    _workThread = null;
                    Interlocked.Exchange(ref _timerCheckPending, 0);
                    FinishStatusTimerShutdown();
                }
                try {
                    if (_listenerSocket != null) {
                        _listenerSocket.Dispose();
                    }
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNServer.Close():关闭Socket异常 {e}");
                } finally {
                    _listenerSocket = null;
                }

                if (clearEvent) {
                    if (LogProxy.Debug != null)
                        LogProxy.Debug("DNServer.Close():清空了所有绑定事件...");
                    PeerErrorOccurred = null;
                    PeerConnected = null;
                    PeerDisconnected = null;
                    PeerReceived = null;
                }

                // 不重置算了
                // IsFastResponse = true;
            }
        }

        /// <summary>
        /// 向某个peer发送一条数据。
        /// </summary>
        /// <param name="peerId">peer的Id</param>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据的起始位置</param>
        /// <param name="count">数据的长度</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        /// <param name="immediately">是否立刻发送</param>
        public void Send(int peerId, byte[] data, int offset, int count,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0,
            bool immediately = true)
        {
            TrySend(peerId, data, offset, count, format, txrId, eventType, immediately);
        }

        /// <summary>
        /// 尝试向指定ID的Peer发送一条数据，并返回消息是否成功进入发送队列。
        /// </summary>
        /// <param name="peerId">Peer的ID。</param>
        /// <param name="data">要发送的数据。</param>
        /// <param name="offset">数据的起始位置。</param>
        /// <param name="count">数据的长度。</param>
        /// <param name="format">数据格式。</param>
        /// <param name="txrId">事务ID。</param>
        /// <param name="eventType">消息类型。</param>
        /// <param name="immediately">是否立刻尝试启动发送泵。</param>
        /// <returns>消息成功进入发送队列时返回true；Peer不存在、服务器不可用或触发背压时返回false。</returns>
        public bool TrySend(int peerId, byte[] data, int offset, int count,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0,
            bool immediately = true)
        {
            Peer peer = PeerManager.Inst.GetPeer(peerId);
            if (peer == null || _disposed) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.TrySend():peer为null或者disposed,不能发送.");
                return false;
            }

            return TrySend(peer, data, offset, count, format, txrId, eventType, immediately);
        }

        /// <summary>
        /// 向某个Peer发送一条数据.
        /// </summary>
        /// <param name="peer">某个peer</param>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据的起始位置</param>
        /// <param name="count">数据的长度</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        /// <param name="immediately">是否立刻发送</param>
        public void Send(Peer peer, byte[] data, int offset, int count,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0,
            bool immediately = true)
        {
            TrySend(peer, data, offset, count, format, txrId, eventType, immediately);
        }

        /// <summary>
        /// 尝试向指定Peer发送一条数据，并返回消息是否成功进入发送队列。
        /// </summary>
        /// <param name="peer">目标Peer。</param>
        /// <param name="data">要发送的数据。</param>
        /// <param name="offset">数据的起始位置。</param>
        /// <param name="count">数据的长度。</param>
        /// <param name="format">数据格式。</param>
        /// <param name="txrId">事务ID。</param>
        /// <param name="eventType">消息类型。</param>
        /// <param name="immediately">是否立刻尝试启动发送泵。</param>
        /// <returns>消息成功进入发送队列时返回true；Peer不可用、服务器不可用或触发背压时返回false。</returns>
        public bool TrySend(Peer peer, byte[] data, int offset, int count,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0,
            bool immediately = true)
        {
            if (peer == null || _disposed) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.TrySend():peer为null或者disposed,不能发送.");
                return false;
            }

            PeerSocket peerSocket = peer.peerSocket;
            if (peerSocket == null
                || !peerSocket.TryAddSendData(data, offset, count, format, txrId, eventType)) {
                return false;
            }
            try {
                if (immediately)
                    peerSocket.TryStartSend();
                else {
                    var msg = new ServerCommand { type = ServerCommand.Type.Send, peer = peer };
                    _workThread.Post(in msg, this);
                }
            } catch (Exception e) {
                // 消息已经成功入队，发送驱动失败时仍返回true，由后续发送泵或连接错误流程处理。
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.TrySend():启动发送泵异常 {e}");
            }
            return true;
        }

        /// <summary>
        /// 向某个Peer发送一条文本数据.
        /// </summary>
        /// <param name="peer">某个peer</param>
        /// <param name="text">要发送的文本</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        /// <param name="immediately">是否立刻发送</param>
        public void Send(Peer peer, string text,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0,
            bool immediately = true)
        {
            TrySend(peer, text, format, txrId, eventType, immediately);
        }

        /// <summary>
        /// 尝试向指定Peer发送字符串数据，并返回消息是否成功进入发送队列。
        /// </summary>
        /// <param name="peer">目标Peer。</param>
        /// <param name="text">要发送的文本。</param>
        /// <param name="format">数据格式。</param>
        /// <param name="txrId">事务ID。</param>
        /// <param name="eventType">消息类型。</param>
        /// <param name="immediately">是否立刻尝试启动发送泵。</param>
        /// <returns>消息成功进入发送队列时返回true；Peer不可用、编码失败或触发背压时返回false。</returns>
        public bool TrySend(Peer peer, string text,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0,
            bool immediately = true)
        {
            if (peer == null || _disposed) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.TrySend():peer为null或者disposed,不能发送.");
                return false;
            }

            ByteBuffer buffer = null;
            try {
                if (string.IsNullOrEmpty(text)) {
                    return TrySend(peer, null, 0, 0, format, txrId, eventType, immediately);
                }
                // 直接编码到 buffer 内部数组
                buffer = GlobalBuffer.Inst.GetEncodedUtf8(text);
                return TrySend(peer, buffer.Bytes, 0, buffer.Length, format, txrId, eventType, immediately);
            } catch (Exception e) {
                if (LogProxy.Error != null)
                    LogProxy.Error($"DNServer.TrySend():发送文本异常 {e}");
                return false;
            } finally {
                buffer?.Recycle();
            }
        }

        /// <summary>
        /// 使用数据打包,然后添加到发送队列.
        /// </summary>
        /// <param name="peer">某个peer</param>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据的起始位置</param>
        /// <param name="count">数据的长度</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        public void AddSendData(Peer peer, byte[] data, int offset, int count,
            Format format = Format.Raw, int txrId = 0, int eventType = 0)
        {
            if (peer == null || _disposed) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.AddSendData():peer为null或者disposed,不能发送.");
                return;
            }

            // TODO: peer.peerSocket 可能为空，需确认调用前已完成初始化
            peer.peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
        }

        /// <summary>
        /// 尝试开始启动发送
        /// </summary>
        /// <param name="peer">目标peer</param>
        /// <param name="forceUseWorkThread">是否明确要求切换到服务器工作线程启动发送。</param>
        /// <returns>true表示确实启动了一个发送</returns>
        public bool TryStartSend(Peer peer, bool forceUseWorkThread = false)
        {
            if (peer == null || _workThread == null || _disposed) {
                return false;
            }

            if (forceUseWorkThread) {
                var msg = new ServerCommand { type = ServerCommand.Type.Send, peer = peer };
                _workThread.Post(in msg, this);
                return false;
            }
            return peer.peerSocket.TryStartSend();
        }

        /// <summary>
        /// 向所有的Token发送它们的待发送消息。
        /// </summary>
        public void SendAll()
        {
            if (_workThread == null || _disposed) {
                return;
            }

            var msg = new ServerCommand { type = ServerCommand.Type.SendAll };
            _workThread.Post(in msg, this);
        }

        #region 定时检查与启动回滚

        /// <summary>
        /// 创建并启用服务器状态检查定时器，只能在监听成功后调用。
        /// </summary>
        private void ResumeStatusTimer()
        {
            lock (_timerLock) {
                if (_timerLifecycleStopping || _disposed || !IsStarted) {
                    return;
                }

                if (_timer == null) {
                    _timer = new Timer(
                        OnStatusTimer,
                        null,
                        Timeout.Infinite,
                        Timeout.Infinite);
                }

                Volatile.Write(ref _timerEnabled, 1);
                try {
                    _timer.Change(
                        StatusTimerPeriodMilliseconds,
                        StatusTimerPeriodMilliseconds);
                } catch {
                    Volatile.Write(ref _timerEnabled, 0);
                    throw;
                }
            }
        }

        /// <summary>
        /// 永久停止当前定时器，并等待已经排入线程池的全部回调退出。
        /// </summary>
        private void StopStatusTimerAndWait()
        {
            Timer timer;
            Volatile.Write(ref _timerEnabled, 0);
            lock (_timerLock) {
                _timerLifecycleStopping = true;
                timer = _timer;
                _timer = null;
            }

            if (timer == null) {
                return;
            }

            if (ReferenceEquals(_executingStatusTimerOwner, this)) {
                timer.Dispose();
                return;
            }

            using (var disposeCompleted = new ManualResetEvent(false)) {
                if (timer.Dispose(disposeCompleted)) {
                    disposeCompleted.WaitOne();
                }
            }
        }

        /// <summary>
        /// 完成关闭或启动回滚流程对定时器生命周期的独占。
        /// </summary>
        private void FinishStatusTimerShutdown()
        {
            lock (_timerLock) {
                _timerLifecycleStopping = false;
            }
        }

        /// <summary>
        /// 在线程池回调中发送到期心跳，并合并投递一次状态检查。
        /// </summary>
        /// <param name="state">System.Threading.Timer传入的用户状态，本实现不使用。</param>
        private void OnStatusTimer(object state)
        {
            if (Volatile.Read(ref _timerEnabled) == 0) {
                return;
            }

            bool posted = false;
            bool ownsPendingFlag = false;
            DNServer previousTimerOwner = _executingStatusTimerOwner;
            _executingStatusTimerOwner = this;
            try {
                // 心跳属于连接控制面，直接由Timer线程发送，不能排在高频业务消息之后。
                SendDueHeartbeatsFromTimer();

                if (Volatile.Read(ref _timerEnabled) == 0
                    || Interlocked.CompareExchange(ref _timerCheckPending, 1, 0) != 0) {
                    return;
                }
                ownsPendingFlag = true;

                WorkThread<ServerCommand> workThread = _workThread;
                if (workThread == null) {
                    return;
                }

                // 离线删除与统计仍在单一工作线程串行执行。
                var check = new ServerCommand { type = ServerCommand.Type.TimerCheckStatus };
                workThread.Post(in check, this);
                posted = true;
            } catch (Exception exception) {
                try {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning("DNServer.OnStatusTimer():投递状态检查异常 " + exception);
                } catch {
                    // Timer线程不能让日志实现中的异常逃逸到线程池。
                }
            } finally {
                if (ownsPendingFlag && !posted) {
                    Interlocked.Exchange(ref _timerCheckPending, 0);
                }
                _executingStatusTimerOwner = previousTimerOwner;
            }
        }

        /// <summary>
        /// 处理工作队列清理时被丢弃的命令，释放对应的状态检查占用标记。
        /// </summary>
        /// <param name="command">被工作线程队列丢弃的命令。</param>
        private void OnCommandDiscarded(ServerCommand command)
        {
            if (command.type == ServerCommand.Type.TimerCheckStatus) {
                Interlocked.Exchange(ref _timerCheckPending, 0);
            }
        }

        /// <summary>
        /// 回滚监听启动失败后的定时器、监听对象和工作线程，同时保留外部事件绑定供重试使用。
        /// </summary>
        private void RollbackFailedStart()
        {
            try {
                StopStatusTimerAndWait();
            } catch (Exception exception) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("DNServer.RollbackFailedStart():停止Timer异常 " + exception);
            }

            try {
                _listenerSocket?.Dispose();
            } catch (Exception exception) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("DNServer.RollbackFailedStart():关闭监听Socket异常 " + exception);
            } finally {
                _listenerSocket = null;
            }

            PeerManager.Inst.DeleteAllPeer();

            try {
                _workThread?.Stop();
            } catch (Exception exception) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("DNServer.RollbackFailedStart():停止工作线程异常 " + exception);
            } finally {
                _workThread = null;
                Interlocked.Exchange(ref _timerCheckPending, 0);
                FinishStatusTimerShutdown();
            }
        }

        #endregion

        #region 工作线程

        /// <summary>
        /// 工作线程处理函数
        /// </summary>
        /// <param name="msg">要处理的消息。</param>
        /// <param name="waitTimeMs">这条消息等待了多长时间(ms)。</param>
        public void Handle(ref ServerCommand msg, double waitTimeMs)
        {
            if (waitTimeMs > 500 && msg.type != ServerCommand.Type.TimerCheckStatus) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.Handle():[{Name}]工作{msg.type}等待处理时间过长！waitTime:{waitTimeMs}ms");
            }

            switch (msg.type) {
                case ServerCommand.Type.Start:
                    DoStart(msg);
                    break;
                case ServerCommand.Type.Send:
                    DoSend(msg);
                    break;
                case ServerCommand.Type.Receive:
                    DoReceive(msg);
                    break;
                case ServerCommand.Type.SendAll:
                    DoSendAll(msg);
                    break;
                case ServerCommand.Type.TimerCheckStatus:
                    try {
                        DoTimerCheckStatus(msg);
                    } finally {
                        Interlocked.Exchange(ref _timerCheckPending, 0);
                    }
                    break;
            }
        }

        /// <summary>
        /// 工作线程内启动服务器
        /// </summary>
        /// <param name="msg">启动参数消息</param>
        private void DoStart(ServerCommand msg)
        {
            try {
                if (IsStarted)
                    return;

                if (LogProxy.Debug != null)
                    LogProxy.Debug("DNServer.DoStart()：工作线程开始执行DoStart()...");
                if (_listenerSocket != null) {
                    _listenerSocket.Dispose();
                }
                _listenerSocket = new ServerListenerSocket();
                _listenerSocket.EventAccept += OnListenerSocketAccept;

                if (LogProxy.Info != null)
                    LogProxy.Info("DNServer.DoStart(): _socketListener.Start(" + _port + ");");
                // msg.text1是服务器的主机IP,一般使用Any表示所有的可能IP
                _listenerSocket.Start(msg.text1, _port);
                if (LogProxy.Debug != null)
                    LogProxy.Debug("DNServer.DoStart()执行完毕！");
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("DNServer.DoStart()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 响应Timer的执行,目前是一秒一次
        /// </summary>
        private void DoTimerCheckStatus(ServerCommand msg)
        {
            var peers = PeerManager.Inst.GetAllPeer();
            try {
                for (int i = 0; i < peers.Count; i++) {
                    Peer peer = peers[i];

                    // 如果还有未提取的消息那么就再次提醒
                    if (peer.HasReceiveMsg) {
                        // EventPeerReceData?.Invoke(peer);
                        // dx: 注意这里不能直接发出事件,否则可能会导致消息的先后顺序不再是按顺序回复的.
                        // 如果全是是线程池,那么会先发出事件等阻塞处理完了再开始下一个接收,所以永远是按顺序的.
                        // 如果是工作线程给出接收事件,那么这里直接发出事件则有两个线程同时从消息队列中提取内容并启动异步回发,
                        // 那么就会导致消息的先后顺序乱掉.
                        // 如果所有的GetReceiveData()方法都由一个工作线程调用,那么不应该有先后顺序的问题.
                        // 如果有多个线程调用GetReceiveData()方法,那么应该加锁,并且加锁到Send()方法结束.
                        OnReceiveCompleted(peer); //让工作线程提醒Peer有数据可处理
                    }

                    if (Config.IsAutoHeartbeat) {
                        if (peer?.Status?.TimeSinceLastReceived > Config.HeartBeatCheckTime) {
                            if (LogProxy.Debug != null)
                                LogProxy.Debug($"DNServer.DoTimerCheckStatus():用户 [{peer.ID}] 长时间没有收到心跳包，被删除!");
                            PeerManager.Inst.DeletePeer(peer.ID, PeerErrorType.HeartBeatTimeout); //删除这个用户
                        }
                    }
                    if (Config.EnablePeerStatistics) {
                        peer.Status?.UpdateStatus();
                    }
                }
            } finally {
                ListPool<Peer>.Shared.Recycle(peers);
            }
        }

        /// <summary>
        /// 从 Timer 线程直接向到期 Peer 发送心跳。PeerSocket 的发送队列和发送状态使用并发队列及
        /// Interlocked 保护，因此这里不会与业务回复或工作线程发送发生并发冲突。
        /// </summary>
        private void SendDueHeartbeatsFromTimer()
        {
            if (!Config.IsAutoHeartbeat
                || Interlocked.CompareExchange(ref _heartbeatTimerRunning, 1, 0) != 0) {
                return;
            }

            try {
                var peers = PeerManager.Inst.GetAllPeer();
                try {
                    for (int index = 0; index < peers.Count; index++) {
                        Peer peer = peers[index];
                        PeerStatus status = peer?.Status;
                        if (status == null || status.TimeSinceLastSend <= Config.HeartBeatSendTime) {
                            continue;
                        }

                        if (Config.IsDebugMode && LogProxy.Debug != null)
                            LogProxy.Debug($"DNServer.SendDueHeartbeatsFromTimer():向用户 [{peer.ID}] 发送心跳包!");
                        peer.Send(null, 0, 0, Format.Heart);
                    }
                } finally {
                    ListPool<Peer>.Shared.Recycle(peers);
                }
            } catch (Exception exception) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("DNServer.SendDueHeartbeatsFromTimer():异常 " + exception);
            } finally {
                Interlocked.Exchange(ref _heartbeatTimerRunning, 0);
            }
        }

        /// <summary>
        /// 线程函数：发送
        /// </summary>
        /// <param name="msg">带peer和数据的消息</param>
        private void DoSend(ServerCommand msg)
        {
            Peer peer = msg.peer;
            try {
                if (peer == null || peer.peerSocket == null) {
                    throw new Exception("DNServer.DoSend():peer == null || peer.peerSocket == null");
                }
                // 添加该条数据,但是发送空数据呢?,这里这个判断将来去掉吧
                if (msg.data != null) {
                    peer.peerSocket.AddSendData(msg.data, 0, msg.data.Length);
                }
                // 尝试驱动一下
                peer.peerSocket.TryStartSend();
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.DoSend():{peer.Name} 异常 {e}");
            }
        }

        /// <summary>
        /// 线程函数，向所有Token发送他们的待发送数据
        /// </summary>
        /// <param name="msg">发送全部的消息</param>
        private void DoSendAll(ServerCommand msg)
        {
            try {
                var peers = PeerManager.Inst.GetAllPeer();
                for (int i = 0; i < peers.Count; i++) {
                    Peer peer = peers[i];
                    peer.peerSocket?.TryStartSend();
                }
                peers.Recycle();
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("DNServer.DoSendAll()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 线程函数,发出事件.
        /// </summary>
        /// <param name="msg">接收事件的消息</param>
        private void DoReceive(ServerCommand msg)
        {
            Peer peer = msg.peer;
            // dx: 在Fast模式下也会丢进来执行这个检查,所以有大量的消息要执行,
            // 所以必须加这个,不要去轻易锁掉peer.
            if (!peer.HasReceiveMsg) {
                return;
            }
            try {
                // 现在没有线程的解包,所以不需要工作
                // 发出数据事件(这里是否要去判断一下有没有未处理消息才发送?)
                lock (peer) {
                    // 这里再检查一下算了,不行就下次timer再检查,没消息就算了
                    if (!peer.HasReceiveMsg) {
                        return;
                    }
                    PeerReceived?.Invoke(this, peer);
                }
            } catch (Exception e) {
                if (LogProxy.Error != null)
                    LogProxy.Error($"DNServer.DoReceive(): {peer.Name}执行事件 EventPeerReceData 异常 {e}");
            }
        }

        #endregion

        #region Socket事件响应

        /// <summary>
        /// 监听器接受到新的连接
        /// </summary>
        /// <param name="acceptSocket">已接受的Socket</param>
        private void OnListenerSocketAccept(Socket acceptSocket)
        {
            Peer peer = new Peer(); //创建一个用户
            peer.ownerServer = this;
            PeerManager.Inst.AddPeer(peer); //把这个用户加入TokenManager,分配一个ID

            peer.peerSocket.ErrorOccurred +=
                (ps, eventError) => {
                    Peer p = ps.User as Peer;
                    if (p == null)
                        return;
                    PeerErrorOccurred?.Invoke(this, p, PeerErrorType.SocketError);
                    if (LogProxy.Info != null)
                        LogProxy.Info($"客户端{p.ID}发生错误,删除它");
                    PeerManager.Inst.DeletePeer(p.ID, PeerErrorType.SocketError); //关闭Token
                };
            peer.peerSocket.ReceiveCompleted +=
                (ps) => {
                    Peer p = ps.User as Peer;
                    if (p == null)
                        return;
                    // dx: 快速模式这里给它挂上这个事件,这样可以第一时间响应发出事件.
                    // 但是注意这样的做法在数据量特别大的时候是有一定线程的压力的.
                    // 如果走这里,那么不执行完事件函数不会开启下一次接收.
                    if (IsFastResponse) {
                        try {
                            // 因为要支持在timer中查看是否有未处理的事件,所以这里需要加锁,让事件中的工作可以串行
                            lock (peer) {
                                PeerReceived?.Invoke(this, p);
                            }
                        } catch (Exception e) {
                            if (LogProxy.Error != null)
                                LogProxy.Error($"DNServer:{p.Name}执行事件 EventPeerReceData 异常 {e}");
                        }
                    }
                    // 在有的时候执行事件的时候从队列中提取不出消息,所以这里丢工作线程,双保险算了
                    OnReceiveCompleted(p);
                };

            // 先绑定全部生命周期回调，再启动底层接收，避免高速连接在事件绑定前完成或断开。
            peer.peerSocket.SetAcceptSocket(acceptSocket); // 这里会初始化args,会使用name
            if (peer.peerSocket == null) return;

            try {
                PeerConnected?.Invoke(this, peer);
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.OnListenerSocketAccept():执行PeerConnected事件异常 {e}");
            }
        }

        /// <summary>
        /// 由PeerManager在取得唯一删除权后通知当前服务器连接已经断开。
        /// </summary>
        /// <param name="peer">即将释放的Peer。</param>
        /// <param name="errorType">连接被移除的原因。</param>
        internal void NotifyPeerDisconnected(Peer peer, PeerErrorType errorType)
        {
            PeerDisconnected?.Invoke(this, peer, errorType);
        }

        /// <summary>
        /// PeerSocket的接收成功的事件,发出消息让工作线程去给出对外事件.
        /// </summary>
        /// <param name="peer">已接收消息的peer</param>
        private void OnReceiveCompleted(Peer peer)
        {
            // TODO: Close 后 _workThread 可能为 null，需确认调用时机
            // 让工作线程处理
            var msg = new ServerCommand { type = ServerCommand.Type.Receive, peer = peer };
            _workThread.Post(in msg, this);

            //DoReceive(msg);//debug:直接使用这个小线程（结果：貌似性能没有明显提高，也貌似没有稳定性的问题）
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try {
                Close();
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("DNServer.Dispose():异常 " + e.Message);
            }
        }

        #endregion
    }
}
