using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 通信传输的客户端类，默认通信数据包打包方法类的类型为FastPacket2。
    /// </summary>
    public class DNClient
    {
        #region Constructor

        /// <summary>
        /// 私有的构造函数，用来构造单例
        /// </summary>
        private DNClient()
        {
            if (_instance != null) {
                this.Dispose();
                _instance = null;
            }
            this.Name = "unname client";
            _packet2 = new FastPacket2();

            Peer = new Peer();
            Peer.client = this;

            IsConnecting = false;
            IsInited = false;
            //Init();

            //启动公共Timer
            ClientTimer.GetInstance().Start();
        }

        /// <summary>
        /// 公有的构造函数，可以用来在一个程序中开多个客户端。
        /// 它没有启动公共Timer
        /// </summary>
        /// <param name="clientName"></param>
        public DNClient(string clientName = "unname client")
        {
            this.Name = clientName;
            _packet2 = new FastPacket2();

            Peer = new Peer();
            Peer.client = this;

            IsConnecting = false;
            IsInited = false;
            //Init();
        }

        private static DNClient _instance;


        /// <summary>
        /// 获得实例
        /// </summary>
        public static DNClient Inst {
            get {
                if (_instance == null) {
                    _instance = new DNClient();
                    _instance.Name = "singleton client";
                }
                return _instance;
            }

        }

        #endregion Constructor

        #region Fields

        ///// <summary>
        ///// 消息队列最大数
        ///// </summary>
        //private int MSG_QUEUE_CAPACITY = 4096;

        ///// <summary>
        ///// 最多缓存上限，接收到的已解包而未被上层处理的数据的队列
        ///// </summary>
        //private int MAX_RECR_DATA_QUEUE = 4096;

        /// <summary>
        /// 最多当前等待发送队列长度(用来提供当前待发送队列是否较长的判断的）
        /// </summary>
        private int MAX_SEND_DATA_QUEUE = 4096;

        /// <summary>
        /// 队列的最大字节占用，虽然理论上这个大小没有限制，但是实测如果过大则通信不正常（win上15M可工作，31M不能工作）。
        /// 现规定这个占用为4M，留成6M
        /// </summary>
        private int MAX_BYTES_SIZE = 6 * 1024 * 1024;

        /// <summary>
        /// 最多当前正在发送数
        /// </summary>
        private int MAX_SENDING_DATA = 1;

        /// <summary>
        /// 工作线程
        /// </summary>
        private Thread _workThread = null;

        /// <summary>
        /// 对应一条消息的信号量
        /// </summary>
        private Semaphore _msgSemaphore = null;

        /// <summary>
        /// 和U3D主模块之间的通信的消息队列
        /// </summary>
        private DQueue<NetWorkTaskArgs> _taskArgsQueue = null;

        /// <summary>
        /// 通信类使用的控制消息池
        /// </summary>
        private DQueue<NetWorkTaskArgs> _taskArgsPool = null;

        /// <summary>
        /// 当前的信号量计数
        /// </summary>
        //private int _curSemCount = 0;

        /// <summary>
        /// 服务器主机名
        /// </summary>
        private string _host; // = "127.0.0.1";//

        /// <summary>
        /// 服务器端口号
        /// </summary>
        private int _port; //9900

        /// <summary>
        /// 底层的通信类
        /// </summary>
        private SocketClient _socketClient = null;

        /// <summary>
        /// 打包器2代
        /// </summary>
        private IPacket2 _packet2;

        /// <summary>
        /// 当前正在发送的计数
        /// </summary>
        private volatile int _snedingCount = 0; //volatile

        /// <summary>
        /// CPU消耗时间计算，目前没有开启
        /// </summary>
        private DThreadTimeAnalyze _cpuTime = new DThreadTimeAnalyze();

        /// <summary>
        /// 标记待发送的队列是否过长
        /// </summary>
        private bool _isQueueFull = false;

        /// <summary>
        /// 队列的峰值长度
        /// </summary>
        private int _sendQueuePeakLength = 0;

        /// <summary>
        /// 是否输出一些调试型的日志.
        /// </summary>
        private bool _isDebugLog = false;

        /// <summary>
        /// 发出消息处理等待警告时的时间长度，会逐级递增和递减.
        /// </summary>
        private int _warringWaitTime = 500;

        /// <summary>
        /// 当前线程是否在工作，0表示false，1表示true.
        /// </summary>
        private int _isThreadWorking = 0;

        /// <summary>
        /// 这个对象是否已经被释放掉
        /// </summary>
        private bool _disposed = true;

        #endregion Fields

        #region Property

        /// <summary>
        /// 这个客户端的名字
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 这个客户端是否已经初始化可用,这个属性目前只对外标记，对内没有用来作判断。
        /// </summary>
        public bool IsInited { get; private set; }

        /// <summary>
        /// 是否已经连接上了服务器
        /// </summary>
        public bool IsConnected {
            get {
                if (_socketClient == null) {
                    return false;
                }
                else {
                    return _socketClient.IsConnected;
                }
            }
        }

        /// <summary>
        /// 是否正在连接
        /// </summary>
        public bool IsConnecting { get; private set; }

        /// <summary>
        /// 通信线程线程的cpu占用率（百分率）
        /// </summary>
        public double CpuOccupancyRate { get { return _cpuTime.OccupancyRate; } }

        /// <summary>
        /// 通信发送的IO占用率（百分率）
        /// </summary>
        public double SendOccupancyRate {
            get {
                if (_socketClient != null) {
                    return _socketClient.SendOccupancyRate;
                }
                else {
                    return 0;
                }
            }
        }

        /// <summary>
        /// 通信接收的IO占用率（百分率）
        /// </summary>
        public double ReceOccupancyRate {
            get {
                if (_socketClient != null) {
                    return _socketClient.ReceOccupancyRate;
                }
                else {
                    return 0;
                }
            }
        }

        /// <summary>
        /// 打包器2代
        /// </summary>
        public IPacket2 Packet2 { get { return _packet2; } set { _packet2 = value; } }

        /// <summary>
        /// 方便逻辑统一使用的token，用来记录一些用户保存的对象，传给事件，只有里面的userObj是有意义的
        /// </summary>
        public Peer Peer { get; set; }

        /// <summary>
        /// 用来记录最后一次收到这个Token发来的消息时间的Tick,创建这Token对象的时候初始化
        /// </summary>
        public long LastMsgReceTickTime { get; internal set; }

        /// <summary>
        /// 用来记录最后一次向这个Token发送的消息时间的Tick,创建这Token对象的时候初始化
        /// </summary>
        public long LastMsgSendTickTime { get; internal set; }

        /// <summary>
        /// 是否发送队列已经比较满,MAX_SEND_DATA_QUEUE/8=512,要注意这个指标不等于服务器已经收到消息.
        /// </summary>
        public bool isSendQueueIsFull {
            get {
                if (_packet2.SendMsgCount > MAX_SEND_DATA_QUEUE / 32) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }

        /// <summary>
        /// 是否打印调试型的日志.
        /// </summary>
        public bool isDebugLog { get { return _isDebugLog; } set { _isDebugLog = value; } }

        #endregion Property

        #region Event

        /// <summary>
        /// 事件：连接服务器成功
        /// </summary>
        public event Action<DNClient> EventConnectSuccess;

        /// <summary>
        /// 事件：接收到了新的消息，可以将任务加入这个事件交给数据解包线程。
        /// 有几条数据就会有几次事件，但是由于粘包问题这些事件可能会一连串的来。
        /// 用户在这个事件中应该自己调用GetReceiveData()。
        /// </summary>
        public event Action<DNClient> EventReceData;

        /// <summary>
        /// 事件：错误,可以用来通知服务器断线，关闭等。当进入这个事件的时候，此时与服务器的连接肯定已经断开了
        /// </summary>
        public event Action<DNClient, EventType, Exception> EventError;

        /// <summary>
        /// 事件：发送队列的大小已经过大
        /// </summary>
        public event Action<DNClient> EventSendQueueIsFull;

        /// <summary>
        /// 事件：发送队列的大小已经可以用了
        /// </summary>
        public event Action<DNClient> EventSendQueueIsAvailable;

        #endregion Event

        #region Exposed Function

        /// <summary>
        /// 连接服务器,输入IP和端口号。如果没有初始化在第一次会初始化。
        /// </summary>
        /// <param name="host">主机IP</param>
        /// <param name="port">端口号</param>
        /// <param name="isTry">是否是尝试连接</param>
        public void Connect(string host, int port, bool isTry = false)
        {
            try {
                LogProxy.LogDebug("DNClient.Connect():连接服务器 主机：" + host + "  端口:" + port);

                // 标记正在连接
                IsConnecting = true;

                // 这个初始化如果重复调用并不会重新new出成员.
                Init();

                // 进行一次连接的时候，把消息队列清空
                Clear();

                Interlocked.Exchange(ref this._host, host); //给类成员赋值
                Interlocked.Exchange(ref this._port, port); //给类成员赋值

                NetWorkTaskArgs taskArg = _taskArgsPool.Dequeue();
                if (taskArg == null) {
                    taskArg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.C_Connect);
                }
                else {
                    taskArg.Reset(NetWorkTaskArgs.Tpye.C_Connect);
                }
                AddTask(taskArg);

                LastMsgReceTickTime = DateTime.Now.Ticks;
                LastMsgSendTickTime = DateTime.Now.Ticks;
            } catch (Exception e) {
                // 一般来说其实不会进入这个异常.因为这个函数只是吧一个Message添加到队列中，不会发生异常. 
                IsConnecting = false; //连接失败了
                if (!isTry) {
                    LogProxy.LogError("DNClient.Connect():异常：" + e.Message);

                    if (EventError != null) {
                        try {
                            EventError(this, EventType.ConnectError, e); //事件类型：ConnectError
                        } catch (Exception e2) {
                            LogProxy.LogWarning("DNClient.Connect():执行EventError事件异常：" + e2.Message);
                        }
                    }
                }
                else {
                    LogProxy.LogDebug("DNClient.Connect():异常：" + e.Message);
                }

                Dispose(); //释放
            }
        }

        /// <summary>
        /// 关闭当前连接
        /// </summary>
        public void Disconnect()
        {
            try {
                this.Clear();
                if (_socketClient != null) {
                    _socketClient.Disconnect();
                }
            } catch (Exception e) {
                LogProxy.LogWarning("DNClient.DisConnect():执行DisConnect异常：" + e.Message);
            }
        }

        /// <summary>
        /// 异步的关闭socket和线程，会在消息队列中执行完这个消息之前的所有消息后，才会执行。
        /// </summary>
        public void Close()
        {
            LogProxy.Log("DNClient.Close():进入了close函数！");

            NetWorkTaskArgs args = _taskArgsPool.Dequeue();
            if (args == null) {
                args = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.C_AsynClose);
            }
            else {
                args.Reset(NetWorkTaskArgs.Tpye.C_AsynClose);
            }
            AddTask(args);
        }

        /// <summary>
        /// 立即的关闭线程和socket，会调用这个类的Dispose()
        /// </summary>
        public void CloseImmediate()
        {
            LogProxy.Log("DNClient.CloseImmediate():进入了CloseImmediate函数！");
            Dispose();
        }

        /// <summary>
        /// 发送一条数据
        /// </summary>
        /// <param name="data">要发送的整个数据</param>
        public void Send(byte[] data)
        {
            if (data == null) {
                LogProxy.LogWarning("DNClient.Send():要发送的数据为null！");
            }

            try {
                // 注意这里是直接添加这条消息到打包器,而没有把数据引用放到args中
                _packet2.AddSend(data, 0, data.Length);

                NetWorkTaskArgs args = _taskArgsPool.Dequeue();
                if (args == null) {
                    args = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.C_Send);
                }
                else {
                    args.Reset(NetWorkTaskArgs.Tpye.C_Send);
                }
                AddTask(args);
            } catch (Exception e) {
                LogProxy.LogWarning("DNClient.Send():异常 " + e.Message);
            }
        }

        /// <summary>
        /// 发送一条数据，有起始和长度控制
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据的起始位置</param>
        /// <param name="count">数据的长度</param>
        public void Send(byte[] data, int offset, int count)
        {
            if (data == null) {
                LogProxy.LogWarning("DNClient.Send(data,offset,count):要发送的数据为null！");
            }
            try {
                _packet2.AddSend(data, offset, count); //添加这条消息到打包器

                //进行数据的预打包，然后不拷贝
                NetWorkTaskArgs msg = _taskArgsPool.Dequeue();
                if (msg == null) {
                    msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.C_Send);
                }
                else {
                    msg.Reset(NetWorkTaskArgs.Tpye.C_Send);
                }
                AddTask(msg);
            } catch (Exception e) {
                LogProxy.LogWarning("DNClient.Send(p1,p2,p3):异常 " + e.Message);
            }
        }

        /// <summary>
        /// 发送字符串数据
        /// </summary>
        /// <param name="text">字符串数据</param>
        public void Send(string text)
        {
            try {
                byte[] dataBytes = null;
                if (string.IsNullOrEmpty(text)) {
                    Send(dataBytes);
                    return;
                }
                dataBytes = Encoding.UTF8.GetBytes(text);
                Send(dataBytes);
            } catch (Exception e) {
                LogProxy.LogWarning($"DNClient.Send:异常 {e}");
            }
        }

        /// <summary>
        /// 得到一条接收到的数据
        /// </summary>
        /// <returns></returns>
        public ByteBuffer GetOneReceiveData()
        {
            return _packet2.GetReceMsg();
        }

        /// <summary>
        /// 提供一个Buffer数组批量得到接收到的数据，返回成功得到的数量
        /// </summary>
        /// <param name="buffs">Buffer数组</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="count">最大计数</param>
        /// <returns></returns>
        public int GetReceiveData(ByteBuffer[] buffs, int offset, int count)
        {
            return _packet2.GetReceMsg(buffs, offset, count);
        }

        /// <summary>
        /// 获取目前所有的已接收的数据，返回byte[][]的形式,没有则返回null.
        /// 这是会产生GC的方式，不推荐.
        /// </summary>
        /// <returns>所有的byte[]数据,没有则返回null</returns>
        public byte[][] GetReceiveData()
        {
            if (_packet2.ReceMsgCount == 0) {
                return null;
            }
            List<ByteBuffer> ListMsg = new List<ByteBuffer>();
            while (_packet2.ReceMsgCount != 0) {
                ByteBuffer bf = _packet2.GetReceMsg(); //提取一条消息
                if (bf != null) {
                    ListMsg.Add(bf);
                }
                else {
                    break;
                }
            }
            byte[][] datas = new byte[ListMsg.Count][];
            for (int i = 0; i < datas.Length; i++) {
                datas[i] = new byte[ListMsg[i].validLength];
                Buffer.BlockCopy(ListMsg[i].buffer, 0, datas[i], 0, ListMsg[i].validLength);
                ListMsg[i].Recycle();
            }

            return datas;
        }

        /// <summary>
        /// 获取目前所有的已接收的数据，返回string[]的形式,没有则返回null.
        /// 这是会产生GC的方式，不推荐.
        /// </summary>
        /// <returns>所有的string数据,没有则返回null</returns>
        public string[] GetReceiveText()
        {
            if (_packet2.ReceMsgCount == 0) {
                return null;
            }

            List<ByteBuffer> listMsg = new List<ByteBuffer>();
            while (_packet2.ReceMsgCount != 0) {
                ByteBuffer bf = _packet2.GetReceMsg(); // 提取一条消息
                if (bf != null) {
                    listMsg.Add(bf);
                }
                else {
                    break;
                }
            }
            string[] texts = new string[listMsg.Count];
            for (int i = 0; i < texts.Length; i++) {
                texts[i] = Encoding.UTF8.GetString(listMsg[i].buffer, 0, listMsg[i].validLength);
                listMsg[i].Recycle();
            }
            return texts;
        }

        /// <summary>
        /// 加入一条要执行的消息，如果加入的过快而无法发送，则将产生信号量溢出异常，表明当前发送数据频率要大于系统能力
        /// </summary>
        /// <param name="taskArgs"></param>
        internal void AddTask(NetWorkTaskArgs taskArgs)
        {
            if (_disposed) {
                LogProxy.LogWarning($"DNClient.AddTask():DNClient对象已经被释放，不能再加入消息。msgType = {taskArgs.type}");
                return;
            }
            try {
                LogProxy.LogDebug("DNClient.AddTask():向消息队列中添加消息");
                if (taskArgs != null)
                    _taskArgsQueue.Enqueue(taskArgs); //消息进队列

                //if (_curSemCount < 1) //如果当前的信号量剩余不多的时候
                //{
                //    Interlocked.Increment(ref _curSemCount);
                //    _msgSemaphore.Release(); // 释放信号量
                //}
                try {
                    // 如果加入的过快而无法发送，则将产生信号量溢出异常,但是不会影响程序的唤醒
                    _msgSemaphore.Release(); // 无脑释放信号量,catch一下好了
                } catch (Exception) {

                }
                //发送数据队列长度为128则认为消息已经积攒较长
                if (_packet2.SendMsgCount > 128 && _isQueueFull == false) //MAX_SEND_DATA_QUEUE
                {
                    _isQueueFull = true;

                    if (EventSendQueueIsFull != null) {
                        try {
                            EventSendQueueIsFull(this);
                        } catch (Exception e) {
                            LogProxy.LogWarning($"DNClient.AddTask():执行事件EventMsgQueueIsFull异常：{e}");
                        }
                    }
                    if (_isDebugLog)
                        LogProxy.LogWarning("DNClient.AddTask():向消息队列中添加消息，发送队列长度较长：" + _packet2.SendMsgCount);
                }
                else if (_isQueueFull) //如果现在的状态是发送队列较长的状态，那么再去记录峰值长度
                {
                    if (_sendQueuePeakLength < _packet2.SendMsgCount) {
                        _sendQueuePeakLength = _packet2.SendMsgCount; //记录当前的峰值长度
                    }
                }
            } catch (Exception e) {
                LogProxy.LogError($"DNClient.AddTask():异常：{e}");
            }
        }

        #endregion Exposed Function

        #region Thread Function

        private void DoWork()
        {
            try {
                LogProxy.LogDebug("DNClient.DoWork():通信线程启动！");
                while (true) {
                    Interlocked.Exchange(ref _isThreadWorking, 0); //标记当前线程已经停止工作

                    _msgSemaphore.WaitOne();
                    //Interlocked.Decrement(ref _curSemCount); //递减信号量计数

                    Interlocked.Exchange(ref _isThreadWorking, 1); //标记当前线程已经正在执行工作

                    //_cpuTime.WorkStart(); //时间分析计时

                    while (true) {
                        NetWorkTaskArgs taskArg = _taskArgsQueue.Dequeue();
                        if (taskArg == null) {
                            break;
                        }
                        float waitTime = (DateTime.Now.Ticks - taskArg.timeTickCreat) / 10000; //毫秒
                        if (waitTime > _warringWaitTime) {
                            _warringWaitTime += 500;
                            LogProxy.LogWarning("DNClient.DoWork():NetWorkMsg等待处理时间过长！waitTime:" + waitTime);
                        }
                        else if ((_warringWaitTime - waitTime) > 500) {
                            _warringWaitTime -= 500;
                        }

                        if (taskArg != null) {
                            switch (taskArg.type) {
                                case NetWorkTaskArgs.Tpye.C_Connect:
                                    DoConnect();
                                    break;

                                case NetWorkTaskArgs.Tpye.C_Send:
                                    DoSend(taskArg);
                                    break;

                                case NetWorkTaskArgs.Tpye.C_Receive:
                                    DoReceive();
                                    break;

                                case NetWorkTaskArgs.Tpye.C_AsynClose:
                                    DoClose();
                                    _workThread = null; //把这个成员置为空
                                    return; //执行完就结束了整个线程函数

                                default:

                                    break;
                            }
                            //用过的消息放回池里
                            _taskArgsPool.EnqueueMaxLimit(taskArg);
                        }
                        else {
                            // _cpuTime.Calculate();//空闲的话就计算一下
                        }
                        //long costTime = _cpuTime.WaitStart(); //时间分析计时
                    }
                }
            } catch (Exception e) {
                LogProxy.LogError("DNClient.DoWork():异常：通信线程执行异常！ " + e.Message);
            }
        }

        private void DoConnect()
        {
            try {
                //DxDebug.LogConsole("DNClient.DoConnect():执行Connect...");
                //标记正在连接
                IsConnecting = true;

                Interlocked.Exchange(ref _snedingCount, 0);
                this.Clear(); //清空数据

                if (_socketClient != null) {
                    //DxDebug.LogConsole("DNClient.DoConnect():断开原先连接！");
                    _socketClient.Disconnect();
                    _socketClient.Bind(_host, _port); //绑定新ip
                    _socketClient.Clear();
                    LogProxy.LogDebug("DNClient.DoConnect():正在连接...");
                    _socketClient.Connect();
                    LogProxy.LogDebug("DNClient.DoConnect():连接服务器成功！" + _host + ":" + _port);
                }
                else {
                    _socketClient = new SocketClient(_host, _port, _packet2);
                    if (_socketClient == null) {
                        LogProxy.LogError("DNClient.DoConnect():连接服务器失败！_socketClient对象未能创建成功。");
                        return;
                    }
                    _socketClient.EventReceive += OnReceive; //加入接收事件
                    _socketClient.EventSend += OnSend; //加入发送事件
                    _socketClient.EventError += OnError; //加入错误事件
                    LogProxy.LogDebug("DNClient.DoConnect():正在连接...");
                    _socketClient.Connect();
                    LogProxy.LogDebug("DNClient.DoConnect():连接服务器成功！" + _host + ":" + _port);
                }

                if (EventConnectSuccess != null) {
                    try {
                        EventConnectSuccess(this);
                    } //事件类型：ConnectError
                    catch (Exception e) {
                        LogProxy.LogError($"DNClient.DoConnect():执行 EventConnectSuccess 事件异常：{e}");
                    }
                }
            } catch (Exception e) {
                LogProxy.LogWarning($"DNClient.DoConnect():连接服务器失败！{e.Message}");

                if (EventError != null) {
                    try {
                        EventError(this, EventType.ConnectError, e);
                    } //事件类型：ConnectError
                    catch (Exception e2) {
                        LogProxy.LogError("DNClient.DoConnect():执行 EventError 事件异常：" + e2.Message);
                    }
                }
            }
            //标记已经结束了连接
            IsConnecting = false;
        }

        private void DoSend(NetWorkTaskArgs args)
        {
            try {
                if (IsConnected == false) {
                    LogProxy.LogWarning("DNClient.DoSend：当前还未连接到一个主机！ ");
                    return;
                }

                // DxDebug.Log("-----------DoSend   " + "  当前的SendingCount  " + _snedingCount);
                if (_snedingCount >= MAX_SENDING_DATA) {
                    return;
                }
                // 如果还有待发送的消息,直接从打包器中获取数据发送
                if (_packet2.SendMsgCount > 0) {
                    if (_socketClient.SendData(_packet2)) {
                        //如果确实发送成功了
                        Interlocked.Increment(ref _snedingCount);
                    }
                    LastMsgSendTickTime = DateTime.Now.Ticks; //记录最后发送消息时间
                }

                //发送队列已经较短了的事件
                if (_packet2.SendMsgCount < 128 && _isQueueFull == true) // MAX_SEND_DATA_QUEUE / 4
                {
                    _isQueueFull = false;

                    if (_isDebugLog)
                        LogProxy.LogWarning("DNClient.DoSend():发送队列长度已经恢复正常，峰值长度" + _sendQueuePeakLength);
                    _sendQueuePeakLength = 0; //重计峰值长度

                    _taskArgsQueue.TrimExcess();
                    _taskArgsPool.TrimExcess();

                    if (EventSendQueueIsAvailable != null) {
                        try {
                            EventSendQueueIsAvailable(this);
                        } catch (Exception e2) {
                            LogProxy.LogWarning("DNClient.DoSend():执行事件EventMsgQueueIsAvailable异常：" + e2.Message);
                        }
                    }
                }
            } catch (Exception e) {
                LogProxy.LogWarning("DNClient.DoSend():异常: " + e.Message);
            }
        }

        private void DoReceive()
        {
            try {
                //不再做心跳包处理，直接发出事件
                LastMsgReceTickTime = DateTime.Now.Ticks;
                //接收数据事件
                if (EventReceData != null) {
                    try {
                        EventReceData(this); //发出事件：接收到了数据
                    } catch (Exception e) {
                        LogProxy.LogWarning($"DNClient.DoReceive()：执行外部事件 EventReceData 异常: {e}");
                    }
                }

                //DxDebug.Log("-----------数据解包完成，数据条数：  " + findPacketResult.data.Length);
            } catch (Exception e) {
                LogProxy.LogWarning($"DNClient.DoReceive():异常: {e}");
            }
        }

        private void DoClose()
        {
            try {
                IsInited = false;
                LogProxy.LogDebug("DNClient.DoClose():开始释放资源 ");
                _disposed = true;

                // 清理托管资源
                _taskArgsQueue.Clear();
                _taskArgsPool.Clear();

                _taskArgsQueue = null;
                _taskArgsPool = null;

                // 清理非托管资源
                _msgSemaphore.Close();
                _msgSemaphore = null;
                //Interlocked.Exchange(ref _curSemCount, 0);

                if (_socketClient != null) {
                    _socketClient.Dispose();
                    _socketClient = null;
                }

                IsConnecting = false;
            } catch (Exception e) {
                LogProxy.LogWarning("DNClient.DoClose():异常: " + e.Message);
            }
        }

        #endregion Thread Function

        #region BuiltIn Function

        /// <summary>
        /// 初始化这个对象,会创建工作线程
        /// </summary>
        private void Init()
        {
            try {
                //if (!_disposed)//强制释放一遍
                //{
                //    DxDebug.LogConsole("DNClient.Init():释放资源");
                //    Dispose();
                //}
                if (_taskArgsQueue == null)
                    _taskArgsQueue = new DQueue<NetWorkTaskArgs>(int.MaxValue, 256);
                if (_taskArgsPool == null)
                    _taskArgsPool = new DQueue<NetWorkTaskArgs>(int.MaxValue, 256);

                if (_msgSemaphore == null) {
                    _msgSemaphore = new Semaphore(0, 4);
                    //Interlocked.Exchange(ref _curSemCount, 0);
                }

                if (_workThread == null) {
                    _workThread = new Thread(DoWork);
                    _workThread.IsBackground = true;

                    _workThread.Start(); //启动线程
                }

                _disposed = false;
                IsInited = true;
            } catch (Exception e) {
                Dispose();
                LogProxy.LogError("DNClient.Init():异常：" + e.Message);
            }
        }

        /// <summary>
        /// 清空当前所有队列和数据存储
        /// </summary>
        private void Clear()
        {
            _taskArgsQueue.Clear();
            _packet2.Clear();
        }

        #endregion BuiltIn Function

        #region EventHandler

        private void OnReceive()
        {
            if (this._isDebugLog)
                LogProxy.LogDebug("-----------EventHandler：进入了OnReceive回调！");

            NetWorkTaskArgs msg = _taskArgsPool.Dequeue();
            if (msg == null) {
                msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.C_Receive);
            }
            else {
                msg.Reset(NetWorkTaskArgs.Tpye.C_Receive);
            }
            AddTask(msg);
        }

        private void OnSend()
        {
            if (_isDebugLog)
                LogProxy.LogDebug("-----------EventHandler.OnSend()：进入OnSend回调！");

            Interlocked.Decrement(ref _snedingCount);
            if (_packet2.SendMsgCount > 0) //如果待发送队列里有消息,不需要再判断_snedingCount < MAX_SENDING_DATA，直接开始下一次发送
            {
                NetWorkTaskArgs msg = _taskArgsPool.Dequeue();
                if (msg == null) {
                    msg = new NetWorkTaskArgs(NetWorkTaskArgs.Tpye.C_Send);
                }
                else {
                    msg.Reset(NetWorkTaskArgs.Tpye.C_Send);
                }
                AddTask(msg);
            }
            else {
            }
        }

        private void OnError()
        {
            if (EventError != null) {
                try {
                    EventError(this, EventType.IOError, null);
                } catch (Exception e) {
                    LogProxy.LogWarning("DNClient.OnError()：执行 EventError 事件异常:" + e.Message);
                }
            }
        }

        #endregion EventHandler

        #region IDisposable implementation

        /// <summary>
        /// Dispose，这个对象的Close()函数会调用该函数
        /// </summary>
        public void Dispose()
        {
            if (_workThread != null) {
                LogProxy.Log("DNClient.Dispose():_threadTest.IsAlive 为:" + _workThread.IsAlive);
            }
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) {
                return;
            }
            IsInited = false;

            try {
                //最先去把线程关了
                if (_workThread != null && _workThread.IsAlive) {
                    LogProxy.LogDebug("DNClient.Dispose():_workThread.Abort()线程中断！");
                    _workThread.Abort();
                }
            } catch (Exception e) {
                LogProxy.LogWarning("DNClient.Dispose(): _workThread.Abort()异常" + e.Message);
            } finally {
                _workThread = null;
            }

            try {
                if (disposing) {
                    // 清理托管资源
                    _taskArgsQueue.Clear();
                    _taskArgsPool.Clear();

                    _taskArgsQueue = null;
                    _taskArgsPool = null;
                }
                // 清理非托管资源
                _packet2.Clear();
                _msgSemaphore.Close();
                _msgSemaphore = null;
                if (_socketClient != null) {
                    _socketClient.Dispose();
                    _socketClient = null;
                }
            } catch (Exception e) {
                LogProxy.LogWarning("DNClient.Dispose():释放异常" + e.Message);
            }
            //让类型知道自己已经被释放
            _disposed = true;

            IsConnecting = false;
        }

        #endregion IDisposable implementation
    }
}
