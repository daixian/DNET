using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using DNET.Protocol;

namespace DNET
{
    class ConnectionContext
    {
        public Socket socket;

        public ByteBuffer sendBuffer;

        public byte[] recvBuffer;
    }


    /// <summary>
    /// 客户机实现套接字的连接逻辑。
    /// </summary>
    internal sealed class SocketClient : IDisposable
    {
        #region Constructor

        /// <summary>
        /// 构造函数： 创建出一个Socket对象
        /// </summary>
        internal SocketClient(string hostName, int port, IPacket3 packet2)
        {
            try {
                IPHostEntry host;
                if (Regex.IsMatch(hostName, @"\d{1,3}[.]\d{1,3}[.]\d{1,3}[.]\d{1,3}")) {
                    byte[] ipadr = new byte[4];

                    MatchCollection ms = Regex.Matches(hostName, @"\d{1,3}");
                    for (int i = 0; i < ms.Count; i++) {
                        ipadr[i] = Convert.ToByte(hostName.Substring(ms[i].Index, ms[i].Length));
                    }

                    IPAddress address = new IPAddress(ipadr);
                    this._hostEndPoint = new IPEndPoint(address, port);
                }
                else {
                    host = Dns.GetHostEntry(hostName);
                    IPAddress[] addressList = host.AddressList;

                    //实例化 endpoint 和 socket.
                    this._hostEndPoint = new IPEndPoint(addressList[addressList.Length - 1], port);
                }

                this._clientSocket = new Socket(this._hostEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                //设置这个Timeout应该是无效的(是有效的，必须设置为0，否则自动断线)
                _clientSocket.SendTimeout = 8 * 1000;
                _clientSocket.ReceiveTimeout = 0;
            } catch (Exception e) {
                LogProxy.LogWarning("SocketClient.SocketClient():类构造函数错误: " + e.Message);
                return;
            }

            if (_areConnectDone == null)
                _areConnectDone = new AutoResetEvent(false);
            if (_receiveBuffer == null)
                _receiveBuffer = new byte[RECE_BUFFER_SIZE];


            _packet2 = packet2;

#if !NEW_EVENT_AEGS

            _sendArgs = new SocketAsyncEventArgs();
            _sendArgs.UserToken = new ConnectionContext {
                socket = this._clientSocket,
                sendBuffer = null,
                recvBuffer = null,
            };
            //_sendArgs.RemoteEndPoint = null;
            _sendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendCompleted);

            _receiveArgs = new SocketAsyncEventArgs();
            _receiveArgs.UserToken = new ConnectionContext {
                socket = this._clientSocket,
                sendBuffer = null,
                recvBuffer = _receiveBuffer
            }; ; //利用了Token
            _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
            _receiveArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnReceiveCompleted);
#endif
            LogProxy.Log("SocketClient.SocketClient().SocketClient类构造对象成功！");
        }

        #endregion Constructor

        #region Fields

        /// <summary>
        /// 接收buffer大小
        /// </summary>
        private const int RECE_BUFFER_SIZE = 16 * 1024; //16k

        /// <summary>
        /// 套接字用于发送/接收消息。
        /// </summary>
        internal Socket _clientSocket;

        /// <summary>
        /// 主机网络端点
        /// </summary>
        private EndPoint _hostEndPoint;

        /// <summary>
        /// 信号量，通知等待的线程已经发生了事件.这个是用来确保连接先成功后再开始发送数据
        /// </summary>
        private AutoResetEvent _areConnectDone = null;

#if !NEW_EVENT_AEGS

        private SocketAsyncEventArgs _sendArgs = null;
        private SocketAsyncEventArgs _receiveArgs = null;

        /// <summary>
        /// 接收的buffer缓冲区
        /// </summary>
        private byte[] _receiveBuffer;

#endif

        /// <summary>
        /// 带数据管理的新打包器
        /// </summary>
        internal IPacket3 _packet2;

        /// <summary>
        /// 待发送数据队列
        /// </summary>
        ConcurrentQueue<ByteBuffer> _sendQueue = new ConcurrentQueue<ByteBuffer>();

        /// <summary>
        /// 待提取的接收数据队列
        /// </summary>
        ConcurrentQueue<Message> _receQueue = new ConcurrentQueue<Message>();

        /// <summary>
        /// 是否正在发送数据
        /// </summary>
        private int _isSending = 0;


        private bool disposed = false;

        #endregion Fields

        #region Property

        /// <summary>
        /// 是否已经连接上了服务器
        /// </summary>
        internal bool IsConnected {
            get {
                if (_clientSocket != null) {
                    return _clientSocket.Connected;
                }
                else {
                    return false;
                }
            }
        }

        /// <summary>
        /// 得到Socket
        /// </summary>
        public Socket socket { get { return _clientSocket; } }

