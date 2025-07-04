using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 通信传输的服务器类
    /// </summary>
    public class DNServer : IWorkHandler<SwMessage>
    {
        private static readonly Lazy<DNServer> _instance = new Lazy<DNServer>(() => new DNServer());

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
        private WorkThread<SwMessage> _workThread;

        /// <summary>
        /// 底层的通信类
        /// </summary>
        private ServerListenerSocket _listenerSocket;

        /// <summary>
        /// 一个定时器
        /// </summary>
        private Timer _timer;

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
        /// 快速响应,如果为true，则会直接在.net小线程进入接收回调的时候直接进入响应事件。
        /// 如果服务器压力特别大,则应该使用false.
        /// 默认为true.
        /// </summary>
        public bool IsFastResponse { get; set; } = true;

        /// <summary>
        /// 事件：某个Peer发生了错误后，会自动调用关闭，这是错误及关闭事件
        /// </summary>
        public event Action<DNServer, Peer, PeerErrorType> EventPeerError;

        /// <summary>
        /// 事件：某个Peer接收到了数据，可以将轻量任务加入这个事件，这是.net线程池中的小线程,这里一定要快速处理
        /// </summary>
        public event Action<DNServer, Peer> EventPeerReceData;

        /// <summary>
        /// 启动服务器，会开启工作线程.
        /// </summary>
        /// <param name="port">端口号</param>
        /// <param name="hostName">服务器的主机IP,一般使用Any表示所有的可能IP</param>
        public void Start(int port, string hostName = "Any")
        {
            if (IsStarted)
                return;

            try {
                LogProxy.LogDebug("DNServer.Start():服务器启动...");
                _port = port;

                PeerManager.Inst.DeleteAllPeer();

                // 工作线程总是启动
                if (_workThread == null) {
                    _workThread = new WorkThread<SwMessage>("DNServerWorkThread");
                }
                _workThread.ClearQueue();

                // 定时检查的定时器
                if (_timer == null) {
                    _timer = new Timer(state => {
                        // 让工作线程定时检查
                        var check = new SwMessage { type = SwMessage.Type.TimerCheckStatus };
                        _workThread.Post(in check, this);
                    });
                    _timer.Change(1000, 1000); //一秒后启动
                }

                // 直接启动监听
                if (_listenerSocket == null) {
                    _listenerSocket = new ServerListenerSocket();
                    _listenerSocket.EventAccept += OnListenerSocketAccept;

                    // msg.text1是服务器的主机IP,一般使用Any表示所有的可能IP
                    _listenerSocket.Start(hostName, port);
                }
                if (!_listenerSocket.IsStarted) {
                    LogProxy.LogError("DNServer.Start():启动监听失败");
                    return;
                }
                LogProxy.Log($"DNServer.Start():服务器启动成功,端口:{_port}");
            } catch (Exception e) {
                LogProxy.LogError("DNServer.Start():异常 " + e.Message);
            }
        }

        /// <summary>
        /// 关闭服务器
        /// </summary>
        /// <param name="clearEvent">是否清除事件绑定</param>
        public void Close(bool clearEvent = true)
        {
            LogProxy.Log("DNServer.Close():准备关闭Socket和停止工作线程...");
            lock (this) {
                try {
                    _timer?.Dispose();
                } catch (Exception e) {
                    LogProxy.LogWarning($"DNServer.Close():停止Timer异常 {e}");
                } finally {
                    _timer = null;
                }

                // 是否要删除所有客户端?
                PeerManager.Inst.DeleteAllPeer();

                try {
                    if (_workThread != null) {
                        _workThread.Stop();
                    }
                } catch (Exception e) {
                    LogProxy.LogWarning($"DNServer.Close():停止工作线程异常 {e}");
                } finally {
                    _workThread = null;
                }
                try {
                    if (_listenerSocket != null) {
                        _listenerSocket.Dispose();
                    }
                } catch (Exception e) {
                    LogProxy.LogWarning($"DNServer.Close():关闭Socket异常 {e}");
                } finally {
                    _listenerSocket = null;
                }

                if (clearEvent) {
                    LogProxy.Log("DNServer.Close():清空了所有绑定事件...");
                    EventPeerError = null;
                    EventPeerReceData = null;
                }

                // 重置设置
                IsFastResponse = true;
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
            Peer peer = PeerManager.Inst.GetPeer(peerId);
            Send(peer, data, offset, count, format, txrId, eventType, immediately);
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
            peer.peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
            if (immediately)
                peer.peerSocket.TryStartSend();
            else {
                var msg = new SwMessage { type = SwMessage.Type.Send, peer = peer };
                _workThread.Post(in msg, this);
            }
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
            try {
                if (string.IsNullOrEmpty(text)) {
                    Send(peer, null, 0, 0, format, txrId, eventType, immediately);
                    return;
                }
                byte[] data = Encoding.UTF8.GetBytes(text);
                Send(peer, data, 0, data.Length, format, txrId, eventType, immediately);
            } catch (Exception e) {
                LogProxy.LogError($"DNServer.Send():发送文本异常 {e}");
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
            peer.peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
        }

        /// <summary>
        /// 尝试开始启动发送
        /// </summary>
        /// <param name="peer"></param>
        /// <param name="forceUseWorkThread"></param>
        /// <returns>true表示确实启动了一个发送</returns>
        public bool TryStartSend(Peer peer, bool forceUseWorkThread = true)
        {
            if (peer.peerSocket.TryStartSend() && forceUseWorkThread == false)
                return true;
            // 在超高并发的时候TryStartSend()可能会漏掉一个发送,所以这里用工作线程再次尝试
            var msg = new SwMessage { type = SwMessage.Type.Send, peer = peer };
            _workThread.Post(in msg, this);
            return false;
        }

        /// <summary>
        /// 向所有的Token发送它们的待发送消息。
        /// </summary>
        public void SendAll()
        {
            var msg = new SwMessage { type = SwMessage.Type.SendAll };
            _workThread.Post(in msg, this);
        }

        #region 工作线程

        /// <summary>
        /// 工作线程处理函数
        /// </summary>
        /// <param name="msg"></param>
        public void Handle(ref SwMessage msg)
        {
            switch (msg.type) {
                case SwMessage.Type.Start:
                    DoStart(msg);
                    break;
                case SwMessage.Type.Send:
                    DoSend(msg);
                    break;
                case SwMessage.Type.Receive:
                    DoReceive(msg);
                    break;
                case SwMessage.Type.SendAll:
                    DoSendAll(msg);
                    break;
                case SwMessage.Type.TimerCheckStatus:
                    DoTimerCheckStatus(msg);
                    break;
            }
        }

        private void DoStart(SwMessage msg)
        {
            try {
                if (IsStarted)
                    return;

                LogProxy.LogDebug("DNServer.DoStart()：工作线程开始执行DoStart()...");
                if (_listenerSocket != null) {
                    _listenerSocket.Dispose();
                }
                _listenerSocket = new ServerListenerSocket();
                _listenerSocket.EventAccept += OnListenerSocketAccept;

                LogProxy.Log("DNServer.DoStart(): _socketListener.Start(" + _port + ");");
                // msg.text1是服务器的主机IP,一般使用Any表示所有的可能IP
                _listenerSocket.Start(msg.text1, _port);
                LogProxy.LogDebug("DNServer.DoStart()执行完毕！");
            } catch (Exception e) {
                LogProxy.LogWarning("DNServer.DoStart()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 响应Timer的执行,目前是一秒一次
        /// </summary>
        private void DoTimerCheckStatus(SwMessage msg)
        {
            var peers = PeerManager.Inst.GetAllPeer();
            for (int i = 0; i < peers.Count; i++) {
                Peer peer = peers[i];

                // 驱动一下未发送的数据,按理这里不需要,这是最后一道保险.
                if (peer.TryStartSend()) {
                    // dx: 有的时候似乎是服务器正在合并发送的时候,刚好的部分触发了.所以这里也不需要打日志
                    //LogProxy.LogWarning($"DNServer.DoTimerCheckStatus():{peer.Name}这里TryStartSend成功了,这是不太应该的");
                }

                // 如果还有未提取的消息那么就再次提醒,可能需要限制 && IsFastResponse
                if (peer.HasReceiveMsg) {
                    // EventPeerReceData?.Invoke(peer);
                    // dx: 注意这里不能直接发出事件,否则可能会导致消息的先后顺序不再是按顺序回复的.
                    // 如果全是是线程池,那么会先发出事件等阻塞处理完了再开始下一个接收,所以永远是按顺序的.
                    // 如果是工作线程给出接收事件,那么这里直接发出事件则有两个线程同时从消息队列中提取内容并启动异步回发,
                    // 那么就会导致消息的先后顺序乱掉.
                    // 如果所有的GetReceiveData()方法都由一个工作线程调用,那么不应该有先后顺序的问题啊
                    OnReceiveCompleted(peer); //让工作线程提醒Peer有数据可处理
                }

                if (Config.IsAutoHeartbeat) {
                    if (peer?.Status?.TimeSinceLastReceived > Config.HeartBeatCheckTime) {
                        LogProxy.LogDebug($"DNServer.DoTimerCheckStatus():用户 [{peer.ID}] 长时间没有收到心跳包，被删除!");
                        PeerManager.Inst.DeletePeer(peer.ID, PeerErrorType.HeartBeatTimeout); //删除这个用户
                    }
                    if (peer?.Status?.TimeSinceLastSend > Config.HeartBeatSendTime) {
                        peer.Send(null, 0, 0, Format.Heart); //发个心跳包
                    }
                }
            }
            ListPool<Peer>.Shared.Recycle(peers);
        }

        /// <summary>
        /// 线程函数：发送
        /// </summary>
        /// <param name="msg">这个消息参数带一个peer</param>
        private void DoSend(SwMessage msg)
        {
            try {
                Peer peer = msg.peer;
                if (peer == null) {
                    return;
                }
                // 添加该条数据,但是发送空数据呢?,这里这个判断将来去掉吧
                if (msg.data != null) {
                    peer.peerSocket.AddSendData(msg.data, 0, msg.data.Length);
                }
                // 尝试驱动一下
                peer.peerSocket.TryStartSend();
            } catch (Exception e) {
                LogProxy.LogWarning("DNServer.DoSend()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 线程函数，向所有Token发送他们的待发送数据
        /// </summary>
        /// <param name="msg"></param>
        private void DoSendAll(SwMessage msg)
        {
            try {
                var peers = PeerManager.Inst.GetAllPeer();
                for (int i = 0; i < peers.Count; i++) {
                    Peer peer = peers[i];
                    peer.peerSocket.TryStartSend();
                }
                ListPool<Peer>.Shared.Recycle(peers);
            } catch (Exception e) {
                LogProxy.LogWarning("DNServer.DoSendAll()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 线程函数,发出事件.
        /// </summary>
        /// <param name="msg"></param>
        private void DoReceive(SwMessage msg)
        {
            try {
                Peer peer = msg.peer;
                // 现在没有线程的解包,所以不需要工作
                // 发出数据事件(这里是否要去判断一下有没有未处理消息才发送?)
                EventPeerReceData?.Invoke(this, peer);
            } catch (Exception e) {
                LogProxy.LogWarning($"DNServer.DoReceive()：异常 {e}");
            }
        }

        #endregion

        #region Socket事件响应

        private void OnListenerSocketAccept(Socket acceptSocket)
        {
            Peer peer = new Peer(); //创建一个用户
            PeerManager.Inst.AddPeer(peer); //把这个用户加入TokenManager,分配一个ID

            peer.peerSocket.SetAcceptSocket(acceptSocket);// 这里会初始化args,会使用name
            peer.peerSocket.EventError += (ps, eventError) => {
                Peer p = PeerManager.Inst.GetPeer(ps.ID);
                if (p == null)
                    return;
                EventPeerError?.Invoke(this, p, PeerErrorType.SocketError);
                LogProxy.Log($"客户端{p.ID}发生错误,删除它");
                PeerManager.Inst.DeletePeer(p.ID, PeerErrorType.SocketError); //关闭Token

            };
            peer.peerSocket.EventReceiveCompleted += (ps) => {
                // dx: 注意这里给它挂上这个事件,这样可以第一时间响应发出事件.
                // 但是注意这样的做法在数据量特别大的时候是有一定线程的压力的.
                // 如果走这里,那么不执行完事件函数不会开启下一次接收.
                Peer p = PeerManager.Inst.GetPeer(ps.ID);
                if (p == null)
                    return;
                if (IsFastResponse) {
                    EventPeerReceData?.Invoke(this, p);
                }
                else {
                    // 这是之前的发出消息让工作线程发出对外的事件.
                    // TODO: 目前实测发现这里如果这样使用会存在一个echo服务器返回消息顺序不一致的问题...
                    // 如果所有的GetReceiveData()方法都由一个工作线程调用,那么不应该有先后顺序的问题啊,
                    // 想不出来是为什么
                    OnReceiveCompleted(p);
                }
            };

        }

        /// <summary>
        /// PeerSocket的接收成功的事件,发出消息让工作线程去给出对外事件.
        /// </summary>
        /// <param name="peer"></param>
        private void OnReceiveCompleted(Peer peer)
        {
            // 让工作线程处理
            var msg = new SwMessage { type = SwMessage.Type.Receive, peer = peer };
            _workThread.Post(in msg, this);

            //DoReceive(msg);//debug:直接使用这个小线程（结果：貌似性能没有明显提高，也貌似没有稳定性的问题）
        }

        private void OnSendSendCompleted(Peer peer)
        {
            if (peer.peerSocket.WaitSendMsgCount > 0) //如果待发送队列里有消息这里应该不需要这个判断了token.SendingCount < MAX_TOKEN_SENDING_COUNT &&
            {
                var msg = new SwMessage { type = SwMessage.Type.Send };
                _workThread.Post(in msg, this);
            }
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
                LogProxy.LogWarning("DNServer.Dispose():异常 " + e.Message);
            }
        }

        #endregion
    }
}
