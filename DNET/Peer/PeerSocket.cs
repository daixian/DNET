using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;

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
        /// 接收buffer大小,定义在这里,让这个类可以单独使用
        /// </summary>
        private const int RECE_BUFFER_SIZE = 12 * 1024; //16k

        /// <summary>
        /// 主机网络端点,也就是连接目标
        /// </summary>
        private EndPoint _remoteEndPoint;

        /// <summary>
        /// 信号量，通知等待的线程已经发生了事件.这个是用来确保连接先成功后再开始发送数据
        /// </summary>
        private AutoResetEvent _areConnectDone;

        /// <summary>
        /// 连接的时候使用的SocketAsyncEventArgs
        /// </summary>
        private SocketAsyncEventArgs _connectArgs;

        /// <summary>
        /// 发送数据时使用的SocketAsyncEventArgs
        /// </summary>
        private SocketAsyncEventArgs _sendArgs;

        /// <summary>
        /// 这个接收始终是异步自动开启的.不需要主动调用
        /// </summary>
        private SocketAsyncEventArgs _receiveArgs;

        /// <summary>
        /// 带数据管理的新打包器
        /// </summary>
        private readonly IPacket3 _packet;

        /// <summary>
        /// 缓存一次发送失败的数据,要重发
        /// </summary>
        private ByteBuffer _sendFailData;

        /// <summary>
        /// 待发送数据队列
        /// </summary>
        readonly ConcurrentQueue<ByteBuffer> _sendQueue = new ConcurrentQueue<ByteBuffer>();

        /// <summary>
        /// 待提取的接收数据队列
        /// </summary>
        readonly ConcurrentQueue<Message> _receQueue = new ConcurrentQueue<Message>();

        /// <summary>
        /// 是否正在发送数据,同时只能发送一个数据
        /// </summary>
        private int _isSending;

        /// <summary>
        /// 是否正在接收数据,同时只能接收一个数据
        /// </summary>
        private int _isReceiving;

        /// <summary>
        /// 上次启动发送的时间戳
        /// </summary>
        private readonly Stopwatch _sendWatch = new Stopwatch();

        /// <summary>
        /// 一直递增的序号,在加入发送队列的时刻记录
        /// </summary>
        private int _sendMsgId = 0;

        /// <summary>
        /// 在从发送队列提取的时刻用来比对发送消息的序号
        /// </summary>
        private int _sendMsgId2 = 0;

        /// <summary>
        /// 用来对比接收的消息序号
        /// </summary>
        private int _receMsgId = 0;

        /// <summary>
        /// 它其实代表一个客户端,所以经常有ID的需求
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// 调试用的名字
        /// </summary>
        public string Name { get; set; } = "unnamed";

        /// <summary>
        /// 用户自定义的绑定对象，用于简单的绑定关联一个对象
        /// </summary>
        public object User { get; set; }

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
        /// 等待提取的消息队列长度
        /// </summary>
        public int WaitReceMsgCount => _receQueue.Count;

        /// <summary>
        /// 有等待提取的消息.
        /// </summary>
        public bool HasReceiveMsg => !_receQueue.IsEmpty;

        /// <summary>
        /// 状态统计
        /// </summary>
        public PeerStatus Status { get; } = new PeerStatus();

        /// <summary>
        /// 往返延迟统计(它包括服务器的CPU执行时间),在发送带事务的类型的消息的时候，会记录延迟
        /// </summary>
        public RttStatistics RttStatis { get; } = new RttStatistics();

        /// <summary>
        /// Remote的IP
        /// </summary>
        public string RemoteIP {
            get {
                try {
                    if (_remoteEndPoint == null)
                        return "null";
                    IPEndPoint ipEndPoint = (IPEndPoint)_remoteEndPoint;
                    return ipEndPoint.Address.ToString();
                } catch (Exception) {
                    return "Unknow";
                }
            }
        }

        #region 对外事件

        /// <summary>
        /// 出现错误
        /// </summary>
        internal event Action<PeerSocket, ErrorType> EventError;

        /// <summary>
        /// 连接成功的事件
        /// </summary>
        internal event Action<PeerSocket> EventConnectCompleted;

        /// <summary>
        /// 数据发送完毕
        /// </summary>
        internal event Action<PeerSocket> EventSendCompleted;

        /// <summary>
        /// 数据接收完毕
        /// </summary>
        internal event Action<PeerSocket> EventReceiveCompleted;

        #endregion

        #region 对外函数

        /// <summary>
        /// 使用数据打包,然后添加到发送队列.
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="count">数据长度</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        public void AddSendData(byte[] data, int offset, int count,
            Format format = Format.Raw, int txrId = 0, int eventType = 0)
        {
            if (data == null) {
                // LogProxy.LogWarning("PeerSocket.AddSendData(data,offset,count):要发送的数据为null！");
                offset = 0;
                count = 0;
            }
            try {
                ByteBuffer packedData = _packet.Pack(data, offset, count, format, txrId, eventType);
                packedData.Bytes.SetHeaderId(_sendMsgId++); //记录id

                _sendQueue.Enqueue(packedData);
                if (Config.EnableRttStatistics && txrId != 0) {
                    RttStatis.RecordSent(txrId); //记录发送
                }
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.AddSendData:[{Name}] 异常 {e}");
            }
        }

        /// <summary>
        /// 从接收队列中提取所有数据包,返回的结果是从ListPool中取的.处理完了之后可以送回ListPool.
        /// </summary>
        /// <returns>如果没有那么返回null</returns>
        /// <example>
        /// 使用示例：
        /// <code>
        /// // 使用完毕后将其归还
        /// ListPool&lt;Message&gt;.Shared.Recycle(msgs);
        /// </code>
        /// </example>
        public List<Message> GetReceiveMessages()
        {
            if (_receQueue.IsEmpty)
                return null;

            // 虽然队列是线程安全的,但是要保证消息的顺序性,所以这里仍然加锁
            lock (_receQueue) {
                List<Message> messages = ListPool<Message>.Shared.Get();
                while (_receQueue.TryDequeue(out Message msg)) {
                    messages.Add(msg);
                }

                // 由于及其微小的概率,queue.IsEmpty和锁不是同步,所以这里最终再次判断一下
                if (messages.Count == 0) {
                    ListPool<Message>.Shared.Recycle(messages);
                    messages = null;
                }
                return messages;
            }
        }

        /// <summary>
        /// 服务器端连接一个客户端,设置acceptSocket进来
        /// </summary>
        /// <param name="acceptSocket">已接受的Socket</param>
        public void SetAcceptSocket(Socket acceptSocket)
        {
            socket?.Close();
            socket = acceptSocket;

            try {
                // 记录客户端远程终结点（IP+端口）
                _remoteEndPoint = acceptSocket.RemoteEndPoint;

                // 这里可以根据需要设置超时等属性
                socket.SendTimeout = 8 * 1000;
                socket.ReceiveTimeout = 0;

                PrepareSocketAsyncEventArgs();
                Status.Reset();

                Clear(); // 这里也清理一下状态

                if (IsConnected) {
                    PrepareReceive(_receiveArgs); // 启动接收
                }
                else {
                    if (LogProxy.Error != null)
                        LogProxy.Error($"PeerSocket.SetAcceptSocket():[{Name}]  IsConnected=false,这不应该!");
                }
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.SetAcceptSocket():[{Name}]  异常{e}");
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
                    _remoteEndPoint = new IPEndPoint(address, port);
                }
                else {
                    IPHostEntry host = Dns.GetHostEntry(hostName);
                    IPAddress[] addressList = host.AddressList;

                    // 重新记录连接目标
                    _remoteEndPoint = new IPEndPoint(addressList[addressList.Length - 1], port);
                }

                socket?.Close();
                socket = new Socket(_remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                //设置这个Timeout应该是无效的(是有效的，必须设置为0，否则自动断线)
                socket.SendTimeout = 8 * 1000;
                socket.ReceiveTimeout = 0;

                Clear(); // 这里也清理一下状态
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.BindRemote():[{Name}] 绑定远程地址地址错误: " + e.Message);
            }
        }

        /// <summary>
        /// 连接到服务器,目前这个连接函数是阻塞的.不过是由Client工作线程执行,所以无所谓.
        /// </summary>
        public void Connect()
        {
            if (IsConnected || _disposed)
                return;

            PrepareSocketAsyncEventArgs();

            Status.Reset();
            Clear(); // 有可能是断线重连,这里干脆清空一下状态吧

            _connectArgs = new SocketAsyncEventArgs(); //创建一个SocketAsyncEventArgs类型
            _connectArgs.UserToken = new ConnectionContext {
                socket = socket,
                sendBuffer = null,
                receiveBuffer = null,
            };
            _connectArgs.RemoteEndPoint = _remoteEndPoint;
            _connectArgs.Completed += OnConnectCompleted; //加一个OnConnect来通知这个线程已经完成了

            if (!socket.ConnectAsync(_connectArgs)) {
                OnConnectCompleted(this, _connectArgs);
            }

            // 这里阻塞吧.
            // TODO: 避免无限等待，必要时可考虑增加超时机制
            _areConnectDone.WaitOne();
            if (_disposed) // 有可能线程在这里才恢复,就直接返回了.
                return;

            // dx: 注意这里这样发出异常
            SocketError errorCode = _connectArgs.SocketError;
            if (errorCode != SocketError.Success) {
                throw new SocketException((int)errorCode);
            }


            // 此时成功了,不用了
            _connectArgs.Dispose();
            _connectArgs = null;

            // 自动开始一次发送吧,在等待连接切换线程的这段时间可能外面已经有添加发送队列进来了.
            TryStartSend();
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
                }
                else {
                    if (_connectArgs != null) {
                        // 如果是正在连接了
                        // Socket.CancelConnectAsync(_connectArgs);
                        socket.Close();
                        _areConnectDone.Set(); //随便释放一下
                    }
                }

                Clear(); //清空数据
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.Disconnect():[{Name}] 异常 " + e.Message);
            }
        }

        /// <summary>
        /// 线程安全的从队列中取出数据包发送,可以每帧不停的驱动.
        /// 这个函数在超高并发的时候可能会漏发.
        /// 建议使用DNServer的TryStartSend()函数,会多一重保险.
        /// 但是本质上可能只有在Update()中不停的调用TryStartSend()来驱动会最为可靠.
        /// </summary>
        /// <returns>true表示确实启动了一个发送</returns>
        public bool TryStartSend()
        {
            if (_disposed)
                return false;

            if (!IsConnected) {
                if (LogProxy.Error != null)
                    LogProxy.Error($"SocketClient.TryStartSend():[{Name}] 还未连接,不能发送数据!");
                return false;
            }
            // dx: 这一段是测试时候测试的代码,或许以后可以扩展某种重发机制.但是如何确认发送失败,这也是一个问题.
            // if (Volatile.Read(ref _isSending) == 1 && _sendWatch.ElapsedMilliseconds > 2000) {
            //     LogProxy.LogError($"SocketClient.TryBeginSend():[{Name}] 检测到发送卡死，已超时2秒，重发?");
            //     PrepareSocketAsyncEventArgs();
            //     ConnectionContext ctx = _sendArgs.GetConnectionContext();
            //     // 缓存最后发送失败数据
            //     _sendFailData = ctx.sendBuffer;
            //     Interlocked.Exchange(ref _isSending, 0);// 标记发送结束
            //     return TryBeginSend();
            // }

            // dx: 一次同时只能发送一个数据包,这里是判断是否正在发送中
            if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0) {
                // 已经有发送在进行了，当前线程不执行
                return false;
            }

            // 尝试获取要发送的,这里应该对所有的数据进行整合然后一次性发送
            if (_sendQueue.Count == 0 && _sendFailData == null) {
                Interlocked.Exchange(ref _isSending, 0); // 标记发送结束
                return false; // 注意返回false前要释放锁
            }

            // 注意这里一定要全部整合一次性发送.
            int totalLength = 0;
            List<ByteBuffer> buffers = ListPool<ByteBuffer>.Shared.Get();
            if (_sendFailData != null) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"SocketClient.TryStartSend():[{Name}] 有一个发送失败的数据,接上重发");
                buffers.Add(_sendFailData);
                totalLength += _sendFailData.Length;
                _sendFailData = null;
            }

            while (_sendQueue.TryDequeue(out ByteBuffer item)) {
                int curID = item.Bytes.GetHeaderId();
                if (_sendMsgId2 != curID) {
                    if (LogProxy.Error != null)
                        LogProxy.Error($"SocketClient.TryStartSend():[{Name}] 提取消息的序号错误! {curID}/{_sendMsgId2}");
                }
                _sendMsgId2++;
                buffers.Add(item);
                if (item.Length == 0) {
                    // 这里是不应该进入的情况,打这个日志用来调试错误
                    if (LogProxy.Error != null)
                        LogProxy.Error($"SocketClient.TryStartSend():[{Name}] 发送item数据长度不应该为0!");
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
                Interlocked.Exchange(ref _isSending, 0); // 标记发送结束
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
                if (LogProxy.Error != null)
                    LogProxy.Error($"SocketClient.TryStartSend():[{Name}] 发送数据长度不应该为0!");
            }

            PrepareSocketAsyncEventArgs();
            ConnectionContext context = _sendArgs.GetConnectionContext();

            // 回收上一次发送缓冲区,专门留到这个地方才回收,防止某些地方空指针吧.
            context.sendBuffer?.Recycle();
            context.sendBuffer = sendBuffer;
            context.curSendMsgCount = buffers.Count; // 发送消息的个数
            // dx: 注意这里是发送的实际数据长度
            _sendArgs.SetBuffer(context.sendBuffer.Bytes, 0, context.sendBuffer.Length);

            ListPool<ByteBuffer>.Shared.Recycle(buffers); // 这是最后使用的地方了归还.

            if (PrepareSend(socket, _sendArgs)) {
                return true;
            }

            Interlocked.Exchange(ref _isSending, 0); // 这里确保是解除计数的
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
                msg.Recycle();
            }
            _packet.Clear();

            // 标记发送结束
            Interlocked.Exchange(ref _isSending, 0);

            RttStatis.Reset();

            _sendMsgId = 0;
            _sendMsgId2 = 0;
            _receMsgId = 0;
        }

        #endregion

        #region Socket Callback

        /// <summary>
        /// 连接完成回调
        /// </summary>
        /// <param name="sender">事件源</param>
        /// <param name="args">异步事件参数</param>
        private void OnConnectCompleted(object sender, SocketAsyncEventArgs args)
        {
            // 这个回调由线程池进来,有时候已经释放了,这里仍然会进入.
            if (_disposed) return;

            try {
                _areConnectDone.Set(); // 通知等待线程连接已完成(中断未完成的连接函数也会保证Set这一下)

                if (args.SocketError == SocketError.Success) {
                    EventConnectCompleted?.Invoke(this);

                    if (IsConnected) {
                        PrepareReceive(_receiveArgs); // 启动接收
                    }
                    else {
                        if (LogProxy.Warning != null)
                            LogProxy.Warning($"PeerSocket.OnConnectCompleted():[{Name}] IsConnected=false, 但 SocketError.Success");
                    }
                }
                else {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.OnConnectCompleted():[{Name}] 连接失败: SocketError={args.SocketError}");
                    // 可以触发一个连接失败的事件或重试逻辑
                }
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.OnConnectCompleted():[{Name}] 异常 {e}");
            }
        }

        /// <summary>
        /// 发送完成回调
        /// </summary>
        /// <param name="sender">事件源</param>
        /// <param name="args">异步事件参数</param>
        private void OnSendCompleted(object sender, SocketAsyncEventArgs args)
        {
            // 这个回调由线程池进来,有时候已经释放了,这里仍然会进入.
            if (_disposed) return;

            try {
                if (args.SocketError != SocketError.Success) {
                    ProcessError(args);
                }
                ConnectionContext context = args.GetConnectionContext();

                if (args.BytesTransferred == 0) {
                    // 在本机回环飞速的发送的时候,有时候会进入这里,导致丢失一个包,似乎不会出现,观察一下.这个错误实际是由Buffer池导致的.后面似乎可以去掉这个
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"SocketClient.OnSendCompleted():[{Name}] 发送状态成功 但是发送数据字节数 {args.BytesTransferred}");

                    // 缓存最后发送失败数据
                    _sendFailData = context.sendBuffer;
                }
                else {
                    if (Config.IsDebugMode && LogProxy.Debug != null)
                        LogProxy.Debug($"SocketClient.OnSendCompleted():[{Name}] 发送成功 发送数据字节数 {args.BytesTransferred}");
                    // 这是多条消息合并发送的,所以这里要记录
                    Status.RecordSentMessage(context.curSendMsgCount, args.BytesTransferred);

                    //执行事件
                    if (EventSendCompleted != null) {
                        EventSendCompleted(this);
                    }
                }

                // 标记发送结束
                Interlocked.Exchange(ref _isSending, 0);

                // 继续发送下一条数据
                if (_sendQueue.Count > 0 || _sendFailData != null)
                    TryStartSend();
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.OnSendCompleted():[{Name}] 异常 {e}");
            }
        }

        /// <summary>
        /// 接收完成回调
        /// </summary>
        /// <param name="sender">事件源</param>
        /// <param name="args">异步事件参数</param>
        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs args)
        {
            // 这个回调由线程池进来,有时候已经释放了,这里仍然会进入.
            if (_disposed) return;

            try {
                // 不能在这里PrepareReceive().它会递归调用OnReceiveCompleted()

                if (args.SocketError != SocketError.Success) {
                    ProcessError(args);
                }
                //有可能会出现接收到的数据长度为0的情形，如当服务器关闭连接的时候
                if (args.BytesTransferred == 0) {
                    // 这个应该是看不同平台,可能不同
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.OnReceiveCompleted():[{Name}] BytesTransferred函数返回了零，说明远程可能已经关闭了连接 ");
                    ProcessError(args);
                }

                // 确定接收成功之后
                // var curRecvBuffer = args.GetConnectionContext().recvBuffer; // 这是当前的接收缓冲区
                byte[] bytes = args.Buffer;
                int offset = args.Offset;
                int length = args.BytesTransferred;

                if (Config.IsDebugMode && LogProxy.Debug != null)
                    LogProxy.Debug($"PeerSocket.OnReceiveCompleted():[{Name}] 接收成功 接收数据字节数 {length}");

                // 写入当前接收的数据(这里等于说是由.net线程池的接收线程进行了解包)
                var msgList = _packet.Unpack(bytes, offset, length);
                if (msgList != null) {
                    msgList.ForEach(msg => {
                        _receQueue.Enqueue(msg);

                        // 检查接收到的消息ID,它应该是按顺序递增的
                        if (_receMsgId != msg.Id) {
                            // 这是真正确保每条消息都是按顺序发送接收成功了
                            if (LogProxy.Error != null)
                                LogProxy.Error($"PeerSocket.OnReceiveCompleted():[{Name}] 接收消息ID顺序错误: {msg.Id}/{_receMsgId}");
                        }
                        _receMsgId++; //它其实和Status记录接受消息条数是一样的

                        // 记录接收状态
                        Status.RecordReceivedMessage(1, msg.Length + Message.HeaderLength);

                        // 这里解析到了消息, 执行RTT统计
                        if (Config.EnableRttStatistics && msg.TxrId != 0) {
                            RttStatis.RecordReceived(msg.TxrId); //记录响应
                        }
                    });
                }
                int msgCount = msgList == null ? 0 : msgList.Count;
                msgList.Recycle(); // list列表可以回收
                // curRecvBuffer.Recycle(); //解包结束,回收接收缓存区

                //如果确实收到了一条消息.执行事件
                if (msgCount > 0 && EventReceiveCompleted != null) {
                    try {
                        EventReceiveCompleted(this);
                    } catch (Exception ex) {
                        if (LogProxy.Error != null)
                            LogProxy.Error($"PeerSocket.OnReceiveCompleted():[{Name}] 执行事件 EventReceiveCompleted 异常: {ex}");
                    }
                }

                Interlocked.Exchange(ref _isReceiving, 0); //标记接收完成

                PrepareReceive(args); //立刻开始下一个接收,免得解包速度过慢
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.OnReceiveCompleted():[{Name}] 异常 {e}");
            }
        }

        #endregion

        /// <summary>
        /// 初始化的时候准备SocketAsyncEventArgs
        /// </summary>
        private void PrepareSocketAsyncEventArgs()
        {
            if (_sendArgs == null) {
                if (LogProxy.Debug != null)
                    LogProxy.Debug($"PeerSocket.PrepareSocketAsyncEventArgs():[{Name}] sendArgs为null,构造新的args");
                _sendArgs = new SocketAsyncEventArgs();
                _sendArgs.Completed += OnSendCompleted;
            }
            if (_sendArgs.UserToken == null) {
                if (LogProxy.Debug != null)
                    LogProxy.Debug($"PeerSocket.PrepareSocketAsyncEventArgs():[{Name}] sendArgs.UserToken为null,构造新的UserToken");
                _sendArgs.UserToken = new ConnectionContext {
                    socket = socket,
                    sendBuffer = null,
                    receiveBuffer = null,
                };
            }
            _sendArgs.GetConnectionContext().socket = socket; // 考虑到这个socket可能会改变

            if (_receiveArgs == null) {
                if (LogProxy.Debug != null)
                    LogProxy.Debug($"PeerSocket.PrepareSocketAsyncEventArgs():[{Name}] receiveArgs为null,构造新的args");
                _receiveArgs = new SocketAsyncEventArgs();
                _receiveArgs.Completed += OnReceiveCompleted;
            }
            if (_receiveArgs.UserToken == null) {
                if (LogProxy.Debug != null)
                    LogProxy.Debug($"PeerSocket.PrepareSocketAsyncEventArgs():[{Name}] receiveArgs.UserToken为null,构造新的UserToken");
                _receiveArgs.UserToken = new ConnectionContext {
                    socket = socket,
                    sendBuffer = null,
                    receiveBuffer = null,
                };
            }
            _receiveArgs.GetConnectionContext().socket = socket; // 考虑到这个socket可能会改变
        }


        /// <summary>
        /// 发生错误后就关闭连接
        /// </summary>
        /// <param name="args">异步事件参数</param>
        private void ProcessError(SocketAsyncEventArgs args)
        {
            if (_disposed) return;

            if (LogProxy.Warning != null)
                LogProxy.Warning($"PeerSocket.ProcessError():[{Name}] 进入了ProcessError,SocketError={args.SocketError}"); //显示下接收的信息
            ConnectionContext context = args.GetConnectionContext(); //使用传递的Token
            if (context.socket.Connected) {
                try {
                    if (LogProxy.Debug != null)
                        LogProxy.Debug($"PeerSocket.ProcessError():[{Name}] 调用Shutdown()关闭连接");
                    context.socket.Shutdown(SocketShutdown.Both);
                } catch (Exception ex) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.ProcessError():[{Name}] Shutdown()异常 {ex}");
                } finally {
                    context.socket.Close();

                    // 上面Shutdown了,这里标记发送结束算了
                    if (_isSending != 0) {
                        if (LogProxy.Warning != null)
                            LogProxy.Warning($"PeerSocket.ProcessError():[{Name}] _isSending={_isSending}");

                        Interlocked.Exchange(ref _isSending, 0);
                    }
                }
            }
            //产生错误事件，这是一个很重要的事件，处理服务器连接断开等
            EventError?.Invoke(this, ErrorType.IOError);
        }

        /// <summary>
        /// 开始异步发送
        /// </summary>
        /// <param name="s">目标Socket</param>
        /// <param name="args">异步事件参数</param>
        private bool PrepareSend(Socket s, SocketAsyncEventArgs args)
        {
            if (_disposed) return false;

            try {
                //如果当前没有连接上，就不发送
                if (!s.Connected) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.PrepareSend():[{Name}] 当前已经断线，但仍尝试发送，已经忽略这条发送.");
                    return false;
                }
                _sendWatch.Restart(); //开始计时
                // 开始发送,这里作异常处理
                if (!s.SendAsync(args)) {
                    // 这个本机上立刻返回十分频繁
                    // LogProxy.Log("PeerSocket.PrepareSend(): SendAsync立刻返回了!");
                    OnSendCompleted(this, args); //如果立即返回
                }
                return true;
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.PrepareSend():[{Name}] 异常{e}");
                // 这里捕获过的异常有：
                // Thread creation failed.
            }
            return false;
        }

        /// <summary>
        /// 开始一个接收
        /// </summary>
        private void PrepareReceive(SocketAsyncEventArgs args)
        {
            if (_disposed) return;

            try {
                // 这里是需要的，否则在断线之后仍然可能不停的接收
                if (socket == null || !socket.Connected) //如果当前没有连接上，就不接收了
                {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.PrepareReceive():[{Name}] 当前已经断线，但仍尝试接收，已经忽略这条接收.");
                    return;
                }

                if (Interlocked.CompareExchange(ref _isReceiving, 1, 0) != 0) {
                    // 理论上应该不可能进入这里
                    if (LogProxy.Error != null)
                        LogProxy.Error($"PeerSocket.PrepareReceive():[{Name}] 正在接收中，请勿重复调用.原则上不应该进入这里");
                    return;
                }

                // 这里确保buffer存在吧,但是不要重复分配buffer了.没有必要.
                var context = args.GetConnectionContext();
                if (context.receiveBuffer == null) {
                    context.receiveBuffer = GlobalBuffer.Inst.Get(RECE_BUFFER_SIZE);
                }
                byte[] buff = context.receiveBuffer.Bytes; // dx: 注意这里是buffer的容量
                args.SetBuffer(buff, 0, buff.Length);
                //开始接收
                if (!socket.ReceiveAsync(args)) {
                    // 如果是同步完成,那么这里会进入递归
                    // 这个本机上立刻返回十分频繁
                    // LogProxy.Log("PeerSocket.PrepareReceive(): ReceiveAsync立刻返回了!");
                    OnReceiveCompleted(this, args);
                }
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.PrepareReceive():[{Name}] 开始异步接收错误：" + e.Message);
                //这里捕获过的异常有：
                // Thread creation failed.

                Interlocked.Exchange(ref _isReceiving, 0); // 标记接收结束
            }
        }

        #region IDisposable

        private bool _disposed;

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try {
                if (LogProxy.Info != null)
                    LogProxy.Info($"PeerSocket.Dispose():[{Name}][{RemoteIP}] 进入Dispose, 发送消息数{Status.SendMessageCount},接收消息数{Status.ReceiveMessageCount}");

                if (_isSending != 0) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.Dispose():[{Name}][{RemoteIP}] isSending={_isSending}");
                }
                // 最好不要有任何的残留数据
                if (_packet.UnpackCachedCount != 0) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.Dispose():[{Name}][{RemoteIP}] 中packet有未处理数据{_packet.UnpackCachedCount}字节");
                }
                if (WaitSendMsgCount != 0) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.Dispose():[{Name}][{RemoteIP}] 中待发送数据{WaitSendMsgCount}条");
                }
                if (WaitReceMsgCount != 0) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.Dispose():[{Name}][{RemoteIP}] 中待接收数据{WaitReceMsgCount}条");
                }
                //快速的尝试掉线？
                //_clientSocket.SendTimeout = 500;
                //_clientSocket.ReceiveTimeout = 500;

                //断开连接,这里有SocketShutdown.Both
                Disconnect();

                EventConnectCompleted = null;
                EventReceiveCompleted = null;
                EventSendCompleted = null;
                EventError = null;

                if (_sendArgs != null) {
                    // dx: 这里不要置空，因为可能此时还有异步回调没有进入
                    //_sendArgs.UserToken = null;
                    _sendArgs.Dispose();
                }
                if (_receiveArgs != null) {
                    _receiveArgs.Dispose();
                }
                if (socket != null) {
                    socket.Dispose();
                }

                _areConnectDone?.Dispose();
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning("PeerSocket.Dispose() 异常：" + e.Message);
            } finally {
                socket = null;
                User = null;
                _areConnectDone = null;
                _sendArgs = null;
                _receiveArgs = null;
            }
        }

        #endregion
    }
}
