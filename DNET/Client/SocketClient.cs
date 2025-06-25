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
    /// 关联SocketAsyncEventArgs的上下文信息
    /// </summary>
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
        /// <summary>
        /// 构造函数： 创建出一个Socket对象
        /// </summary>
        internal SocketClient()
        {
            _packet = new SimplePacket();
            _areConnectDone = new AutoResetEvent(false);
        }

        /// <summary>
        /// 接收buffer大小
        /// </summary>
        private const int RECE_BUFFER_SIZE = 16 * 1024; //16k


        /// <summary>
        /// 主机网络端点,也就是连接目标
        /// </summary>
        private EndPoint _hostEndPoint;

        /// <summary>
        /// 信号量，通知等待的线程已经发生了事件.这个是用来确保连接先成功后再开始发送数据
        /// </summary>
        private AutoResetEvent _areConnectDone = null;

        private SocketAsyncEventArgs _sendArgs = null;

        private SocketAsyncEventArgs _receiveArgs = null;

        /// <summary>
        /// 带数据管理的新打包器
        /// </summary>
        private IPacket3 _packet;

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


        /// <summary>
        /// 是否已经连接上了服务器
        /// </summary>
        internal bool IsConnected => socket != null && socket.Connected;

        /// <summary>
        /// 套接字用于发送/接收消息。
        /// </summary>
        public Socket socket { get; private set; }

        /// <summary>
        /// 等待发送消息队列长度
        /// </summary>
        public int WaitSendMsgCount => _sendQueue.Count;

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
        /// 使用数据打包
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="format"></param>
        /// <param name="txrId"></param>
        /// <param name="eventType"></param>
        internal void AddSendData(byte[] data, int offset, int count,
            Format format = Format.Raw,
            uint txrId = 0,
            int eventType = 0)
        {
            if (data == null) {
                LogProxy.LogWarning("SocketClient.AddSendData(data,offset,count):要发送的数据为null！");
            }
            try {
                ByteBuffer packedData = _packet.Pack(data, offset, count, format, txrId, eventType);
                _sendQueue.Enqueue(packedData);
            } catch (Exception e) {
                LogProxy.LogWarning("SocketClient.AddSendData(p1,p2,p3):异常 " + e.Message);
            }
        }

        /// <summary>
        /// 从接收队列中提取所有数据包
        /// </summary>
        /// <returns></returns>
        internal List<Message> GetReceiveMessages()
        {
            List<Message> messages = new List<Message>();
            while (_receQueue.TryDequeue(out Message msg)) {
                messages.Add(msg);
            }
            return messages;
        }

        /// <summary>
        /// 重新记录一个远程地址,在Connect之前调用.
        /// </summary>
        /// <param name="hostName">服务器主机</param>
        /// <param name="port">服务器端口</param>
        internal void BindRemote(string hostName, int port)
        {
            try {
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
                    IPHostEntry host = Dns.GetHostEntry(hostName);
                    IPAddress[] addressList = host.AddressList;

                    // 重新记录连接目标
                    this._hostEndPoint = new IPEndPoint(addressList[addressList.Length - 1], port);
                }

                socket.Close();
                this.socket = new Socket(this._hostEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                //设置这个Timeout应该是无效的(是有效的，必须设置为0，否则自动断线)
                socket.SendTimeout = 8 * 1000;
                socket.ReceiveTimeout = 0;
            } catch (Exception e) {
                LogProxy.LogWarning("SocketClient.BindRemote():绑定远程地址地址错误: " + e.Message);
            }
        }

        /// <summary>
        /// 连接到服务器,目前这个连接函数是阻塞的.不过是由Client工作线程执行,所以无所谓.
        /// </summary>
        internal void Connect()
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

            if (_receiveArgs == null) {
                _receiveArgs = new SocketAsyncEventArgs();
                ConnectionContext context = new ConnectionContext {
                    socket = this.socket,
                    sendBuffer = null,
                    recvBuffer = new byte[RECE_BUFFER_SIZE]
                };
                _receiveArgs.UserToken = context;
                _receiveArgs.SetBuffer(context.recvBuffer, 0, context.recvBuffer.Length);
                _receiveArgs.Completed += OnReceiveCompleted;
            }

            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs(); //创建一个SocketAsyncEventArgs类型
            connectArgs.UserToken = new ConnectionContext {
                socket = this.socket,
                sendBuffer = null,
                recvBuffer = null,
            };
            connectArgs.RemoteEndPoint = this._hostEndPoint;
            connectArgs.Completed += OnConnectCompleted; //加一个OnConnect来通知这个线程已经完成了

            if (!socket.ConnectAsync(connectArgs)) {
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
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Disconnect(false); //不允许重用套接字

                    // 这个在Connect的时候重新new
                    _sendArgs.Dispose();
                    _sendArgs.UserToken = null;
                    _sendArgs = null;
                    _receiveArgs.Dispose();
                    _receiveArgs.UserToken = null;
                    _receiveArgs = null;
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
                // 尝试获取要发送的,这里应该对所有的数据进行整合然后一次性发送
                if (_sendQueue.Count == 0) {
                    return false;
                }

                // 一次同时只能发送一个数据包
                if (!TryBeginSend()) {
                    return false;
                }

                // 注意这里一定要全部整合一次性发送.
                int totalLength = 0;
                List<ByteBuffer> buffers = new List<ByteBuffer>();
                while (_sendQueue.TryDequeue(out var sendBuff)) {
                    buffers.Add(sendBuff);

                    // 统计总长度
                    totalLength += sendBuff.Length;
                    if (totalLength > 12 * 1024) {
                        // 最大的buffer是16K,这里差不多12K就停下来算了
                        break;
                    }
                }
                if (buffers.Count == 0) {
                    return false;
                }

                var sendBuffer = GlobalBuffer.Inst.Get(totalLength);
                for (int i = 0; i < buffers.Count; i++) {
                    sendBuffer.Append(buffers[i]);
                    buffers[i].Recycle();
                }

                ConnectionContext context = _sendArgs.UserToken as ConnectionContext;
                context.sendBuffer = sendBuffer;
                _sendArgs.SetBuffer(context.sendBuffer.buffer, 0, context.sendBuffer.Length);

                PrepareSend(socket, _sendArgs);

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
            while (_sendQueue.TryDequeue(out var buff)) {
                buff.Recycle();
            }
            while (_receQueue.TryDequeue(out var msg)) {
            }
            _packet.Clear();

            EndSend();
        }

        #endregion Exposed Function

        #region Callback

        private void OnConnectCompleted(object sender, SocketAsyncEventArgs args)
        {
            try {
                _areConnectDone.Set(); // 通知等待线程连接已完成

                if (args.SocketError == SocketError.Success) {
                    if (IsConnected) {
                        PrepareReceive(); // 启动接收
                    }
                    else {
                        LogProxy.LogWarning("OnConnectCompleted: IsConnected=false, 但 SocketError.Success");
                    }

                    EventConnectCompleted?.Invoke();
                }
                else {
                    LogProxy.LogWarning($"连接失败: SocketError={args.SocketError}");
                    // 你可以触发一个连接失败的事件或重试逻辑
                }

                args.UserToken = null; // 清理资源
            } catch (Exception e) {
                LogProxy.LogWarning($"OnConnectCompleted: 异常 {e}");
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
                    var msgs = _packet.Unpack(args.Buffer, args.Offset, args.BytesTransferred);
                    if (msgs != null) {
                        msgs.ForEach(msg => { _receQueue.Enqueue(msg); });
                    }
                    int msgCount = msgs == null ? 0 : msgs.Count;

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
                if (!socket.Connected) //如果当前没有连接上，就不接收了
                {
                    return;
                }

                // _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);

                //开始接收
                if (!socket.ReceiveAsync(_receiveArgs)) {
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
                    _sendArgs = null;
                }
                if (this._receiveArgs != null) {
                    _sendArgs.UserToken = null;
                    _receiveArgs.Dispose();
                    _receiveArgs = null;
                }
                if (this.socket != null) {
                    this.socket.Close();
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