        /// <summary>
        /// 等待发送消息队列长度
        /// </summary>
        public int WaitSendMsgCount => _sendQueue.Count;

        #endregion Property

        #region Event

        /// <summary>
        /// 出现错误
        /// </summary>
        internal event Action EventError;

        /// <summary>
        /// 连接成功的事件
        /// </summary>
        internal event Action EventConnectCompleted;

        /// <summary>
        /// 数据发送完毕
        /// </summary>
        internal event Action EventSendCompleted;

        /// <summary>
        /// 数据接收完毕
        /// </summary>
        internal event Action EventReceiveCompleted;

        #endregion Event

        #region Exposed Function

        /// <summary>
        /// 添加一个发送数据已经对数据打包了
        /// </summary>
        /// <param name="byteBuffer"></param>
        internal void AddSendData(ByteBuffer byteBuffer)
        {
            _sendQueue.Enqueue(byteBuffer);
        }

        /// <summary>
        /// 从接收队列中提取所有数据包
        /// </summary>
        /// <returns></returns>
        internal List<Message> GetReceiveMessages()
        {
            List<Message> messages = new List<Message>();
            while (_receQueue.TryDequeue(out var msg)) {
                messages.Add(msg);
            }
            return messages;
        }

        /// <summary>
        /// 重新绑定一个IP地址
        /// </summary>
        /// <param name="hostName"></param>
        /// <param name="port"></param>
        internal void Bind(string hostName, int port)
        {
            try {
                IPHostEntry host;
                if (Regex.IsMatch(hostName, @"\d{1,3}[.]\d{1,3}[.]\d{1,3}[.]\d{1,3}")) {
                    byte[] ipadr = new byte[4];

                    MatchCollection ms = Regex.Matches(hostName, @"\d{1,3}");
                    for (int i = 0; i < ms.Count; i++) {
                        ipadr[i] = Convert.ToByte(hostName.Substring(ms[i].Index, ms[i].Length));
                    }

                    IPAddress address = new IPAddress(ipadr);
                    this._hostEndPoint = new IPEndPoint(address, port);
                }
                else {
                    host = Dns.GetHostEntry(hostName);
                    IPAddress[] addressList = host.AddressList;

                    //实例化 endpoint 和 socket.
                    this._hostEndPoint = new IPEndPoint(addressList[addressList.Length - 1], port);
                }

                //现在有一个内存缓慢增加的问题，这里如此处理之后，未能解决问题。
                _sendArgs.Dispose();
                _sendArgs = null;
                _receiveArgs.Dispose();
                _receiveArgs = null;

                _clientSocket.Close();
                this._clientSocket = new Socket(this._hostEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                //设置这个Timeout应该是无效的(是有效的，必须设置为0，否则自动断线)
                _clientSocket.SendTimeout = 8 * 1000;
                _clientSocket.ReceiveTimeout = 0;
            } catch (Exception e) {
                LogProxy.LogWarning("SocketClient.Bind():绑定IP地址错误: " + e.Message);
                return;
            }
        }

        /// <summary>
        /// 连接到服务器,目前这个连接函数是阻塞的
        /// </summary>
        internal void Connect()
        {
            if (_sendArgs == null) {
                _sendArgs = new SocketAsyncEventArgs();
                _sendArgs.UserToken = new ConnectionContext {
                    socket = this._clientSocket,
                    sendBuffer = null,
                    recvBuffer = null,
                };
                //_sendArgs.RemoteEndPoint = null;
                _sendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendCompleted);
            }

            if (_receiveArgs == null) {
                _receiveArgs = new SocketAsyncEventArgs();
                _receiveArgs.UserToken = new ConnectionContext {
                    socket = this._clientSocket,
                    sendBuffer = null,
                    recvBuffer = _receiveBuffer,
                };
                _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
                _receiveArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnReceiveCompleted);
            }

            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs(); //创建一个SocketAsyncEventArgs类型

