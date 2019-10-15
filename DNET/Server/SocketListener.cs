using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace DNET
{
    internal class SocketListener : IDisposable
    {
        #region Constructor

        /// <summary>
        /// 构造函数：创建一个未初始化的服务器实例。
        /// 来开始一个监听服务，
        /// 调用Init方法之后再Start方法
        /// </summary>
        /// <param name="server">它所关联的DNServer</param>
        /// <param name="bufferSize">给每个连接的IO操作的buffer大小</param>
        internal SocketListener(DNServer server, int bufferSize)
        {
            this._dnserver = server;
            this._bufferSize = bufferSize;

            //这里可能无法限制最大连接数，需要在用户连接的回调中处理最大连接数
        }

        #endregion Constructor

        #region Fields

        /// <summary>
        /// 保存创建它的DNServer,用来在token中标记归属
        /// </summary>
        private DNServer _dnserver;

        /// <summary>
        /// 用于监听的套接字的连接请求。
        /// </summary>
        private Socket _listenSocket;

        /// <summary>
        /// 缓冲区大小为每个套接字I / O操作使用。
        /// </summary>
        private int _bufferSize;

        /// <summary>
        /// 服务器启动成功标志
        /// </summary>
        private bool _isStarted = false;

        /// <summary>
        /// 专门给Accept用的
        /// </summary>
        private SocketAsyncEventArgs _acceptEventArg;

        /// <summary>
        /// 专门给Accept用的,改为一组，在代码里设置为使用2个
        /// </summary>
        private SocketAsyncEventArgs[] _acceptEventArgs;

        /// <summary>
        /// 一个锁尝试解决并发Accept问题，现在此问题已解决，这个锁还是保留
        /// </summary>
        private object _lockAccept = new object();

        #endregion Fields

        #region Property

        /// <summary>
        /// 服务器启动成功标志
        /// </summary>
        public bool IsStarted
        {
            get { return _isStarted; }
        }

        #endregion Property

        #region Event

        /// <summary>
        /// 接受认证完成
        /// </summary>
        internal event Action<Token> EventAccept;

        /// <summary>
        /// 数据发送完毕
        /// </summary>
        internal event Action<Token> EventSend;

        /// <summary>
        /// 数据接收完毕
        /// </summary>
        internal event Action<Token> EventReceive;

        /// <summary>
        /// 关闭了某个客户端
        /// </summary>
        internal event Action<Token, SocketError> EventError;

        private bool disposed;

        #endregion Event

        #region Exposed Function

        /// <summary>
        /// 启动服务器
        /// </summary>
        /// <param name="hostName">服务器的ip</param>
        /// <param name="port">本机的服务器端口</param>
        internal void Start(string hostName, int port)
        {
            try
            {
                _isStarted = false;
                IPAddress address = IPAddress.Any;
                if (hostName == "Any")
                {
                    address = IPAddress.Any;
                }
                else if (Regex.IsMatch(hostName, @"\d{1,3}[.]\d{1,3}[.]\d{1,3}[.]\d{1,3}"))
                {
                    byte[] ipadr = new byte[4];

                    MatchCollection ms = Regex.Matches(hostName, @"\d{1,3}");
                    for (int i = 0; i < ms.Count; i++)
                    {
                        ipadr[i] = Convert.ToByte(hostName.Substring(ms[i].Index, ms[i].Length));
                    }
                    address = new IPAddress(ipadr);
                }
                else
                {
                    IPHostEntry host = Dns.GetHostEntry(hostName);
                    IPAddress[] addressList = host.AddressList;
                    address = addressList[addressList.Length - 1];

                }
                IPEndPoint localEndPoint = new IPEndPoint(address, port);
                DxDebug.LogConsole("SocketListener.Start():尝试启动服务器 " + address + ":" + port);

                //创建一个监听Socket
                this._listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                this._listenSocket.ReceiveBufferSize = this._bufferSize;
                this._listenSocket.SendBufferSize = this._bufferSize;

                if (localEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    this._listenSocket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, false);
                    this._listenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, localEndPoint.Port));
                }
                else
                {
                    this._listenSocket.Bind(localEndPoint);
                }
                this._listenSocket.Listen(2); //最大挂起数
                this.StartAccept2();

                _isStarted = true;//服务器启动成功
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketListener.Start()：Start函数错误：" + e.Message);
            }
        }

        /// <summary>
        /// 由Socket开始一个异步发送
        /// </summary>
        /// <param name="token"></param>
        /// <param name="data"></param>
        internal void Send(Token token, byte[] data)
        {
            int errorCount = 0;
            try
            {
                if (token.disposed == false)//如果这个token已经被释放，那就不要再发送了
                {
                    token.IncrementSendingCount();//计数递增：这里需要及早标记，否则多线程调用SocketAsyncEventArgs会异常。

                    SocketAsyncEventArgs sendEventArgs = token.SendArgs;
                    sendEventArgs.SetBuffer(data, 0, data.Length);

                    //这里这个有可能会出现异常:"现在已经正在使用此 SocketAsyncEventArgs 实例进行异步套接字操作。
                    //所以这句可能要加锁
                    if (!token.socket.SendAsync(sendEventArgs))  //开始发送  ,这里作异常处理()
                    {
                        OnCompletedProcessSend(this, sendEventArgs);
                    }
                }
            }
            catch (Exception e)
            {
                errorCount++;
                DxDebug.LogWarning("SocketListener.Send()：异常:" + e.Message);
                token.DecrementSendingCount();//直接异常了就去掉这个计数递减
                if (errorCount <= 2)
                {
                    DxDebug.LogConsole("SocketListener.Send()尝试自动重试！errorCount=" + errorCount);
                    Send(token, data);
                }
            }
        }

        #endregion Exposed Function

        #region Callback

        /// <summary>
        /// 这个函数只绑定了acceptEventArg
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnIOCompleted(object sender, SocketAsyncEventArgs e)
        {
            //lock (_lockAccept)
            //{
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Accept:
                    //当关闭Socket的时候，如果没有注销事件，那么也会进入这里，所以作了一个判断(同时注销事件也完善了)
                    if (e.SocketError != SocketError.OperationAborted)
                    {
                        this.ProcessAccept(e);
                    }
                    break;

                /*  case SocketAsyncOperation.Receive:
                      this.ProcessReceive(this,e);
                      break;

                  case SocketAsyncOperation.Send:
                      this.ProcessSend(this,e);
                      break;*/
                default:
                    DxDebug.LogWarning("SocketListener：OnIOCompleted(): 进入了未预料的switch分支！");
                    break;
            }
            //}
        }

        /// <summary>
        /// Accept的处理。
        /// </summary>
        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            try
            {
                Socket s = e.AcceptSocket;
                if (s.Connected)
                {
                    //创建了两个事件参数（接收发送给Token）
                    SocketAsyncEventArgs receiveEventArgs = new SocketAsyncEventArgs();
                    receiveEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnCompletedProcessReceive);
                    SocketAsyncEventArgs sendEventArgs = new SocketAsyncEventArgs();
                    sendEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnCompletedProcessSend);

                    Token token = new Token(s, sendEventArgs, receiveEventArgs, _bufferSize); //创建一个用户Token
                    token.server = this._dnserver;
                    sendEventArgs.UserToken = token; //绑定一个用户Token
                    receiveEventArgs.UserToken = token; //绑定一个用户Token
                    receiveEventArgs.SetBuffer(token.ReceiveBuffer, 0, token.ReceiveBuffer.Length);

                    TokenManager.GetInstance().AddToken(token);//把这个用户加入TokenManager

                    if (EventAccept != null) //产生认证事件
                    {
                        EventAccept(token);
                    }
                    PrepareReceive(s, receiveEventArgs);//开始接收
                }

                this.StartAccept(e);
            }
            catch (SocketException ex)
            {
                //Token token = e.UserToken as Token;
                DxDebug.LogWarning(String.Format("SocketListener.ProcessAccept()：Socket异常,接收认证连接出现错误 {0}", ex.Message));
            }
            catch (Exception ex)
            {
                DxDebug.LogWarning("SocketListener.ProcessAccept()：异常" + ex.Message);
            }
        }

        /// <summary>
        /// 执行异步接收完成处理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnCompletedProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                // 如果返回0说明远程端已经关闭了连接,MSDN说明如下：
                // 此属性提供在可接收或发送数据的异步套接字操作传输的字节数。 如果从读取操作返回零，则说明远程端已关闭了连接。
                if (e.BytesTransferred > 0)
                {
                    if (e.SocketError == SocketError.Success)
                    {
                        Token token = e.UserToken as Token;
                        token.SetData(e);

                        /* Socket s = token.socket;
                         if (s.Available == 0)
                         {
                             if (EventReceive != null)//产生接收事件，通知线程进行解包
                             {
                                 EventReceive(token);
                             }
                             e.SetBuffer(token.ReceiveBuffer, 0, token.ReceiveBuffer.Length);
                         }*/

                        Socket s = token.socket;//Available的用法不太明确，但是这里有大概率是!=0的
                        //if (s.Available != 0)
                        //{
                        //    DxDebug.LogWarning("SocketListener.OnCompletedProcessReceive():s.Available != 0");
                        //}
                        e.SetBuffer(token.ReceiveBuffer, 0, token.ReceiveBuffer.Length);
                        if (EventReceive != null)//产生接收事件，通知线程进行解包
                        {
                            EventReceive(token);
                        }
                        PrepareReceive(s, e); //开始下一个接收
                    }
                    else
                    {
                        this.ProcessError(e);
                    }
                }
                else
                {
                    DxDebug.LogWarning("SocketListener.OnCompletedProcessReceive():BytesTransferred函数返回了零，说明远程已经关闭了连接，关闭这个用户。");
                    Token token = e.UserToken as Token;
                    TokenManager.GetInstance().DeleteToken(token.ID, TokenErrorType.BytesTransferredZero);//关闭Token
                    // token.Close();
                }
            }
            catch (Exception ex)
            {
                DxDebug.LogWarning("SocketListener.OnCompletedProcessReceive():异常：" + ex.Message);
            }
        }

        /// <summary>
        /// 执行异步发送完成
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnCompletedProcessSend(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError == SocketError.Success)
                {
                    Token token = e.UserToken as Token; //获取token
                    token.DecrementSendingCount();//计数递减
                    if (EventSend != null)//产生发送完成事件
                    {
                        EventSend(token);
                    }
                }
                else
                {
                    this.ProcessError(e);
                }
            }
            catch (Exception ex)
            {
                DxDebug.LogWarning("SocketListener.OnCompletedProcessSend():异常：" + ex.Message);
            }
        }

        /// <summary>
        /// 发生了错误，通常是断开连接了
        /// </summary>
        /// <param name="e"></param>
        private void ProcessError(SocketAsyncEventArgs e)
        {
            try
            {
                Token token = e.UserToken as Token;
                IPEndPoint localEp = token.socket.LocalEndPoint as IPEndPoint;

                DxDebug.LogConsole(string.Format("SocketListener.ProcessError()：SocketError:{0}  IP:{1}  上次操作:{2}.", e.SocketError, localEp, e.LastOperation));
                TokenManager.GetInstance().DeleteToken(token.ID, e.SocketError);
                //  token.Close();//执行关闭
                if (EventError != null)// 执行事件
                {
                    EventError(token, e.SocketError);
                }
            }
            catch (Exception ex)
            {
                DxDebug.LogWarning("SocketListener.ProcessError():异常：" + ex.Message);
            }
        }

        #endregion Callback

        #region BuiltIn Function

        /// <summary>
        /// 开始接受客户端的Accept
        /// </summary>
        private void StartAccept(SocketAsyncEventArgs eArg)
        {
            //lock (_lockAccept)//这个加锁没有解决问题
            //{
            try
            {
                if (eArg == null)
                {
                    eArg = new SocketAsyncEventArgs();
                    eArg.Completed += new EventHandler<SocketAsyncEventArgs>(OnIOCompleted);
                }

                eArg.AcceptSocket = null;   // 必须要先清掉Socket

                DxDebug.LogConsole("SocketListener.StartAccept():服务器开始接收认证!");
                //开始异步接收认证
                if (!_listenSocket.AcceptAsync(eArg))
                {
                    this.ProcessAccept(eArg);
                }
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketListener.StartAccept():异常：" + e.Message);
                // throw;
            }
            //}
        }

        /// <summary>
        /// 开始接受客户端的Accept,使用一组SocketAsyncEventArgs
        /// </summary>
        private void StartAccept2()
        {
            //lock (_lockAccept)//这个加锁没有解决问题
            //{
            try
            {
                if (_acceptEventArgs == null)
                {
                    _acceptEventArgs = new SocketAsyncEventArgs[2];
                    for (int i = 0; i < _acceptEventArgs.Length; i++)
                    {
                        _acceptEventArgs[i] = new SocketAsyncEventArgs();
                        _acceptEventArgs[i].Completed += new EventHandler<SocketAsyncEventArgs>(OnIOCompleted);
                    }
                }
                for (int i = 0; i < _acceptEventArgs.Length; i++)
                {
                    _acceptEventArgs[i].AcceptSocket = null;   // 必须要先清掉Socket
                }

                DxDebug.LogConsole("SocketListener.StartAccept2():服务器开始接收认证!");
                for (int i = 0; i < _acceptEventArgs.Length; i++)
                {
                    //开始异步接收认证
                    if (!_listenSocket.AcceptAsync(_acceptEventArgs[i]))
                    {
                        this.ProcessAccept(_acceptEventArgs[i]);
                    }
                }
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketListener.StartAccept2():异常：" + e.Message);
                throw;
            }
            //}
        }

        /// <summary>
        /// 开始一个接收
        /// </summary>
        /// <param name="s"></param>
        /// <param name="args"></param>
        private void PrepareReceive(Socket s, SocketAsyncEventArgs args)
        {
            try
            {
                if (!s.ReceiveAsync(args)) //开始下一次接收
                {
                    this.OnCompletedProcessReceive(this, args);
                }
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketListener：开始异步接收错误：" + e.Message);
                throw;
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
            DxDebug.LogWarning("SocketListener.Dispose()：进入了Dispose!");
            if (disposed)
            {
                return;
            }
            disposed = true;

            if (disposing)
            {
                _isStarted = false;

                // 清理托管资源
                EventAccept = null;
                EventSend = null;
                EventReceive = null;
                EventError = null;
            }
            // 清理非托管资源
            try
            {
                if (_acceptEventArg != null)
                {
                    _acceptEventArg.Completed -= new EventHandler<SocketAsyncEventArgs>(OnIOCompleted);
                    _acceptEventArg.Dispose();
                    _acceptEventArg = null;
                }
                if (_acceptEventArgs != null)
                {
                    for (int i = 0; i < _acceptEventArgs.Length; i++)
                    {
                        _acceptEventArgs[i].Completed -= new EventHandler<SocketAsyncEventArgs>(OnIOCompleted);
                        _acceptEventArgs[i].Dispose();
                    }
                    _acceptEventArgs = null;
                }

                _listenSocket.Close();
                DxDebug.LogWarning("SocketListener.Dispose()：关闭了服务器Socket");
                DxDebug.LogWarning("SocketListener.Dispose()：删除所有用户");
                TokenManager.GetInstance().DeleteAllToken();//关闭所有Token
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("SocketListener.Dispose()：异常：" + e.Message);
            }
        }

        #endregion IDisposable implementation
    }
}