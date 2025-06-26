using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using DNET.Protocol;

namespace DNET
{
    /// <summary>
    /// 通信传输的客户端类.
    /// 主要就是再加上一层工作线程的异步封装
    /// </summary>
    public class DNClient
    {
        private static readonly Lazy<DNClient> _instance = new Lazy<DNClient>(() => new DNClient());

        /// <summary>
        /// 单例
        /// </summary>
        public static DNClient Inst => _instance.Value;

        /// <summary>
        /// 公有的构造函数，可以用来在一个程序中开多个客户端。
        /// 它没有启动公共Timer
        /// </summary>
        /// <param name="clientName"></param>
        public DNClient(string clientName = "unname client")
        {
            this.Name = clientName;

            Peer = new Peer();

            IsConnecting = false;
            IsInited = false;

            //启动公共Timer
            ClientTimer.Inst.Start();
        }

        #region Fields

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
        /// 当前的信号量计数,防止出异常吧
        /// </summary>
        private int _curSemCount = 0;

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
        private PeerSocket _peerSocket = null;

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
                if (_peerSocket == null) {
                    return false;
                }
                else {
                    return _peerSocket.IsConnected;
                }
            }
        }

        /// <summary>
        /// 是否正在连接
        /// </summary>
        public bool IsConnecting { get; private set; }

        /// <summary>
        /// 发送队列是否太长
        /// </summary>
        public bool SendQueueOverflow {
            get {
                if (_peerSocket.WaitSendMsgCount >= 64)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// 方便逻辑统一使用的token，用来记录一些用户保存的对象，传给事件，只有里面的userObj是有意义的
        /// </summary>
        public Peer Peer { get; set; }

        /// <summary>
        /// 它的状态.
        /// </summary>
        public PeerStatus Status => _peerSocket.peerStatus;

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
                if (_peerSocket != null) {
                    _peerSocket.Disconnect();
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
            Send(data, 0, data.Length);
        }

        /// <summary>
        /// 发送一条数据，有起始和长度控制
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据的起始位置</param>
        /// <param name="count">数据的长度</param>
        /// <param name="format"></param>
        /// <param name="txrId"></param>
        /// <param name="eventType"></param>
        public void Send(byte[] data,
            int offset,
            int count,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0)
        {
            if (data == null) {
                LogProxy.LogWarning("DNClient.Send(data,offset,count):要发送的数据为null！");
            }
            try {
                // 这里其实已经开始打包了.
                _peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
                _peerSocket.TryBeginSend();//这个函数可以直接启动 
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
                Send(dataBytes, 0, dataBytes.Length, Format.Text);
            } catch (Exception e) {
                LogProxy.LogWarning($"DNClient.Send:异常 {e}");
            }
        }

        /// <summary>
        /// 获取目前所有的已接收的数据.
        /// </summary>
        /// <returns>所有的byte[]数据,没有则返回null</returns>
        public List<Message> GetReceiveData()
        {
            return _peerSocket.GetReceiveMessages();
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

                try {
                    //如果当前的信号量剩余不多的时候
                    if (_curSemCount < 4) {
                        Interlocked.Increment(ref _curSemCount);
                        _msgSemaphore.Release(); // 释放信号量
                    }
                    // 如果加入的过快而无法发送，则将产生信号量溢出异常,但是不会影响程序的唤醒
                } catch (Exception) {
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
                    Interlocked.Decrement(ref _curSemCount); //递减信号量计数

                    Interlocked.Exchange(ref _isThreadWorking, 1); //标记当前线程已经正在执行工作

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

                this.Clear(); //清空数据

                if (_peerSocket != null) {
                    // 断开原先连接，绑定新ip，清理状态
                    _peerSocket.Disconnect();
                    _peerSocket.Clear();
                }
                else {
                    _peerSocket = new PeerSocket();
                    //_peerSocket.EventReceiveCompleted += OnReceiveCompleted;
                    //_peerSocket.EventSendCompleted += OnSendCompleted;
                    _peerSocket.EventError += OnError;

                }

                LogProxy.LogDebug("DNClient.DoConnect():正在连接...");
                _peerSocket.BindRemote(_host, _port);
                _peerSocket.Connect();
                LogProxy.LogDebug($"DNClient.DoConnect():连接服务器成功！{_host}:{_port}");

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
                // 如果还有待发送的消息,直接从打包器中获取数据发送
                if (_peerSocket.TryBeginSend()) {


                }
            } catch (Exception e) {
                LogProxy.LogWarning("DNClient.DoSend():异常: " + e.Message);
            }
        }

        private void DoReceive()
        {
            try {
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

                if (_peerSocket != null) {
                    _peerSocket.Dispose();
                    _peerSocket = null;
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
        }

        #endregion BuiltIn Function

        #region EventHandler

        private void OnReceiveCompleted()
        {
            if (Config.isDebugLog)
                LogProxy.LogDebug("-----------EventHandler：进入了OnReceive回调！");


        }

        private void OnSendCompleted()
        {
            if (Config.isDebugLog)
                LogProxy.LogDebug("-----------EventHandler.OnSend()：进入OnSend回调！");


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
                _msgSemaphore.Close();
                _msgSemaphore = null;
                if (_peerSocket != null) {
                    _peerSocket.Dispose();
                    _peerSocket = null;
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
