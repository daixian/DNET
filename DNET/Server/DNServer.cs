//#define Multitask //是否多核心,依赖.net4.0。速度没有提升，CPU占用加大。

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DNET.Protocol;

#if Multitask
using System.Threading.Tasks;
#endif

namespace DNET
{
    /// <summary>
    /// 通信传输的服务器类，默认通信数据包打包方法类的类型为FastPacket。
    /// </summary>
    public class DNServer
    {
        #region Constructor

        /// <summary>
        /// 构造函数
        /// </summary>
        private DNServer()
        {
            //if (_instance != null)
            //{
            //    this.Dispose();
            //    _instance = null;
            //}

            //_packet = new DPacketNoCrc();
            _packet = new SimplePacket();

            ServerTimer.GetInstance().Start();

            Status = new ServerStatus(this); //创建状态统计
            Status.BindTimer(ServerTimer.GetInstance()); //绑定一个计时器
            Status.isPrintCur1s = false; //默认不打印状态统计（1s一打印）
        }

        private static DNServer _instance = null;

        /// <summary>
        /// 获得实例
        /// </summary>
        public static DNServer Inst {
            get {
                if (_instance == null) {
                    _instance = new DNServer();
                }
                return _instance;

            }
        }

        #endregion Constructor

        #region Event

        /// <summary>
        /// 事件：某个Peer发生了错误后，会自动调用关闭，这是错误及关闭事件
        /// </summary>
        public event Action<Peer, SocketError> EventPeerError;

        /// <summary>
        /// 事件：某个Peer接收到了数据，可以将轻量任务加入这个事件，交给数据解包线程
        /// </summary>
        public event Action<Peer> EventPeerReceData;

        #endregion Event

        #region Fields

        /// <summary>
        /// 最大客户端连接数（未使用）
        /// </summary>
        private int MAX_CONNECTIONS = 4;

        /// <summary>
        /// 客户端数据buffer大小，当这个buffer比较小的时候也是可以接收到一个比较长的消息的。
        /// 128K则有1000个客户端的时候至少内存占用128M。1W人是1.28G
        /// </summary>
        private int CONNECTIONS_BUFFER_SIZE = 128 * 1024; //改为8K，则1W人的时候占用80M

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
        //private int _curSemCount = 0;

        /// <summary>
        /// 服务器端口号
        /// </summary>
        private int _port; //9900

        /// <summary>
        /// 底层的通信类
        /// </summary>
        private SocketListener _socketListener = null;

        /// <summary>
        /// 打包解包器
        /// </summary>
        private IPacket3 _packet;

        /// <summary>
        /// CPU消耗时间计算，目前没有开启
        /// </summary>
        private DThreadTimeAnalyze _cpuTime = new DThreadTimeAnalyze();

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
        public ServerStatus Status;

        #endregion Fields

        #region Property

        /// <summary>
        /// 服务器启动成功标志
        /// </summary>
        public bool IsStarted {
            get {
                if (_socketListener != null) {
                    return _socketListener.IsStarted;
                }
                else {
                    return false;
                }
            }
        }

        /// <summary>
        /// 打包方法
        /// </summary>
        public IPacket3 Packet { get { return _packet; } set { _packet = value; } }

        /// <summary>
        /// 通信库所使用的临时文件工作目录,是绝对路径。这个目录是由SetDirCache()函数设置的
        /// </summary>
        public string dirCache { get; private set; }

        /// <summary>
        /// 是否工作文件夹能够使用
        /// </summary>
        public bool isDirCanUse { get { return !String.IsNullOrEmpty(dirCache); } }

        /// <summary>
        /// 当前的消息队列长度
        /// </summary>
        public int msgQueueLength { get { return _taskArgsQueue.Count; } }

        #endregion Property

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
                    PeerManager.Inst.Clear();
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
        /// <param name="tokenID">tokenID</param>
        /// <param name="data">要发送的数据</param>
        public void Send(int tokenID, byte[] data)
        {
            //DxDebug.Log("信号量： 发送");
            NetWorkTaskArgs msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.S_Send, data, tokenID);
            msg.peer = PeerManager.Inst.GetPeer(tokenID);
            AddTask(msg);
        }

