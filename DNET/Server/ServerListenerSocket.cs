using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace DNET
{
    /// <summary>
    /// 服务器监听Socket封装
    /// </summary>
    internal class ServerListenerSocket : IDisposable
    {
        /// <summary>
        /// 用于监听的套接字的连接请求。
        /// </summary>
        private Socket _listenSocket;

        /// <summary>
        /// 监听已经启动成功标志
        /// </summary>
        private bool _isStarted;

        // private SocketAsyncEventArgs _acceptSocketArg;

        /// <summary>
        /// 专门给Accept用的,改为一组，在代码里设置为使用2个
        /// </summary>
        private SocketAsyncEventArgs[] _acceptSocketArgs;

        /// <summary>
        /// 标记是否已释放
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// 服务器启动成功标志
        /// </summary>
        public bool IsStarted => _isStarted;

        #region Event

        /// <summary>
        /// 接受认证完成
        /// </summary>
        internal event Action<Socket> EventAccept;

        #endregion

        /// <summary>
        /// 启动服务器
        /// </summary>
        /// <param name="hostName">服务器的ip</param>
        /// <param name="port">本机的服务器端口</param>
        /// <param name="backlog">监听的队列长度</param>
        internal void Start(string hostName, int port, int backlog = 64)
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
                if (LogProxy.Debug != null)
                    LogProxy.Debug("ServerListenerSocket.Start():尝试启动服务器 " + address + ":" + port);

                //创建一个监听Socket
                _listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                //_listenSocket.ReceiveBufferSize = _bufferSize;
                //_listenSocket.SendBufferSize = _bufferSize;

                if (localEndPoint.AddressFamily == AddressFamily.InterNetworkV6) {
                    _listenSocket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, false);
                    _listenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, localEndPoint.Port));
                }
                else {
                    _listenSocket.Bind(localEndPoint);
                }
                _listenSocket.Listen(backlog); //最大挂起数
                StartAccept2();

                _isStarted = true; //服务器启动成功
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"ServerListenerSocket.Start():异常 {e}");
            }
        }

        /// <summary>
        /// 这个函数只绑定了acceptEventArg
        /// </summary>
        /// <param name="sender">事件源</param>
        /// <param name="args">异步事件参数</param>
        private void OnIOCompleted(object sender, SocketAsyncEventArgs args)
        {
            if (_disposed) // 如果已经销毁了，那么就不处理了
                return;

            switch (args.LastOperation) {
                case SocketAsyncOperation.Accept:
                    // 当关闭Socket的时候，如果没有注销事件，那么也会进入这里，所以作了一个判断(同时注销事件也完善了)
                    if (args.SocketError != SocketError.OperationAborted) {
                        ProcessAccept(args);
                    }
                    break;

                /*  case SocketAsyncOperation.Receive:
                      ProcessReceive(this,e);
                      break;

                  case SocketAsyncOperation.Send:
                      ProcessSend(this,e);
                      break;*/
                default:
                    break;
            }
        }

        /// <summary>
        /// Accept的处理。
        /// </summary>
        /// <param name="args">Accept事件参数</param>
        private void ProcessAccept(SocketAsyncEventArgs args)
        {
            try {
                Socket acceptSocket = args.AcceptSocket;
                if (acceptSocket.Connected) {
                    // 产生认证事件,这是一个内部使用的,所以没有try catch
                    if (EventAccept != null) {
                        EventAccept(acceptSocket);
                    }
                }

                // 继续开始异步接收
                StartAccept(args);
            } catch (SocketException ex) {
                //Token token = e.UserToken as Token;
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"ServerListenerSocket.ProcessAccept():Socket异常,接收认证连接出现错误 {ex}");
            } catch (Exception ex) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"ServerListenerSocket.ProcessAccept():异常 {ex}");
            }
        }

        /// <summary>
        /// 单个的开始接受客户端的Accept
        /// </summary>
        /// <param name="args">Accept事件参数</param>
        private void StartAccept(SocketAsyncEventArgs args)
        {
            try {
                if (args == null) {
                    // 这里是不太可能进入的吧?
                    args = new SocketAsyncEventArgs();
                    args.Completed += OnIOCompleted;
                }

                args.AcceptSocket = null; // 必须要先清掉Socket

                // LogProxy.LogDebug("ServerListenerSocket.StartAccept():服务器继续开始接收认证!");
                //开始异步接收认证
                if (!_listenSocket.AcceptAsync(args)) {
                    ProcessAccept(args);
                }
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("ServerListenerSocket.StartAccept():异常：" + e.Message);
            }
        }

        /// <summary>
        /// 开始接受客户端的Accept,使用一组SocketAsyncEventArgs
        /// </summary>
        private void StartAccept2()
        {
            try {
                if (_acceptSocketArgs == null) {
                    _acceptSocketArgs = new SocketAsyncEventArgs[2];
                }
                for (int i = 0; i < _acceptSocketArgs.Length; i++) {
                    if (_acceptSocketArgs[i] == null) {
                        _acceptSocketArgs[i] = new SocketAsyncEventArgs();
                        _acceptSocketArgs[i].Completed += OnIOCompleted;
                    }
                    _acceptSocketArgs[i].AcceptSocket = null; // 必须要先清掉Socket
                }
                if (LogProxy.Debug != null)
                    LogProxy.Debug("ServerListenerSocket.StartAccept2():服务器开始接收认证!");
                for (int i = 0; i < _acceptSocketArgs.Length; i++) {
                    //开始异步接收认证
                    if (!_listenSocket.AcceptAsync(_acceptSocketArgs[i])) {
                        ProcessAccept(_acceptSocketArgs[i]);
                    }
                }
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("ServerListenerSocket.StartAccept2():异常：" + e.Message);
                throw;
            }
        }

        /// <summary>
        /// 释放监听资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (LogProxy.Info != null)
                LogProxy.Info("ServerListenerSocket.Dispose():进入了Dispose!");

            _isStarted = false;
            EventAccept = null;

            try {
                // TODO: _listenSocket 可能为 null，需确认是否需要判空保护
                _listenSocket.Close();

                // if (_acceptSocketArg != null) {
                //     _acceptSocketArg.Completed -= OnIOCompleted;
                //     _acceptSocketArg.Dispose();
                // }
                if (_acceptSocketArgs != null) {
                    for (int i = 0; i < _acceptSocketArgs.Length; i++) {
                        // TODO: _acceptSocketArgs[i] 可能为 null，需确认释放流程
                        _acceptSocketArgs[i].Completed -= OnIOCompleted;
                        _acceptSocketArgs[i].Dispose();
                    }
                }

                if (LogProxy.Info != null)
                    LogProxy.Info("ServerListenerSocket.Dispose():关闭了服务器Socket");
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("ServerListenerSocket.Dispose():异常：" + e.Message);
            }
        }
    }
}