            connectArgs.UserToken = new ConnectionContext {
                socket = this._clientSocket,
                sendBuffer = null,
                recvBuffer = null,
            };
            connectArgs.RemoteEndPoint = this._hostEndPoint;
            connectArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnConnectCompleted); //加一个OnConnect来通知这个线程已经完成了

            if (!_clientSocket.ConnectAsync(connectArgs)) {
                OnConnectCompleted(this, connectArgs);
            }

            _areConnectDone.WaitOne(); //debug:这里可能不应该阻塞工作线程，或该在后面改进处理

            SocketError errorCode = connectArgs.SocketError;
            if (errorCode != SocketError.Success) {
                throw new SocketException((int)errorCode);
            }
        }

        /// <summary>
        /// 断开服务器的连接
        /// </summary>
        internal void Disconnect()
        {
            try {
                if (IsConnected) {
                    _clientSocket.Shutdown(SocketShutdown.Both);
                    _clientSocket.Disconnect(false); //不允许重用套接字
                }
            } catch (Exception e) {
                LogProxy.LogWarning("SocketClient.Disconnect()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 尝试标记为“正在发送”，若已在发送则返回 false
        /// </summary>
        private bool TryBeginSend()
        {
            return Interlocked.CompareExchange(ref _isSending, 1, 0) == 0;
        }

        /// <summary>
        /// 标记发送结束
        /// </summary>
        private void EndSend()
        {
            Interlocked.Exchange(ref _isSending, 0);
        }

        /// <summary>
        /// 从队列中取出数据包发送
        /// </summary>
        /// <returns></returns>
        /// <exception cref="SocketException"></exception>
        internal bool SendData()
        {
            if (IsConnected) {
#if !NEW_EVENT_AEGS
                // 尝试获取要发送的,这里应该对所有的数据进行整合然后一次性发送
                if (_sendQueue.Count == 0) {
                    return false;
                }

                // 一次同时只能发送一个数据包
                if (!TryBeginSend()) {
                    return false;
                }


                // 注意这里一定要全部整合一次性发送.
                List<ByteBuffer> buffers = new List<ByteBuffer>();
                while (_sendQueue.TryDequeue(out var sendBuff)) {
                    buffers.Add(sendBuff);
                }
                if (buffers.Count == 0) {
                    return false;
                }

                // 统计总长度
                int totalLength = buffers.Sum(x => x.Length);
                var sendBuffer = GlobalData.Inst.GetBuffer(totalLength);
                for (int i = 0; i < buffers.Count; i++) {
                    sendBuffer.Append(buffers[i]);
                    buffers[i].Recycle();
                }


                ConnectionContext context = _sendArgs.UserToken as ConnectionContext;
                context.sendBuffer = sendBuffer;
                _sendArgs.SetBuffer(context.sendBuffer.buffer, 0, context.sendBuffer.Length);

#else
                SocketAsyncEventArgs sendArgs = new SocketAsyncEventArgs();
                sendArgs.UserToken = this.clientSocket;
                sendArgs.RemoteEndPoint = this.hostEndPoint;
                sendArgs.SetBuffer(data, 0, data.Length);
                sendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnSend);
#endif
                PrepareSend(_clientSocket, _sendArgs);

                return true;
            }
            else {
                throw new SocketException((int)SocketError.NotConnected);
            }
        }

        /// <summary>
        /// 清空当前所有的队列和数据结构
        /// </summary>
        internal void Clear()
        {
            // _dataQueue.Clear();
        }

        #endregion Exposed Function

        #region Callback

        private void OnConnectCompleted(object sender, SocketAsyncEventArgs args)
        {
            try {
                // 这种回调是新开了一个线程执行的
                _areConnectDone.Set();

                if (IsConnected) {
                    PrepareReceive(); // 自动开始一个接收
                }
                else {
                    LogProxy.LogWarning("SocketClient.ProcessConnect():没能自动开始接收 IsConnected = " + IsConnected);
                }
                if (EventConnectCompleted != null) // 执行事件
                {
                    EventConnectCompleted();
                }
                args.UserToken = null;// 成功了. 可以不用了.
            } catch (Exception e) {
                LogProxy.LogWarning($"SocketClient.ProcessConnect():异常 {e}");
            }
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs args)
        {
            try {

                if (args.SocketError != SocketError.Success) {
                    this.ProcessError(args);
                }
                if (args.BytesTransferred > 0) //有可能会出现接收到的数据长度为0的情形，如当服务器关闭连接的时候
                {
                    //写入当前接收的数据
                    var msgs = _packet2.Unpack(args.Buffer, args.Offset, args.BytesTransferred);
                    if (msgs != null) {
                        msgs.ForEach(msg => {
                            _receQueue.Enqueue(msg);
                        });
                    }
                    int msgCount = msgs == null ? 0 : msgs.Count;

                    //int msgCount = _packet2.AddRece(args.Buffer, args.Offset, args.BytesTransferred);
                    //如果确实收到了一条消息.执行事件
                    if (msgCount > 0 && EventReceiveCompleted != null) {
                        EventReceiveCompleted();
                    }
                }
                else {
                    this.ProcessError(args);
                }
                PrepareReceive(); //开始下一个接收
            } catch (Exception e) {
                LogProxy.LogWarning($"SocketClient.OnReceiveCompleted():异常 {e}");
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs args)
        {
            try {
                // 标记发送完成
                EndSend();

                if (args.SocketError != SocketError.Success) {
                    this.ProcessError(args);
                }
                // 回收发送缓冲区
                ConnectionContext context = args.UserToken as ConnectionContext;
                context?.sendBuffer?.Recycle();


                //执行事件,这是.net池的线程,这个事件中如果再执行SendData()
                if (EventSendCompleted != null) {
                    EventSendCompleted();
                }

                // 继续发送下一条数据
                if (_sendQueue.Count > 0)
                    SendData();

            } catch (Exception e) {
                LogProxy.LogWarning($"SocketClient.OnSendCompleted(): {e}");
            }
        }

        #endregion Callback

        #region BuiltIn Function

        /// <summary>
        /// 发生错误后就关闭连接
        /// </summary>
        /// <param name="e"></param>
        private void ProcessError(SocketAsyncEventArgs e)
        {
            LogProxy.LogWarning("SocketClient.ProcessError():进入了ProcessError.  ErroType：" + e.SocketError); //显示下接收的信息
            ConnectionContext context = e.UserToken as ConnectionContext; //使用传递的Token
            if (context.socket.Connected) {
                try {
                    LogProxy.LogDebug("SocketClient.ProcessError():调用Shutdown()关闭连接");
                    context.socket.Shutdown(SocketShutdown.Both);
                } catch (Exception ex) {
                    LogProxy.LogWarning("SocketClient.ProcessError() ：Shutdown()异常 " + ex.Message);
                } finally {
                    if (context.socket.Connected) {
                        context.socket.Close();
                        LogProxy.LogWarning("SocketClient.ProcessError() ：调用Close()关闭了连接"); //这里是否必须要关闭待定
                    }
                }
            }
            //产生错误事件，这是一个很重要的事件，处理服务器连接断开等
            if (EventError != null) {
                EventError();
            }
        }

        /// <summary>
        /// 开始异步发送
        /// </summary>
        /// <param name="s"></param>
        /// <param name="args"></param>
        private void PrepareSend(Socket s, SocketAsyncEventArgs args)
        {
            try {
                // 这个判断不严谨
                if (!s.Connected) //如果当前没有连接上，就不发送
                {
                    LogProxy.LogWarning("SocketClient.PrepareSend() 当前已经断线，但仍尝试发送，已经忽略这条发送.");
                    return;
                }
                // 开始发送,这里作异常处理
                if (!s.SendAsync(args)) {
                    OnSendCompleted(this, args); //如果立即返回
                }
            } catch (Exception e) {
                LogProxy.LogWarning("SocketClient.PrepareSend() 开始准备异步发送出错！！" + e.Message);
                //这里捕获过的异常有：
                // Thread creation failed.
            }
        }

        /// <summary>
        /// 开始一个接收
        /// </summary>
        private void PrepareReceive()
        {
            try {
                //这里是需要的，否则在断线之后仍然可能不停的接收
                if (!_clientSocket.Connected) //如果当前没有连接上，就不接收了
                {
                    return;
                }

#if !NEW_EVENT_AEGS
                _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
#else
            SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
            receiveArgs.UserToken = this.clientSocket;
            receiveArgs.RemoteEndPoint = this.hostEndPoint;
            byte[] receiveBuffer = new byte[2048]; //接收buffer大小为2048;
            receiveArgs.SetBuffer(receiveBuffer, 0, receiveBuffer.Length);
            receiveArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnReceive);
#endif
                if (!_clientSocket.ReceiveAsync(_receiveArgs)) //开始接收
                {
                    OnReceiveCompleted(this, _receiveArgs);
                }

            } catch (Exception e) {
                LogProxy.LogWarning("SocketClient.PrepareReceive() 开始异步接收错误：" + e.Message);
                //这里捕获过的异常有：
                // Thread creation failed.
            }
        }

        #endregion BuiltIn Function

        #region IDisposable implementation

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            try {
                if (disposed) {
                    return;
                }
                //快速的尝试掉线？
                //_clientSocket.SendTimeout = 500;
                //_clientSocket.ReceiveTimeout = 500;

                //断开连接
                Disconnect();

                if (disposing) {
                    // 清理托管资源
                    EventConnectCompleted = null;
                    EventReceiveCompleted = null;
                    EventSendCompleted = null;
                    EventError = null;
                }
                // 清理非托管资源
                if (this._sendArgs != null) {
                    _sendArgs.UserToken = null;
                    _sendArgs.Dispose();

                }
                if (this._receiveArgs != null) {
                    _sendArgs.UserToken = null;
                    _receiveArgs.Dispose();
                }
                if (this._clientSocket != null) {
                    this._clientSocket.Close();
                }

                _areConnectDone.Close();
                _areConnectDone = null;
                //_areSendDone.Close();
                //_areReceiveDone.Close();
            } catch (Exception e) {
                LogProxy.LogWarning("SocketClient.Dispose() 异常：" + e.Message);
            }
            //让类型知道自己已经被释放
            disposed = true;
        }

        #endregion IDisposable implementation
    }
}