        /// <summary>
        /// 向某个token发送一条数据。直接使用Token对象来发送
        /// </summary>
        /// <param name="peer"></param>
        /// <param name="data"></param>
        public void Send_(Peer peer, byte[] data)
        {
            NetWorkTaskArgs msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.S_Send, data, peer.ID);
            msg.peer = peer;
            AddTask(msg);
        }


        /// <summary> 向某个token发送一条数据. </summary>
        ///
        /// <remarks> Dx, 2017/6/26. </remarks>
        ///
        /// <param name="peer">  </param>
        /// <param name="data">  要发送的数据. </param>
        public void Send(Peer peer, byte[] data)
        {
            NetWorkTaskArgs msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.S_Send, null, peer.ID);
            peer.AddSendData(data, 0, data.Length);
            msg.peer = peer;
            AddTask(msg);
        }

        /// <summary> 向某个token发送一条数据. </summary>
        ///
        /// <remarks> Dx, 2017/6/26. </remarks>
        ///
        /// <param name="peer">  </param>
        /// <param name="text">  要发送的数据. </param>
        public void Send(Peer peer, string text)
        {
            try {
                byte[] dataBytes = null;
                if (string.IsNullOrEmpty(text)) {
                    Send(peer, dataBytes);
                    return;
                }
                dataBytes = Encoding.UTF8.GetBytes(text);
                Send(peer, dataBytes);
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
                //if (_curSemCount < 1) //信号量剩余较少的时候才去释放信号量
                //{
                //    Interlocked.Increment(ref _curSemCount);
                //    _msgSemaphore.Release();
                //}

                try {
                    // 如果加入的过快而无法发送，则将产生信号量溢出异常,但是不会影响程序的唤醒
                    _msgSemaphore.Release(); // 无脑释放信号量,catch一下好了
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
                //Interlocked.Decrement(ref _curSemCount);
                while (true) {
                    _cpuTime.WorkStart(); //时间分析计时
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
                    _cpuTime.WaitStart(); //时间分析计时
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
                if (_socketListener != null) {
                    LogProxy.Log(" DNServer.DoStart():_socketListener.Dispose();");
                    _socketListener.Dispose();
                }
                LogProxy.Log("DNServer.DoStart():_socketListener = new SocketListener(CONNECTIONS_BUFFER_SIZE);");
                _socketListener = new SocketListener(this, CONNECTIONS_BUFFER_SIZE);
                LogProxy.Log("DNServer.DoStart():_socketListener.EventAccept += OnAccept;");
                _socketListener.EventAccept += OnAccept;
                _socketListener.EventReceive += OnReceive;
                _socketListener.EventSend += OnSend;
                _socketListener.EventError += OnTokenError;
                LogProxy.Log("DNServer.DoStart(): _socketListener.Start(" + _port + ");");
                _socketListener.Start(msg.text1, _port);
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
                    peer.AddSendData(args.data, 0, args.data.Length);
                }
                //DxDebug.Log("DoSend : ID号 " + token.ID + "  当前的SendingCount  " + token.SendingCount);
                if (peer.SendingCount < MAX_TOKEN_SENDING_COUNT) {
                    if (peer.WaitSendMsgCount > 0) {
                        int sendByteLen = _socketListener.Send(peer);
                        Interlocked.Add(ref Status.CountSend, 1); //状态统计发送递增
                        Interlocked.Add(ref Status.CountSendBytes, sendByteLen); //状态统计递增发送数据长度 
                    }
                }
                else //如果当前正在发送，那么这次发送完成之后，会自动的开始下一次发送：OnSend()函数
                {
                }
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
                    //DxDebug.Log("DoSend : ID号 " + token.ID + "  当前的SendingCount  " + token.SendingCount);
                    if (peer.SendingCount < MAX_TOKEN_SENDING_COUNT) {
                        if (peer.WaitSendMsgCount > 0) {
                            int sendByteLen = _socketListener.Send(peer);
                            Interlocked.Add(ref Status.CountSend, 1); //状态统计发送递增
                            Interlocked.Add(ref Status.CountSendBytes, sendByteLen); //状态统计递增发送数据长度 
                        }
                    }
                    else //如果当前正在发送，那么这次发送完成之后，会自动的开始下一次发送：OnSend()函数
                    {
                    }
                }
            } catch (Exception e) {
                LogProxy.LogWarning("DNServer.DoSendAll()：异常 " + e.Message);
            }
        }

        private void DoReceive(NetWorkTaskArgs args)
        {

            try {
                Peer peer = args.peer;
                if (peer != null) {

                    int bytesLen = 0;
                    int msgCount = peer.UnpackReceiveData(_packet, out bytesLen);
                    if (msgCount > 0) //解包有数据
                    {
                        Interlocked.Add(ref Status.CountReceive, msgCount); //状态统计递增一条记录

                        if (EventPeerReceData != null) //发出事件：有收到客户端消息
                        {
                            try {
                                EventPeerReceData(peer);
                            } catch (Exception e) {
                                LogProxy.LogWarning("DNServer.DoReceive()：执行外部事件EventTokenReceData 异常 " + e.Message);
                            }
                        }
                    }
                    Interlocked.Add(ref Status.CountReceiveBytes, bytesLen); //状态统计递增接收数据长度
                }
            } catch (Exception e) {
                LogProxy.LogWarning($"DNServer.DoReceive()：异常 {e}");
            }
        }

        #endregion Thread Function

        #region EventHandler

        private void OnAccept(Peer peer)
        {
            //注意这个并没有使用
            //
            //DxDebug.Log("信号量： 认证");
            //AddMessage(new Msg(Msg.Tpye.S_Accept, null));
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
            if (peer.WaitSendMsgCount > 0) //如果待发送队列里有消息这里应该不需要这个判断了token.SendingCount < MAX_TOKEN_SENDING_COUNT &&
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

        private void OnTokenError(Peer peer, SocketError error)
        {
            if (EventPeerError != null) {
                EventPeerError(peer, error);
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
                    Status.Clear(); //清空状态统计
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

                if (_socketListener != null) {
                    _socketListener.Dispose();
                    _socketListener = null;
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
