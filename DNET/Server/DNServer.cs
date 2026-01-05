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
        /// <summary>
        /// 单例实例懒加载容器
        /// </summary>
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
        public event Action<DNServer, Peer, PeerErrorType> EventPeerError;

        /// <summary>
        /// 事件：某个Peer接收到了数据，注意如果可能会有多线程执行这个事件的时候(如IsFastResponse=True的时候)
        /// 如果特别关心回复的发送顺序,那么要对回调函数中的Peer进行加锁.
        /// </summary>
        public event Action<DNServer, Peer> EventPeerReceData;

        /// <summary>
        /// 启动服务器，会开启工作线程.
        /// </summary>
        /// <param name="port">端口号</param>
        /// <param name="hostName">服务器的主机IP,一般使用Any表示所有的可能IP</param>
        /// <param name="backlog">监听队列的长度</param>
        public void Start(int port, string hostName = "Any", int backlog = 64)
        {
            if (IsStarted)
                return;

            try {
                if (LogProxy.Debug != null)
                    LogProxy.Debug("DNServer.Start():服务器尝试启动...");
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
                    _listenerSocket.Start(hostName, port, backlog);
                }
                if (!_listenerSocket.IsStarted) {
                    if (LogProxy.Error != null)
                        LogProxy.Error("DNServer.Start():启动监听失败");
                    return;
                }
                if (LogProxy.Info != null)
                    LogProxy.Info($"DNServer.Start():服务器启动成功,端口:{_port}");
            } catch (Exception e) {
                if (LogProxy.Error != null)
                    LogProxy.Error("DNServer.Start():异常 " + e.Message);
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
                    _timer?.Dispose();
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNServer.Close():停止Timer异常 {e}");
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
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNServer.Close():停止工作线程异常 {e}");
                } finally {
                    _workThread = null;
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
                    EventPeerError = null;
                    EventPeerReceData = null;
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
            Peer peer = PeerManager.Inst.GetPeer(peerId);
            if (peer == null || _disposed) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.Send():peer为null或者disposed,不能发送.");
                return;
            }

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
            if (peer == null || _disposed) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.Send():peer为null或者disposed,不能发送.");
                return;
            }

            // TODO: peer.peerSocket 为空时会抛出异常，必要时增加保护或提前校验
            peer.peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
            if (immediately)
                peer.peerSocket.TryStartSend();
            else {
                // TODO: _workThread 可能为 null，需确认在调用前已启动工作线程
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
            if (peer == null || _disposed) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.Send():peer为null或者disposed,不能发送.");
                return;
            }

            try {
                if (string.IsNullOrEmpty(text)) {
                    Send(peer, null, 0, 0, format, txrId, eventType, immediately);
                    return;
                }
                // 直接编码到 buffer 内部数组
                ByteBuffer buffer = GlobalBuffer.Inst.GetEncodedUtf8(text);
                Send(peer, buffer.Bytes, 0, buffer.Length, format, txrId, eventType, immediately);
                buffer.Recycle();
            } catch (Exception e) {
                if (LogProxy.Error != null)
                    LogProxy.Error($"DNServer.Send():发送文本异常 {e}");
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
        /// <param name="forceUseWorkThread">是否强制使用工作线程补偿发送</param>
        /// <returns>true表示确实启动了一个发送</returns>
        public bool TryStartSend(Peer peer, bool forceUseWorkThread = true)
        {
            if (peer == null || _workThread == null || _disposed) {
                return false;
            }

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
            if (_workThread == null || _disposed) {
                return;
            }

            var msg = new SwMessage { type = SwMessage.Type.SendAll };
            _workThread.Post(in msg, this);
        }

        #region 工作线程

        /// <summary>
        /// 工作线程处理函数
        /// </summary>
        /// <param name="msg">要处理的消息。</param>
        /// <param name="waitTimeMs">这条消息等待了多长时间(ms)。</param>
        public void Handle(ref SwMessage msg, double waitTimeMs)
        {
            if (waitTimeMs > 500 && msg.type != SwMessage.Type.TimerCheckStatus) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.Handle():[{Name}]工作{msg.type}等待处理时间过长！waitTime:{waitTimeMs}ms");
            }

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

        /// <summary>
        /// 工作线程内启动服务器
        /// </summary>
        /// <param name="msg">启动参数消息</param>
        private void DoStart(SwMessage msg)
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
                    if (peer?.Status?.TimeSinceLastSend > Config.HeartBeatSendTime) {
                        peer.Send(null, 0, 0, Format.Heart); //发个心跳包
                    }
                }
                if (Config.EnablePeerStatistics) {
                    peer.Status?.UpdateStatus();
                }
            }
            ListPool<Peer>.Shared.Recycle(peers);
        }

        /// <summary>
        /// 线程函数：发送
        /// </summary>
        /// <param name="msg">带peer和数据的消息</param>
        private void DoSend(SwMessage msg)
        {
            Peer peer = msg.peer;
            try {
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
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNServer.DoSend():{peer.Name} 异常 {e}");
            }
        }

        /// <summary>
        /// 线程函数，向所有Token发送他们的待发送数据
        /// </summary>
        /// <param name="msg">发送全部的消息</param>
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
                if (LogProxy.Warning != null)
                    LogProxy.Warning("DNServer.DoSendAll()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 线程函数,发出事件.
        /// </summary>
        /// <param name="msg">接收事件的消息</param>
        private void DoReceive(SwMessage msg)
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
                    EventPeerReceData?.Invoke(this, peer);
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
            PeerManager.Inst.AddPeer(peer); //把这个用户加入TokenManager,分配一个ID

            peer.peerSocket.SetAcceptSocket(acceptSocket); // 这里会初始化args,会使用name
            peer.peerSocket.EventError +=
                (ps, eventError) => {
                    Peer p = ps.User as Peer;
                    if (p == null)
                        return;
                    EventPeerError?.Invoke(this, p, PeerErrorType.SocketError);
                    if (LogProxy.Info != null)
                        LogProxy.Info($"客户端{p.ID}发生错误,删除它");
                    PeerManager.Inst.DeletePeer(p.ID, PeerErrorType.SocketError); //关闭Token
                };
            peer.peerSocket.EventReceiveCompleted +=
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
                                EventPeerReceData?.Invoke(this, p);
                            }
                        } catch (Exception e) {
                            if (LogProxy.Error != null)
                                LogProxy.Error($"DNServer:{p.Name}执行事件 EventPeerReceData 异常 {e}");
                        }
                    }
                    // 在有的时候执行事件的时候从队列中提取不出消息,所以这里丢工作线程,双保险算了
                    OnReceiveCompleted(p);
                };
        }

        /// <summary>
        /// PeerSocket的接收成功的事件,发出消息让工作线程去给出对外事件.
        /// </summary>
        /// <param name="peer">已接收消息的peer</param>
        private void OnReceiveCompleted(Peer peer)
        {
            // TODO: Close 后 _workThread 可能为 null，需确认调用时机
            // 让工作线程处理
            var msg = new SwMessage { type = SwMessage.Type.Receive, peer = peer };
            _workThread.Post(in msg, this);

            //DoReceive(msg);//debug:直接使用这个小线程（结果：貌似性能没有明显提高，也貌似没有稳定性的问题）
        }

        /// <summary>
        /// 发送完成后的补偿处理
        /// </summary>
        /// <param name="peer">发送完成的peer</param>
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
                if (LogProxy.Warning != null)
                    LogProxy.Warning("DNServer.Dispose():异常 " + e.Message);
            }
        }

        #endregion
    }
}
