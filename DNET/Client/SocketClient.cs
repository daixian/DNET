using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 客户机实现套接字的连接逻辑。
    /// </summary>
    internal sealed class SocketClient : IDisposable
    {
        #region Constructor

        /// <summary>
        /// 构造函数： 创建出一个Socket对象
        /// </summary>
        internal SocketClient(string hostName, int port)
        {
            try
            {
                IPHostEntry host;
                if (Regex.IsMatch(hostName, @"\d{1,3}[.]\d{1,3}[.]\d{1,3}[.]\d{1,3}"))
                {
                    byte[] ipadr = new byte[4];

                    MatchCollection ms = Regex.Matches(hostName, @"\d{1,3}");
                    for (int i = 0; i < ms.Count; i++)
                    {
                        ipadr[i] = Convert.ToByte(hostName.Substring(ms[i].Index, ms[i].Length));
                    }

                    IPAddress address = new IPAddress(ipadr);
                    this._hostEndPoint = new IPEndPoint(address, port);
                }
                else
                {
                    host = Dns.GetHostEntry(hostName);
                    IPAddress[] addressList = host.AddressList;

                    //实例化 endpoint 和 socket.
                    this._hostEndPoint = new IPEndPoint(addressList[addressList.Length - 1], port);
                }

                this._clientSocket = new Socket(this._hostEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                //设置这个Timeout应该是无效的(是有效的，必须设置为0，否则自动断线)
                _clientSocket.SendTimeout = 8 * 1000;
                _clientSocket.ReceiveTimeout = 0;

                /*   TcpClient client = new TcpClient(hostName, port);
                   this._hostEndPoint = client.Client.RemoteEndPoint;
                   this._clientSocket = client.Client;*/
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketClient.SocketClient():类构造函数错误: " + e.Message);
                return;
            }

            if (_areConnectDone == null)
                _areConnectDone = new AutoResetEvent(false);
            if (_receiveBuffer == null)
                _receiveBuffer = new byte[RECE_BUFFER_SIZE];
            if (_dataQueue == null)
                _dataQueue = new BytesQueue(int.MaxValue, MAX_DATA_QUEUE_BYTES_SIZE, 256);

#if !NEW_EVENT_AEGS

            _sendArgs = new SocketAsyncEventArgs();
            _sendArgs.UserToken = this._clientSocket; //利用了Token
            //_sendArgs.RemoteEndPoint = null;
            _sendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(ProcessSend);

            _receiveArgs = new SocketAsyncEventArgs();
            _receiveArgs.UserToken = this._clientSocket;//利用了Token
            _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
            _receiveArgs.Completed += new EventHandler<SocketAsyncEventArgs>(ProcessReceive);
#endif
            DxDebug.Log("SocketClient.SocketClient().SocketClient类构造对象成功！");
        }

        #endregion Constructor

        #region Fields

        /// <summary>
        /// 接收buffer大小
        /// </summary>
        private const int RECE_BUFFER_SIZE = 512 * 1024; //512k

        ///// <summary>
        ///// 消息的队列最大长度
        ///// </summary>
        //private const int MAX_DATA_QUEUE_LENGTH = 1024; //最多保存 1024条未处理的

        /// <summary>
        /// 消息队列的最大内存大小
        /// </summary>
        private const int MAX_DATA_QUEUE_BYTES_SIZE = 6 * 1024 * 1024;

        /// <summary>
        /// 套接字用于发送/接收消息。
        /// </summary>
        internal Socket _clientSocket;

        /// <summary>
        /// 主机网络端点
        /// </summary>
        private EndPoint _hostEndPoint;

        /// <summary>
        /// 信号量，通知等待的线程已经发生了事件
        /// </summary>
        private AutoResetEvent _areConnectDone = null;

        //private AutoResetEvent _areSendDone = new AutoResetEvent(false);
        //private AutoResetEvent _areReceiveDone = new AutoResetEvent(false);

#if !NEW_EVENT_AEGS

        private SocketAsyncEventArgs _sendArgs = null;
        private SocketAsyncEventArgs _receiveArgs = null;

        /// <summary>
        /// 接收的buffer缓冲区
        /// </summary>
        private byte[] _receiveBuffer;

#endif

        /// <summary>
        /// 存贮当前接收到的消息的队列
        /// </summary>
        private BytesQueue _dataQueue = null;

        /// <summary>
        /// IO消耗时间计算
        /// </summary>
        private DThreadTimeAnalyze _sendTime = new DThreadTimeAnalyze();

        private DThreadTimeAnalyze _receTime = new DThreadTimeAnalyze();

        private bool disposed = false;

        #endregion Fields

        #region Property

        /// <summary>
        /// 是否已经连接上了服务器
        /// </summary>
        internal bool IsConnected
        {
            get
            {
                if (_clientSocket != null)
                {
                    return _clientSocket.Connected;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 得到Socket
        /// </summary>
        public Socket socket
        {
            get
            {
                return _clientSocket;
            }
        }

        /// <summary>
        /// 通信发送的IO占用率（百分率）
        /// </summary>
        internal double SendOccupancyRate
        {
            get { return _sendTime.OccupancyRate; }
        }

        /// <summary>
        /// 通信接收的IO占用率（百分率）
        /// </summary>
        internal double ReceOccupancyRate
        {
            get { return _receTime.OccupancyRate; }
        }

        #endregion Property

        #region Event

        /// <summary>
        /// 出现错误
        /// </summary>
        internal event Action EventError;

        /// <summary>
        /// 连接成功的事件
        /// </summary>
        internal event Action EventConnect;

        /// <summary>
        /// 数据发送完毕
        /// </summary>
        internal event Action EventSend;

        /// <summary>
        /// 数据接收完毕
        /// </summary>
        internal event Action EventReceive;

        #endregion Event

        #region Exposed Function

        /// <summary>
        /// 重新绑定一个IP地址
        /// </summary>
        /// <param name="hostName"></param>
        /// <param name="port"></param>
        internal void Bind(string hostName, int port)
        {
            try
            {
                IPHostEntry host;
                if (Regex.IsMatch(hostName, @"\d{1,3}[.]\d{1,3}[.]\d{1,3}[.]\d{1,3}"))
                {
                    byte[] ipadr = new byte[4];

                    MatchCollection ms = Regex.Matches(hostName, @"\d{1,3}");
                    for (int i = 0; i < ms.Count; i++)
                    {
                        ipadr[i] = Convert.ToByte(hostName.Substring(ms[i].Index, ms[i].Length));
                    }

                    IPAddress address = new IPAddress(ipadr);
                    this._hostEndPoint = new IPEndPoint(address, port);
                }
                else
                {
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
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketClient.Bind():绑定IP地址错误: " + e.Message);
                return;
            }
        }

        /// <summary>
        /// 连接到服务器,目前这个连接函数是阻塞的
        /// </summary>
        internal void Connect()
        {
            if (_sendArgs == null)
            {
                _sendArgs = new SocketAsyncEventArgs();
                _sendArgs.UserToken = this._clientSocket; //利用了Token
                                                          //_sendArgs.RemoteEndPoint = null;
                _sendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(ProcessSend);
            }

            if (_receiveArgs == null)
            {
                _receiveArgs = new SocketAsyncEventArgs();
                _receiveArgs.UserToken = this._clientSocket;//利用了Token
                _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
                _receiveArgs.Completed += new EventHandler<SocketAsyncEventArgs>(ProcessReceive);
            }

            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs();//创建一个SocketAsyncEventArgs类型

            connectArgs.UserToken = this._clientSocket;
            connectArgs.RemoteEndPoint = this._hostEndPoint;
            connectArgs.Completed += new EventHandler<SocketAsyncEventArgs>(ProcessConnect); //加一个OnConnect来通知这个线程已经完成了

            if (!_clientSocket.ConnectAsync(connectArgs))
            {
                ProcessConnect(this, connectArgs);
            }

            _areConnectDone.WaitOne();//debug:这里可能不应该阻塞工作线程，或该在后面改进处理

            SocketError errorCode = connectArgs.SocketError;
            if (errorCode != SocketError.Success)
            {
                throw new SocketException((int)errorCode);
            }
        }

        /// <summary>
        /// 断开服务器的连接
        /// </summary>
        internal void Disconnect()
        {
            try
            {
                if (IsConnected)
                {
                    _clientSocket.Shutdown(SocketShutdown.Both);
                    _clientSocket.Disconnect(false);//不允许重用套接字
                }

                Clear();//顺便清空队列
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketClient.Disconnect()：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        internal void SendData(byte[] data)
        {
            if (IsConnected)
            {
#if !NEW_EVENT_AEGS
                _sendArgs.SetBuffer(data, 0, data.Length);
#else
                SocketAsyncEventArgs sendArgs = new SocketAsyncEventArgs();
                sendArgs.UserToken = this.clientSocket;
                sendArgs.RemoteEndPoint = this.hostEndPoint;
                sendArgs.SetBuffer(data, 0, data.Length);
                sendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnSend);
#endif
                PrepareSend(_clientSocket, _sendArgs);

                _sendTime.WorkStart();
                return;
            }
            else
            {
                throw new SocketException((int)SocketError.NotConnected);
            }
        }

        /// <summary>
        /// 得到当前缓存的数据,返回byte[][]的形式,没有则返回null
        /// </summary>
        internal byte[][] GetData()
        {
            return _dataQueue.GetData();
        }

        /// <summary>
        /// 得到当前缓存的数据,返回byte[]的形式,没有则返回reserveData本身
        /// </summary>
        /// <param name="reserveData"></param>
        /// <returns></returns>
        internal byte[] GetDataOnce(byte[] reserveData)
        {
            return _dataQueue.GetDataOnce(reserveData);
        }

        /// <summary>
        /// 清空当前所有的队列和数据结构
        /// </summary>
        internal void Clear()
        {
            _dataQueue.Clear();
        }

        #endregion Exposed Function

        #region Callback

        private void ProcessConnect(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                //这种回调是新开了一个线程执行的
                _areConnectDone.Set();

                if (IsConnected)
                {
                    PrepareReceive(); //自动开始一个接收
                }
                else
                {
                    DxDebug.LogWarning("SocketClient.ProcessConnect():没能自动开始接收 IsConnected = " + IsConnected);
                }
                if (EventConnect != null) //执行事件
                {
                    EventConnect();
                }
            }
            catch (Exception ex)
            {
                DxDebug.LogWarning("SocketClient.ProcessConnect():异常：" + ex.Message);
            }
        }

        private void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            _receTime.WorkStart();
            try
            {
                //_areReceiveDone.Set();//设置信号量(但是目前压根没用到)
                if (e.SocketError != SocketError.Success)
                {
                    this.ProcessError(e);
                }
                if (e.BytesTransferred > 0) //有可能会出现接收到的数据长度为0的情形，如当服务器关闭连接的时候
                {
                    byte[] data = new byte[e.BytesTransferred]; //当次接收的数据
                    Buffer.BlockCopy(e.Buffer, e.Offset, data, 0, data.Length);
                    EnqueueData(data); //记录数据

                    if (EventReceive != null) //执行事件
                    {
                        EventReceive();
                    }
                }
                else
                {
                    this.ProcessError(e);
                }
                PrepareReceive(); //开始下一个接收
            }
            catch (Exception ex)
            {
                DxDebug.LogWarning("SocketClient：ProcessReceive():" + ex.Message);
            }
        }

        private void ProcessSend(object sender, SocketAsyncEventArgs e)
        {
            _sendTime.WaitStart();
            try
            {
                //_areSendDone.Set();//设置信号量
                if (e.SocketError != SocketError.Success)
                {
                    this.ProcessError(e);
                }
                if (EventSend != null) //执行事件
                {
                    EventSend();
                }
            }
            catch (Exception ex)
            {
                DxDebug.LogWarning("SocketClient：ProcessSend()：ProcessSend可能设置信号量异常 " + ex.Message);
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
            DxDebug.LogWarning("SocketClient.ProcessError():进入了ProcessError.  ErroType：" + e.SocketError); //显示下接收的信息
            Socket s = e.UserToken as Socket; //使用传递的Token
            if (s.Connected)
            {
                try
                {
                    DxDebug.LogConsole("SocketClient.ProcessError():调用Shutdown()关闭连接");
                    s.Shutdown(SocketShutdown.Both);
                }
                catch (Exception ex)
                {
                    DxDebug.LogWarning("SocketClient.ProcessError() ：Shutdown()异常 " + ex.Message);
                }
                finally
                {
                    if (s.Connected)
                    {
                        s.Close();
                        DxDebug.LogWarning("SocketClient.ProcessError() ：调用Close()关闭了连接");//这里是否必须要关闭待定
                    }
                }
            }
            //产生错误事件，这是一个很重要的事件，处理服务器连接断开等
            if (EventError != null)
            {
                EventError();
            }
        }

        /// <summary>
        /// 将数据加入到接收缓存数据队列
        /// </summary>
        private void EnqueueData(byte[] data)
        {
            if (!_dataQueue.EnqueueMaxLimit(data))
            {
                DxDebug.LogWarning("SocketClient.EnqueueData():接收缓存数据队列丢弃了一段数据");
            }
            return;
        }

        /// <summary>
        /// 开始异步发送
        /// </summary>
        /// <param name="s"></param>
        /// <param name="args"></param>
        private void PrepareSend(Socket s, SocketAsyncEventArgs args)
        {
            try
            {
                //这个判断不严谨
                if (!s.Connected) //如果当前没有连接上，就不发送
                {
                    DxDebug.LogWarning("SocketClient.PrepareSend() 当前已经断线，但仍尝试发送，已经忽略这条发送.");
                    return;
                }

                if (!s.SendAsync(args))  //开始发送  ,这里作异常处理
                {
                    ProcessSend(this, args);//如果立即返回
                }
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketClient.PrepareSend() 开始准备异步发送出错！！" + e.Message);
                //这里捕获过的异常有：
                // Thread creation failed.
            }
        }

        /// <summary>
        /// 开始一个接收
        /// </summary>
        private void PrepareReceive()
        {
            try
            {
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
                if (!_clientSocket.ReceiveAsync(_receiveArgs))//开始接收
                {
                    ProcessReceive(this, _receiveArgs);
                }
                _receTime.WaitStart();
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketClient.PrepareReceive() 开始异步接收错误：" + e.Message);
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
            try
            {
                if (disposed)
                {
                    return;
                }
                //快速的尝试掉线？
                //_clientSocket.SendTimeout = 500;
                //_clientSocket.ReceiveTimeout = 500;

                //断开连接
                Disconnect();

                if (disposing)
                {
                    // 清理托管资源
                    EventConnect = null;
                    EventReceive = null;
                    EventSend = null;
                    EventError = null;

                    _dataQueue.Clear();
                    _dataQueue = null;
                }
                // 清理非托管资源
                if (this._sendArgs != null)
                {
                    _sendArgs.Dispose();
                }
                if (this._receiveArgs != null)
                {
                    _receiveArgs.Dispose();
                }
                if (this._clientSocket != null)
                {
                    this._clientSocket.Close();
                }

                _areConnectDone.Close();
                _areConnectDone = null;
                //_areSendDone.Close();
                //_areReceiveDone.Close();
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketClient.Dispose() 异常：" + e.Message);
            }
            //让类型知道自己已经被释放
            disposed = true;
        }

        #endregion IDisposable implementation
    }
}