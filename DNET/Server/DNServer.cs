//#define Multitask //是否多核心,依赖.net4.0。速度没有提升，CPU占用加大。

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DNET.Protocol;
using System.Threading.Tasks;


namespace DNET
{
    /// <summary>
    /// 通信传输的服务器类，默认通信数据包打包方法类的类型为FastPacket。
    /// </summary>
    public class DNServer
    {
        private static readonly Lazy<DNServer> _instance = new Lazy<DNServer>(() => new DNServer());

        /// <summary>
        /// 单例
        /// </summary>
        public static DNServer Inst => _instance.Value;

        /// <summary>
        /// 构造函数
        /// </summary>
        private DNServer()
        {
            _packet = new SimplePacket();

            ServerTimer.GetInstance().Start();

            status = new ServerStatus(this); //创建状态统计
            status.BindTimer(ServerTimer.GetInstance()); //绑定一个计时器
            status.isPrintCur1s = false; //默认不打印状态统计（1s一打印）
        }

        /// <summary>
        /// 事件：某个Peer发生了错误后，会自动调用关闭，这是错误及关闭事件
        /// </summary>
        public event Action<Peer, PeerErrorType> EventPeerError;

        /// <summary>
        /// 事件：某个Peer接收到了数据，可以将轻量任务加入这个事件，交给数据解包线程
        /// </summary>
        public event Action<Peer> EventPeerReceData;

        /// <summary>
        /// 最大客户端连接数（未使用）
        /// </summary>
        private int MAX_CONNECTIONS = 4;

        /// <summary>
        /// 消息队列最大数(服务器的消息队列最大长度应该比较长才对，它基本等于狂发情况下的同时在线人数)
        /// </summary>
        private int MSG_QUEUE_CAPACITY = 8192 * 4;

        /// <summary>
        /// Token的最大发送数
        /// </summary>
        private int MAX_TOKEN_SENDING_COUNT = 1;

        /// <summary>
        /// 工作线程组
        /// </summary>
        private Thread[] _workThread;

        /// <summary>
        /// 对应一条消息的信号量
        /// </summary>
        private Semaphore _msgSemaphore;

        /// <summary>
        /// 和U3D主模块之间的通信的消息队列
        /// </summary>
        private DQueue<NetWorkTaskArgs> _taskArgsQueue;

        /// <summary>
        /// 当前的信号量计数
        /// </summary>
        private int _curSemCount = 0;

        /// <summary>
        /// 服务器端口号
        /// </summary>
        private int _port; //9900

        /// <summary>
        /// 底层的通信类
        /// </summary>
        private ServerListenerSocket _listenerSocket = null;

        /// <summary>
        /// 打包解包器
        /// </summary>
        private IPacket3 _packet;

        /// <summary>
        /// 发出消息处理等待警告时的时间长度，会逐级递增和递减.
        /// </summary>
        private int _warringWaitTime = 500;

        /// <summary>
        /// 标记是否已经被disposed
        /// </summary>
        private bool disposed = true;

        /// <summary>
        /// 服务器工作状态（只是字段，外面使用自行注意安全）
        /// </summary>
        public ServerStatus status;

        /// <summary>
        /// 服务器启动成功标志
        /// </summary>
        public bool IsStarted {
            get {
                if (_listenerSocket != null) {
                    return _listenerSocket.IsStarted;
                }
                else {
                    return false;
                }
            }
        }

        /// <summary>
        /// 当前的消息队列长度
        /// </summary>
        public int msgQueueLength { get { return _taskArgsQueue.Count; } }

        #region Exposed Function

