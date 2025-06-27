using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace DNET
{
    internal class ServerListenerSocket : IDisposable
    {
        /// <summary>
        /// 构造函数：创建一个未初始化的服务器实例。
        /// 来开始一个监听服务，
        /// </summary>
        internal ServerListenerSocket()
        {
        }

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

        /// <summary>
        /// 服务器启动成功标志
        /// </summary>
        public bool IsStarted => _isStarted;

        #region Event

        /// <summary>
        /// 接受认证完成
        /// </summary>
        internal event Action<Peer> EventAccept;


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
            try {
                _isStarted = false;
                IPAddress address = IPAddress.Any;
                if (hostName == "Any") {
                    address = IPAddress.Any;
                }
                else if (Regex.IsMatch(hostName, @"\d{1,3}[.]\d{1,3}[.]\d{1,3}[.]\d{1,3}")) {
                    byte[] ipadr = new byte[4];

                    MatchCollection ms = Regex.Matches(hostName, @"\d{1,3}");
                    for (int i = 0; i < ms.Count; i++) {
                        ipadr[i] = Convert.ToByte(hostName.Substring(ms[i].Index, ms[i].Length));
                    }
                    address = new IPAddress(ipadr);
                }
                else {
                    IPHostEntry host = Dns.GetHostEntry(hostName);
                    IPAddress[] addressList = host.AddressList;
                    address = addressList[addressList.Length - 1];
                }
                IPEndPoint localEndPoint = new IPEndPoint(address, port);
                LogProxy.LogDebug("SocketListener.Start():尝试启动服务器 " + address + ":" + port);

                //创建一个监听Socket
                this._listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                //this._listenSocket.ReceiveBufferSize = this._bufferSize;
                //this._listenSocket.SendBufferSize = this._bufferSize;

                if (localEndPoint.AddressFamily == AddressFamily.InterNetworkV6) {
                    this._listenSocket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, false);
                    this._listenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, localEndPoint.Port));
                }
                else {
                    this._listenSocket.Bind(localEndPoint);
                }
                this._listenSocket.Listen(2); //最大挂起数
                this.StartAccept2();

                _isStarted = true; //服务器启动成功
            } catch (Exception e) {
                LogProxy.LogWarning($"SocketListener.Start():异常 {e}");
            }
        }

        #endregion Exposed Function

        /// <summary>
        /// 这个函数只绑定了acceptEventArg
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnIOCompleted(object sender, SocketAsyncEventArgs e)
        {
            //lock (_lockAccept)
            //{
            switch (e.LastOperation) {
                case SocketAsyncOperation.Accept:
                    //当关闭Socket的时候，如果没有注销事件，那么也会进入这里，所以作了一个判断(同时注销事件也完善了)
                    if (e.SocketError != SocketError.OperationAborted) {
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
                    LogProxy.LogWarning("SocketListener：OnIOCompleted(): 进入了未预料的switch分支！");
                    break;
            }
            //}
        }

        /// <summary>
        /// Accept的处理。
        /// </summary>
        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            try {
                Socket acceptSocket = e.AcceptSocket;
                if (acceptSocket.Connected) {
                    Peer peer = new Peer(); //创建一个用户
                    peer.peerSocket.SetAcceptSocket(acceptSocket);

                    if (EventAccept != null) //产生认证事件
                    {
                        EventAccept(peer);
                    }
                }

                // 继续开始异步接收
                this.StartAccept(e);
            } catch (SocketException ex) {
                //Token token = e.UserToken as Token;
                LogProxy.LogWarning(String.Format("SocketListener.ProcessAccept()：Socket异常,接收认证连接出现错误 {0}", ex.Message));
            } catch (Exception ex) {
                LogProxy.LogWarning("SocketListener.ProcessAccept()：异常" + ex.Message);
            }
        }

        #region BuiltIn Function

        /// <summary>
        /// 开始接受客户端的Accept
        /// </summary>
        private void StartAccept(SocketAsyncEventArgs eArg)
        {
            try {
                if (eArg == null) {
                    eArg = new SocketAsyncEventArgs();
                    eArg.Completed += new EventHandler<SocketAsyncEventArgs>(OnIOCompleted);
                }

                eArg.AcceptSocket = null; // 必须要先清掉Socket

                LogProxy.LogDebug("ServerListenerSocket.StartAccept():服务器开始接收认证!");
                //开始异步接收认证
                if (!_listenSocket.AcceptAsync(eArg)) {
                    this.ProcessAccept(eArg);
                }
            } catch (Exception e) {
                LogProxy.LogWarning("ServerListenerSocket.StartAccept():异常：" + e.Message);
                // throw;
            }
        }

        /// <summary>
        /// 开始接受客户端的Accept,使用一组SocketAsyncEventArgs
        /// </summary>
        private void StartAccept2()
        {
            try {
                if (_acceptEventArgs == null) {
                    _acceptEventArgs = new SocketAsyncEventArgs[2];
                    for (int i = 0; i < _acceptEventArgs.Length; i++) {
                        _acceptEventArgs[i] = new SocketAsyncEventArgs();
                        _acceptEventArgs[i].Completed += new EventHandler<SocketAsyncEventArgs>(OnIOCompleted);
                    }
                }
                for (int i = 0; i < _acceptEventArgs.Length; i++) {
                    _acceptEventArgs[i].AcceptSocket = null; // 必须要先清掉Socket
                }

                LogProxy.LogDebug("ServerListenerSocket.StartAccept2():服务器开始接收认证!");
                for (int i = 0; i < _acceptEventArgs.Length; i++) {
                    //开始异步接收认证
                    if (!_listenSocket.AcceptAsync(_acceptEventArgs[i])) {
                        this.ProcessAccept(_acceptEventArgs[i]);
                    }
                }
            } catch (Exception e) {
                LogProxy.LogWarning("ServerListenerSocket.StartAccept2():异常：" + e.Message);
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
            LogProxy.LogWarning("SocketListener.Dispose()：进入了Dispose!");
            if (disposed) {
                return;
            }
            disposed = true;

            if (disposing) {
                _isStarted = false;

                // 清理托管资源
                EventAccept = null;
            }
            // 清理非托管资源
            try {
                if (_acceptEventArg != null) {
                    //_acceptEventArg.Completed -= new EventHandler<SocketAsyncEventArgs>(OnIOCompleted);
                    _acceptEventArg.Dispose();
                    _acceptEventArg = null;
                }
                if (_acceptEventArgs != null) {
                    for (int i = 0; i < _acceptEventArgs.Length; i++) {
                        //_acceptEventArgs[i].Completed -= new EventHandler<SocketAsyncEventArgs>(OnIOCompleted);
                        _acceptEventArgs[i].Dispose();
                    }
                    _acceptEventArgs = null;
                }

                _listenSocket.Close();
                LogProxy.LogWarning("SocketListener.Dispose()：关闭了服务器Socket");
                LogProxy.LogWarning("SocketListener.Dispose()：删除所有用户");
                PeerManager.Inst.DeleteAllPeer(); //关闭所有Token
            } catch (Exception e) {
                LogProxy.LogWarning("SocketListener.Dispose()：异常：" + e.Message);
            }
        }

        #endregion IDisposable implementation
    }
}
