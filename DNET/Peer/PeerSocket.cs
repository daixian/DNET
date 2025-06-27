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
    /// <summary>
    /// 客户机实现套接字的连接逻辑。
    /// </summary>
    public class PeerSocket : IDisposable
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public PeerSocket()
        {
            _packet = new SimplePacket();
            _areConnectDone = new AutoResetEvent(false);
        }

        /// <summary>
        /// 接收buffer大小
        /// </summary>
        private const int RECE_BUFFER_SIZE = 12 * 1024; //16k

        /// <summary>
        /// 主机网络端点,也就是连接目标
        /// </summary>
        private EndPoint _remoteEndPoint;

        /// <summary>
        /// 信号量，通知等待的线程已经发生了事件.这个是用来确保连接先成功后再开始发送数据
        /// </summary>
        private AutoResetEvent _areConnectDone = null;

        /// <summary>
        /// 发送数据时使用的SocketAsyncEventArgs
        /// </summary>
        private SocketAsyncEventArgs _sendArgs = null;

        /// <summary>
        /// 这个接收始终是异步自动开启的.不需要主动调用
        /// </summary>
        private SocketAsyncEventArgs _receiveArgs = null;

        /// <summary>
        /// 带数据管理的新打包器
        /// </summary>
        private IPacket3 _packet;

        /// <summary>
        /// 缓存一次发送失败的数据,要重发
        /// </summary>
        private ByteBuffer _sendFailData = null;

        /// <summary>
        /// 待发送数据队列
        /// </summary>
        ConcurrentQueue<ByteBuffer> _sendQueue = new ConcurrentQueue<ByteBuffer>();

        /// <summary>
        /// 待提取的接收数据队列
        /// </summary>
        ConcurrentQueue<Message> _receQueue = new ConcurrentQueue<Message>();

        /// <summary>
        /// 是否正在发送数据,同时只能发送一个数据
        /// </summary>
        private int _isSending = 0;

        /// <summary>
        /// 调试用的名字
        /// </summary>
        public string Name { get; set; } = "unnamed";

        /// <summary>
        /// 是否已经连接上了服务器
        /// </summary>
        internal bool IsConnected => socket != null && socket.Connected;

        /// <summary>
        /// 套接字用于发送/接收消息。如果是服务器端可以设置一个AcceptSocket进来
        /// </summary>
        public Socket socket { get; private set; }

        /// <summary>
        /// 等待发送消息队列长度
        /// </summary>
        public int WaitSendMsgCount => _sendQueue.Count;

        /// <summary>
        /// 状态统计
        /// </summary>
        public PeerStatus peerStatus { get; private set; } = new PeerStatus();

        /// <summary>
        /// Remote的IP
        /// </summary>
        public string RemoteIP {
            get {
                try {
                    IPEndPoint ipEndPoint = (IPEndPoint)_remoteEndPoint;
                    return ipEndPoint.Address.ToString();
                } catch (Exception) {
                    return "Unknow";
                }
            }
        }

        #region Event

        /// <summary>
        /// 出现错误
        /// </summary>
        internal event Action<ErrorType> EventError;

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
        /// 使用数据打包,然后添加到发送队列.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="format"></param>
        /// <param name="txrId"></param>
        /// <param name="eventType"></param>
        public void AddSendData(byte[] data, int offset, int count,
            Format format = Format.Raw, int txrId = 0, int eventType = 0)
        {
            if (data == null) {
                // LogProxy.LogWarning("PeerSocket.AddSendData(data,offset,count):要发送的数据为null！");
            }
            try {
                ByteBuffer packedData = _packet.Pack(data, offset, count, format, txrId, eventType);
                _sendQueue.Enqueue(packedData);
            } catch (Exception e) {
                LogProxy.LogWarning($"PeerSocket.AddSendData:[{Name}] 异常 {e}");
            }
        }

        /// <summary>
        /// 从接收队列中提取所有数据包
        /// </summary>
        /// <returns>如果没有那么返回null</returns>
        public List<Message> GetReceiveMessages()
        {
            if (_receQueue.IsEmpty)
                return null;

            List<Message> messages = new List<Message>();
            while (_receQueue.TryDequeue(out Message msg)) {
                messages.Add(msg);
            }
            return messages;
        }

        /// <summary>
        /// 服务器端连接一个客户端,设置acceptSocket进来
        /// </summary>
        /// <param name="acceptSocket"></param>
        public void SetAcceptSocket(Socket acceptSocket)
        {
            socket?.Close();
            this.socket = acceptSocket;

            try {
                // 记录客户端远程终结点（IP+端口）
                this._remoteEndPoint = acceptSocket.RemoteEndPoint;

                // 这里可以根据需要设置超时等属性
                socket.SendTimeout = 8 * 1000;
                socket.ReceiveTimeout = 0;

                PrepareSocketAsyncEventArgs();
                peerStatus.Reset();

                if (IsConnected) {
                    PrepareReceive(_receiveArgs); // 启动接收
                }
                else {
                    LogProxy.LogError($"PeerSocket.SetAcceptSocket():[{Name}]  IsConnected=false,这不应该!");
                }
            } catch (Exception e) {
                LogProxy.LogWarning($"PeerSocket.SetAcceptSocket():[{Name}]  异常{e}");
            }
        }

        /// <summary>
        /// 重新记录一个远程地址,在Connect之前调用.
        /// </summary>
        /// <param name="hostName">服务器主机</param>
        /// <param name="port">服务器端口</param>
        public void BindRemote(string hostName, int port)
        {
            try {
                if (Regex.IsMatch(hostName, @"\d{1,3}[.]\d{1,3}[.]\d{1,3}[.]\d{1,3}")) {
                    byte[] ipadr = new byte[4];

                    MatchCollection ms = Regex.Matches(hostName, @"\d{1,3}");
                    for (int i = 0; i < ms.Count; i++) {
                        ipadr[i] = Convert.ToByte(hostName.Substring(ms[i].Index, ms[i].Length));
                    }

                    IPAddress address = new IPAddress(ipadr);
                    this._remoteEndPoint = new IPEndPoint(address, port);
                }
                else {
                    IPHostEntry host = Dns.GetHostEntry(hostName);
                    IPAddress[] addressList = host.AddressList;

                    // 重新记录连接目标
                    this._remoteEndPoint = new IPEndPoint(addressList[addressList.Length - 1], port);
                }

                socket?.Close();
                this.socket = new Socket(this._remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                //设置这个Timeout应该是无效的(是有效的，必须设置为0，否则自动断线)
                socket.SendTimeout = 8 * 1000;
                socket.ReceiveTimeout = 0;
            } catch (Exception e) {
                LogProxy.LogWarning($"PeerSocket.BindRemote():[{Name}] 绑定远程地址地址错误: " + e.Message);
            }
        }

        /// <summary>
        /// 连接到服务器,目前这个连接函数是阻塞的.不过是由Client工作线程执行,所以无所谓.
        /// </summary>
        public void Connect()
        {
            PrepareSocketAsyncEventArgs();

            peerStatus.Reset();

            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs(); //创建一个SocketAsyncEventArgs类型
            connectArgs.UserToken = new ConnectionContext {
                socket = this.socket,
                sendBuffer = null,
                recvBuffer = null,
            };
            connectArgs.RemoteEndPoint = this._remoteEndPoint;
            connectArgs.Completed += OnConnectCompleted; //加一个OnConnect来通知这个线程已经完成了

            if (!socket.ConnectAsync(connectArgs)) {
                OnConnectCompleted(this, connectArgs);
            }

            // 这里阻塞吧.
            _areConnectDone.WaitOne();

            // dx: 注意这里这样发出异常
            SocketError errorCode = connectArgs.SocketError;
            if (errorCode != SocketError.Success) {
                throw new SocketException((int)errorCode);
            }

            // 自动开始一次发送吧,在等待连接切换线程的这段时间可能外面已经有添加发送队列进来了.
            TryBeginSend();
        }

        /// <summary>
        /// 断开服务器的连接
        /// </summary>
        public void Disconnect()
        {
            try {
                if (IsConnected) {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Disconnect(false); //不允许重用套接字
                    Clear(); //清空数据
                }
            } catch (Exception e) {
                LogProxy.LogWarning($"PeerSocket.Disconnect():[{Name}] 异常 " + e.Message);
            }
        }

        /// <summary>
        /// 从队列中取出数据包发送,可以每帧不停的驱动
        /// </summary>
        /// <returns></returns>
        /// <exception cref="SocketException"></exception>
        public bool TryBeginSend()
        {
            if (IsConnected) {
                // 尝试获取要发送的,这里应该对所有的数据进行整合然后一次性发送
                if (_sendQueue.Count == 0) {
                    return false;
                }

                // dx: 一次同时只能发送一个数据包,这里是判断是否正在发送中
                if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0) {
                    // 已经有发送在进行了，当前线程不执行
                    return false;
                }

                // 注意这里一定要全部整合一次性发送.
                int totalLength = 0;
                List<ByteBuffer> buffers = new List<ByteBuffer>();
                if (_sendFailData != null) {
                    LogProxy.LogWarning($"SocketClient.TryBeginSend():[{Name}] 有一个发送失败的数据,接上重发");
                    buffers.Add(_sendFailData);
                    totalLength += _sendFailData.Length;
                    _sendFailData = null;
                }

                while (_sendQueue.TryDequeue(out ByteBuffer item)) {
                    buffers.Add(item);
                    if (item.Length == 0) {
                        LogProxy.LogError($"SocketClient.TryBeginSend():[{Name}] 发送item数据长度不应该为0!");
                    }
                    // 统计总长度
                    totalLength += item.Length;
                    if (totalLength > 12 * 1024) {
                        // 最大的buffer是16K,这里差不多12K就停下来算了
                        break;
                    }
                }

                // 没有要发送的数据,那么直接返回吧
                if (buffers.Count == 0) {
                    return false;
                }

                // 如果只有一个数据,那么就直接用这个数据
                ByteBuffer sendBuffer = buffers[0];
                if (buffers.Count > 1) {
                    // 如果有多个数据，那么就合并成一个数据
                    sendBuffer = GlobalBuffer.Inst.Get(totalLength);
                    for (int i = 0; i < buffers.Count; i++) {
                        sendBuffer.Append(buffers[i]);
                        buffers[i].Recycle();
                    }
                }

                if (sendBuffer.Length == 0) {
                    LogProxy.LogError($"SocketClient.TryBeginSend():[{Name}] 发送数据长度不应该为0!");
                }

                ConnectionContext context = _sendArgs.GetConnectionContext();
                context.sendBuffer = sendBuffer;
                context.curSendMsgCount = buffers.Count;
                // dx: 注意这里是发送的实际数据长度
                _sendArgs.SetBuffer(context.sendBuffer.buffer, 0, context.sendBuffer.Length);

                PrepareSend(socket, _sendArgs);

                return true;
            }
            else {
                LogProxy.LogError($"SocketClient.TryBeginSend():[{Name}] 还未连接,不能发送数据!");
            }
            return false;
        }

        /// <summary>
        /// 清空当前所有的队列和数据结构
        /// </summary>
        public void Clear()
        {
            while (_sendQueue.TryDequeue(out var buff)) {
                buff.Recycle();
            }
            while (_receQueue.TryDequeue(out var msg)) {
            }
            _packet.Clear();

            // 标记发送结束
            Interlocked.Exchange(ref _isSending, 0);
        }

        #endregion Exposed Function

        #region Callback

        private void OnConnectCompleted(object sender, SocketAsyncEventArgs args)
        {
            // 这个回调由线程池进来,有时候已经释放了,这里仍然会进入.
            if (_disposed) return;

            try {
                _areConnectDone.Set(); // 通知等待线程连接已完成

                if (args.SocketError == SocketError.Success) {
                    EventConnectCompleted?.Invoke();

                    if (IsConnected) {
                        PrepareReceive(_receiveArgs); // 启动接收
                    }
                    else {
                        LogProxy.LogWarning($"PeerSocket.OnConnectCompleted():[{Name}] IsConnected=false, 但 SocketError.Success");
                    }
                }
                else {
                    LogProxy.LogWarning($"PeerSocket.OnConnectCompleted():[{Name}] 连接失败: SocketError={args.SocketError}");
                    // 你可以触发一个连接失败的事件或重试逻辑
                }
            } catch (Exception e) {
                LogProxy.LogWarning($"PeerSocket.OnConnectCompleted():[{Name}] 异常 {e}");
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs args)
        {
            // 这个回调由线程池进来,有时候已经释放了,这里仍然会进入.
            if (_disposed) return;

            try {
                if (args.SocketError != SocketError.Success) {
                    this.ProcessError(args);
                }
                ConnectionContext context = args.GetConnectionContext();

                if (args.BytesTransferred == 0) {
                    // 在本机回环飞速的发送的时候,有时候会进入这里,导致丢失一个包,似乎不会出现,观察一下.这个错误实际是由Buffer池导致的.后面似乎可以去掉这个
                    LogProxy.LogWarning($"SocketClient.OnSendCompleted():[{Name}] 发送状态成功 但是发送数据字节数 {args.BytesTransferred}");

                    // 缓存最后发送失败数据
                    _sendFailData = context.sendBuffer;
                }
                else {
                    // 回收发送缓冲区
                    context.sendBuffer.Recycle();
                    context.sendBuffer = null;

                    if (Config.isDebugLog)
                        LogProxy.LogDebug($"SocketClient.OnSendCompleted():[{Name}] 发送成功 发送数据字节数 {args.BytesTransferred}");
                    // 这是多条消息合并发送的,所以这里要记录
                    peerStatus.RecordSentMessage(context.curSendMsgCount, args.BytesTransferred);

                    //执行事件
                    if (EventSendCompleted != null) {
                        EventSendCompleted();
                    }
                }

                // 标记发送结束
                Interlocked.Exchange(ref _isSending, 0);

                // 继续发送下一条数据
                if (_sendQueue.Count > 0 || _sendFailData != null)
                    TryBeginSend();
            } catch (Exception e) {
                LogProxy.LogWarning($"PeerSocket.OnSendCompleted():[{Name}]  {e}");
            }
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs args)
        {
            // 这个回调由线程池进来,有时候已经释放了,这里仍然会进入.
            if (_disposed) return;

            try {
                // 不能在这里PrepareReceive().它会递归调用OnReceiveCompleted()

                if (args.SocketError != SocketError.Success) {
                    this.ProcessError(args);
                }
                //有可能会出现接收到的数据长度为0的情形，如当服务器关闭连接的时候
                if (args.BytesTransferred == 0) {
                    // 这个应该是看不同平台,可能不同
                    LogProxy.LogWarning($"PeerSocket.OnReceiveCompleted():[{Name}] BytesTransferred函数返回了零，说明远程可能已经关闭了连接 ");
                    this.ProcessError(args);
                }

                // 确定接收成功之后
                var curRecvBuffer = args.GetConnectionContext().recvBuffer; // 这是当前的接收缓冲区
                byte[] bytes = args.Buffer;
                int offset = args.Offset;
                int length = args.BytesTransferred;

                if (Config.isDebugLog)
                    LogProxy.LogDebug($"SocketClient.OnReceiveCompleted():[{Name}] 接收成功 接收数据字节数 {length}");

                // 写入当前接收的数据(这里等于说是由.net线程池的接收线程进行了解包)
                var msgs = _packet.Unpack(bytes, offset, length);
                if (msgs != null) {
                    msgs.ForEach(msg => { _receQueue.Enqueue(msg); });
                }
                int msgCount = msgs == null ? 0 : msgs.Count;

                curRecvBuffer.Recycle(); //解包结束,回收接收缓存区

                // 记录接收状态
                peerStatus.RecordReceivedMessage(msgCount, length);

                //如果确实收到了一条消息.执行事件
                if (msgCount > 0 && EventReceiveCompleted != null) {
                    EventReceiveCompleted();
                }
                PrepareReceive(args); //立刻开始下一个接收,免得解包速度过慢
            } catch (Exception e) {
                LogProxy.LogWarning($"PeerSocket.OnReceiveCompleted():[{Name}] 异常 {e}");
            }
        }

        #endregion Callback

        #region Private Function

        /// <summary>
        /// 初始化的时候准备SocketAsyncEventArgs
        /// </summary>
        private void PrepareSocketAsyncEventArgs()
        {
            if (_sendArgs == null) {
                _sendArgs = new SocketAsyncEventArgs();
                _sendArgs.UserToken = new ConnectionContext {
                    socket = this.socket,
                    sendBuffer = null,
                    recvBuffer = null,
                };
                _sendArgs.Completed += OnSendCompleted;
            }
            _sendArgs.GetConnectionContext().socket = this.socket; // 考虑到这个socket可能会改变

            if (_receiveArgs == null) {
                _receiveArgs = new SocketAsyncEventArgs();
                ConnectionContext context = new ConnectionContext {
                    socket = this.socket,
                    sendBuffer = null,
                    recvBuffer = GlobalBuffer.Inst.Get(RECE_BUFFER_SIZE),
                };
                _receiveArgs.UserToken = context;
                _receiveArgs.Completed += OnReceiveCompleted;
            }
            _receiveArgs.GetConnectionContext().socket = this.socket; // 考虑到这个socket可能会改变
        }


        /// <summary>
        /// 发生错误后就关闭连接
        /// </summary>
        /// <param name="args"></param>
        private void ProcessError(SocketAsyncEventArgs args)
        {
            LogProxy.LogWarning($"PeerSocket.ProcessError():[{Name}] 进入了ProcessError,SocketError={args.SocketError}"); //显示下接收的信息
            ConnectionContext context = args.GetConnectionContext(); //使用传递的Token
            if (context.socket.Connected) {
                try {
                    LogProxy.LogDebug($"PeerSocket.ProcessError():[{Name}] 调用Shutdown()关闭连接");
                    context.socket.Shutdown(SocketShutdown.Both);
                } catch (Exception ex) {
                    LogProxy.LogWarning($"PeerSocket.ProcessError():[{Name}] Shutdown()异常 {ex}");
                } finally {
                    context.socket.Close();
                }
            }
            //产生错误事件，这是一个很重要的事件，处理服务器连接断开等
            EventError?.Invoke(ErrorType.IOError);
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
                    LogProxy.LogWarning($"PeerSocket.PrepareSend():[{Name}] 当前已经断线，但仍尝试发送，已经忽略这条发送.");
                    return;
                }
                // 开始发送,这里作异常处理
                if (!s.SendAsync(args)) {
                    // 这个本机上立刻返回十分频繁
                    // LogProxy.Log("PeerSocket.PrepareSend(): SendAsync立刻返回了!");
                    OnSendCompleted(this, args); //如果立即返回
                }
            } catch (Exception e) {
                LogProxy.LogWarning($"PeerSocket.PrepareSend():[{Name}] 异常{e}");
                // 这里捕获过的异常有：
                // Thread creation failed.
            }
        }

        /// <summary>
        /// 开始一个接收
        /// </summary>
        private void PrepareReceive(SocketAsyncEventArgs args)
        {
            try {
                // 这里是需要的，否则在断线之后仍然可能不停的接收
                if (socket == null || !socket.Connected) //如果当前没有连接上，就不接收了
                {
                    return;
                }

                // 这里重新给一个接收buffer
                var context = args.GetConnectionContext();
                context.recvBuffer = GlobalBuffer.Inst.Get(RECE_BUFFER_SIZE);
                byte[] buff = context.recvBuffer.buffer; // dx: 注意这里是buffer的容量
                args.SetBuffer(buff, 0, buff.Length);

                //开始接收
                if (!socket.ReceiveAsync(args)) {
                    // 如果是同步完成,那么这里会进入递归
                    // 这个本机上立刻返回十分频繁
                    // LogProxy.Log("PeerSocket.PrepareReceive(): ReceiveAsync立刻返回了!");
                    OnReceiveCompleted(this, args);
                }
            } catch (Exception e) {
                LogProxy.LogWarning($"PeerSocket.PrepareReceive():[{Name}] 开始异步接收错误：" + e.Message);
                //这里捕获过的异常有：
                // Thread creation failed.
            }
        }

        #endregion BuiltIn Function

        #region IDisposable

        private bool _disposed = false;

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try {
                LogProxy.Log($"PeerSocket.Dispose():[{Name}] 进入Dispose");
                //快速的尝试掉线？
                //_clientSocket.SendTimeout = 500;
                //_clientSocket.ReceiveTimeout = 500;

                //断开连接,这里有SocketShutdown.Both
                Disconnect();

                EventConnectCompleted = null;
                EventReceiveCompleted = null;
                EventSendCompleted = null;
                EventError = null;

                if (this._sendArgs != null) {
                    // dx: 这里不要置空，因为可能此时还有异步回调没有进入
                    //_sendArgs.UserToken = null;
                    _sendArgs.Dispose();
                }
                if (this._receiveArgs != null) {
                    // dx: 这里不要置空，因为可能此时还有异步回调没有进入
                    //_receiveArgs.UserToken = null;
                    _receiveArgs.Dispose();
                }
                if (socket != null) {
                    socket.Dispose();
                }

                _areConnectDone?.Dispose();
            } catch (Exception e) {
                LogProxy.LogWarning("PeerSocket.Dispose() 异常：" + e.Message);
            } finally {
                socket = null;
                _areConnectDone = null;
                _sendArgs = null;
                _receiveArgs = null;
            }
        }

        #endregion
    }
}