        /// <summary>
        /// 启动服务器，会开启工作线程然后释放一个DoStart信号量。
        /// </summary>
        /// <param name="port">端口号</param>
        /// <param name="threadCount">服务器使用的处理线程数量</param>
        /// <param name="hostName">服务器的主机IP,一般使用Any表示所有的可能IP</param>
        public void Start(int port, int threadCount = 1, string hostName = "Any")
        {
            try {
                LogProxy.LogDebug("DNServer.Start()：服务器工作线程数 " + threadCount);
                if (disposed) {
                    PeerManager.Inst.DeleteAllPeer();
                    _taskArgsQueue = new DQueue<NetWorkTaskArgs>(MSG_QUEUE_CAPACITY);
                    _msgSemaphore = new Semaphore(0, MSG_QUEUE_CAPACITY);

                    _workThread = new Thread[threadCount];
                    for (int i = 0; i < threadCount; i++) {
                        _workThread[i] = new Thread(DoWork);
                        _workThread[i].IsBackground = true;
                        //工作线程的优先级(影响不大)
                        _workThread[i].Priority = ThreadPriority.Highest;
                        _workThread[i].Name = "SeverThread " + i;
                        _workThread[i].Start(); //启动线程
                    }

                    disposed = false;
                }

                _port = port;
                NetWorkTaskArgs msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.S_Start, null);
                msg.text1 = hostName;
                AddTask(msg);
            } catch (Exception e) {
                LogProxy.LogError("DNServer.Start()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 关闭服务器
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        /// <summary>
        /// 向某个token发送一条数据。
        /// </summary>
        /// <param name="peerId">tokenID</param>
        /// <param name="data">要发送的数据</param>
        public void Send(int peerId, byte[] data, Format format = Format.Raw, int txrId = 0, int eventType = 0)
        {
            //DxDebug.Log("信号量： 发送");
            //NetWorkTaskArgs msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.S_Send, data, peerId);
            Peer peer = PeerManager.Inst.GetPeer(peerId);
            Send(peer, data, format, txrId, eventType);
        }

        /// <summary>
        /// 向某个Peer发送一条数据.
        /// </summary>
        /// <param name="peer"></param>
        /// <param name="data"></param>
        /// <param name="format"></param>
        /// <param name="txrId"></param>
        /// <param name="eventType"></param>
        public void Send(Peer peer, byte[] data, Format format = Format.Raw, int txrId = 0, int eventType = 0)
        {
            //NetWorkTaskArgs msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.S_Send, null, peer.ID);
            peer.peerSocket.AddSendData(data, 0, data.Length, format, txrId, eventType);
            peer.peerSocket.TryBeginSend();
            //msg.peer = peer;
            //AddTask(msg);
        }

        /// <summary>
        /// 向某个Peer发送一条数据.
        /// </summary>
        /// <param name="peer"></param>
        /// <param name="text"></param>
        /// <param name="format"></param>
        /// <param name="txrId"></param>
        /// <param name="eventType"></param>
        public void Send(Peer peer, string text, Format format = Format.Raw, int txrId = 0, int eventType = 0)
        {
            try {
                byte[] data = null;
                if (string.IsNullOrEmpty(text)) {
                    Send(peer, data);
                    return;
                }
                data = Encoding.UTF8.GetBytes(text);
                Send(peer, data, format, txrId, eventType);
            } catch (Exception e) {
                LogProxy.LogError($"DNServer.Send()：异常 {e}");
            }
        }

        /// <summary>
        /// 向所有的Token发送它们的待发送消息。
        /// </summary>
        public void SendAll()
        {
            //DxDebug.Log("信号量： 发送All");
            AddTask(new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.S_SendAll, null)); //无参数
        }

        /// <summary>
        /// 加入一条要执行的消息，如果加入的过快而无法发送，则将产生信号量溢出异常，表明当前发送数据频率要大于系统能力
        /// </summary>
        /// <param name="taskArgs"></param>
        internal void AddTask(NetWorkTaskArgs taskArgs)
        {
            try {
                if (taskArgs != null)
                    _taskArgsQueue.Enqueue(taskArgs);


                try {
                    // 如果加入的过快而无法发送，则将产生信号量溢出异常,但是不会影响程序的唤醒
                    if (_curSemCount < 4) //信号量剩余较少的时候才去释放信号量
                    {
                        Interlocked.Increment(ref _curSemCount);
                        _msgSemaphore.Release(); // 无脑释放信号量,catch一下好了
                    }
                } catch (Exception) {
                }

                //catch (SemaphoreFullException) {
                //    //当前发送数据频率要大于系统能力，可尝试增加消息队列长度
                //    string msgtype = "";
                //    switch (msg.type) {
                //        case NetWorkMsg.Tpye.S_Start:
                //            msgtype = "S_Start";
                //            break;

                //        case NetWorkMsg.Tpye.S_Send:
                //            msgtype = "S_Send";
                //            break;

                //        case NetWorkMsg.Tpye.S_Receive:
                //            msgtype = "S_Receive";
                //            break;

                //        default:
                //            break;
                //    }
                //    LogProxy.LogError("DNServer.AddMessage():大于系统能力，当前最后一条：" + msgtype);
            } catch (Exception e) {
                LogProxy.LogError($"DNServer.AddMessage():异常 {e}");
            }
        }

        #endregion Exposed Function

        #region Thread Function

        private void DoWork()
        {
            LogProxy.LogDebug("DNServer.DoWork():服务器线程启动！");
            while (true) {
                _msgSemaphore.WaitOne();
                Interlocked.Decrement(ref _curSemCount);
                while (true) {
#if Multitask
                    NetWorkMsg msg1 = _msgQueue.Dequeue();
                    NetWorkMsg msg2 = _msgQueue.Dequeue();
                    if (msg1==null&& msg2==null)
                    {
                        break;
                    }
                    else if (msg1 != null && msg2 != null)//取到了两条消息
                    {
                        //再消耗一条信号量
                        //_msgSemaphore.WaitOne();
                        //Interlocked.Decrement(ref _curSemCount);
                        Parallel.Invoke(delegate () { ProcessMsg(msg1); }, delegate () { ProcessMsg(msg2); });
                    }
                    else if (msg1 != null && msg2 == null)
                    {
                        //只有一条消息，就直接执行
                        ProcessMsg(msg1);
                    }
                    else if (msg1 == null && msg2 != null)
                    {
                        //只有一条消息，就直接执行
                        ProcessMsg(msg2);
                    }
#else
                    NetWorkTaskArgs taskArg = _taskArgsQueue.Dequeue();
                    if (taskArg == null) //直到消息取尽之前都不停的处理
                    {
                        break;
                    }
                    float waitTime = (DateTime.Now.Ticks - taskArg.timeTickCreat) / 10000; //毫秒
                    if (waitTime > _warringWaitTime) {
                        _warringWaitTime += 500;
                        LogProxy.LogWarning("DNServer.DoWork():NetWorkMsg等待处理时间过长！waitTime:" + waitTime);
                    }
                    else if ((_warringWaitTime - waitTime) > 500) {
                        _warringWaitTime -= 500;
                    }

                    ProcessTaskArgs(taskArg);
#endif
                }
            }
        }

        private void ProcessTaskArgs(NetWorkTaskArgs msg)
        {
            if (msg != null) {
                //DxDebug.Log("取到了一条消息,当前队列长度： " + _msgQueue.Count);
                switch (msg.type) {
                    case NetWorkTaskArgs.Tpye.S_Start:
                        DoStart(msg);
                        break;

                    case NetWorkTaskArgs.Tpye.S_Send:
                        DoSend(msg);
                        break;

                    case NetWorkTaskArgs.Tpye.S_Receive:
                        //DxDebug.Log("DoReceive()");
                        DoReceive(msg);
                        //DxDebug.Log("DoReceive() 结束");
                        break;

                    case NetWorkTaskArgs.Tpye.S_SendAll:
                        DoSendAll(msg);
                        break;

                    default:

                        break;
                }
            }
        }

        private void DoStart(NetWorkTaskArgs msg)
        {
            try {
                LogProxy.LogDebug("DNServer.DoStart()：工作线程开始执行DoStart()...");
                if (_listenerSocket != null) {
                    LogProxy.Log(" DNServer.DoStart():_socketListener.Dispose();");
                    _listenerSocket.Dispose();
                }
                LogProxy.Log("DNServer.DoStart():_socketListener = new SocketListener(CONNECTIONS_BUFFER_SIZE);");
                _listenerSocket = new ServerListenerSocket();
                LogProxy.Log("DNServer.DoStart():_socketListener.EventAccept += OnAccept;");
                _listenerSocket.EventAccept += OnListenerSocketAccept;

                LogProxy.Log("DNServer.DoStart(): _socketListener.Start(" + _port + ");");
                _listenerSocket.Start(msg.text1, _port);
                LogProxy.LogDebug("DNServer.DoStart()执行完毕！");
            } catch (Exception e) {
                LogProxy.LogWarning("DNServer.DoStart()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 线程函数：发送
        /// </summary>
        /// <param name="args">这个消息参数的arg1为tokenID</param>
        private void DoSend(NetWorkTaskArgs args)
        {
            try {
                Peer peer = args.peer;
                if (peer == null) {
                    return;
                }

                if (args.data != null) //添加该条数据
                {
                    peer.peerSocket.AddSendData(args.data, 0, args.data.Length);
                }
                peer.peerSocket.TryBeginSend();
            } catch (Exception e) {
                LogProxy.LogWarning("DNServer.DoSend()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 线程函数，向所有Token发送他们的待发送数据
        /// </summary>
        /// <param name="args"></param>
        private void DoSendAll(NetWorkTaskArgs args)
        {
            try {
                Peer[] tokens = PeerManager.Inst.GetAllPeer();
                if (tokens == null) {
                    return;
                }
                for (int i = 0; i < tokens.Length; i++) {
                    Peer peer = tokens[i];
                    peer.peerSocket.TryBeginSend();
                }
            } catch (Exception e) {
                LogProxy.LogWarning("DNServer.DoSendAll()：异常 " + e.Message);
            }
        }

        private void DoReceive(NetWorkTaskArgs args)
        {
            try {
                Peer peer = args.peer;
                // 现在没有线程的解包,所以不需要工作
            } catch (Exception e) {
                LogProxy.LogWarning($"DNServer.DoReceive()：异常 {e}");
            }
        }

        #endregion Thread Function

        #region EventHandler

        private void OnListenerSocketAccept(Peer peer)
        {
            peer.peerSocket.EventError += (eventError) => {
                EventPeerError?.Invoke(peer, PeerErrorType.SocketError);
                LogProxy.Log($"客户端{peer.ID}发生错误,删除它");
                PeerManager.Inst.DeletePeer(peer.ID, PeerErrorType.SocketError); //关闭Token
            };
            peer.peerSocket.EventReceiveCompleted += () => { EventPeerReceData?.Invoke(peer); };

            PeerManager.Inst.AddPeer(peer); //把这个用户加入TokenManager,分配一个ID
        }

        private void OnReceive(Peer peer)
        {
            //DxDebug.Log("信号量： 接收");
            NetWorkTaskArgs msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.S_Receive, null, peer.ID);
            msg.peer = peer;
            AddTask(msg); //debug:这里可以不传ID，直接传Token引用

            //DoReceive(msg);//debug:直接使用这个小线程（结果：貌似性能没有明显提高，也貌似没有稳定性的问题）
        }

        private void OnSend(Peer peer)
        {
            if (peer.peerSocket.WaitSendMsgCount > 0) //如果待发送队列里有消息这里应该不需要这个判断了token.SendingCount < MAX_TOKEN_SENDING_COUNT &&
            {
                //DxDebug.Log("OnSend 信号量： 发送");
                NetWorkTaskArgs msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.S_Send, null, peer.ID);
                msg.peer = peer;
                AddTask(msg);

                //DoSend(msg);//debug:直接使用这个小线程（结果：貌似性能没有明显提高，也貌似没有稳定性的问题）
            }
            else {
            }
        }

        #endregion EventHandler

        #region IDisposable implementation

        /// <summary>
        /// 这个对象的Close()函数会调用该函数
        /// </summary>
        public void Dispose()
        {
            LogProxy.LogWarning("DNServer.Dispose():进入了Dispose.");
            if (_workThread != null) {
                for (int i = 0; i < _workThread.Length; i++) {
                    LogProxy.LogDebug("DNServer.Dispose():[" + _workThread[i].Name + "].IsAlive 为:" + _workThread[i].IsAlive);
                }
            }

            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposed) {
                return;
            }
            try {
                if (disposing) {
                    // 清理托管资源
                    _taskArgsQueue.Clear();
                    status.Clear(); //清空状态统计
                }
                // 清理非托管资源
                EventPeerError = null;
                EventPeerReceData = null;

                _msgSemaphore.Close(); //关信号量队列
                if (_workThread != null) {
                    for (int i = 0; i < _workThread.Length; i++) {
                        try {
                            if (_workThread[i].IsAlive) //关线程
                            {
                                _workThread[i].Abort();
                            }
                        } catch (Exception e) {
                            LogProxy.LogWarning("DNServer.Dispose():异常 _workThread[" + i + "].Abort();" + e.Message);
                        }
                    }
                }

                if (_listenerSocket != null) {
                    _listenerSocket.Dispose();
                    _listenerSocket = null;
                }
            } catch (Exception e) {
                LogProxy.LogWarning("DNServer.Dispose():异常 " + e.Message);
            }
            //让类型知道自己已经被释放
            disposed = true;
        }

        #endregion IDisposable implementation
    }
}
