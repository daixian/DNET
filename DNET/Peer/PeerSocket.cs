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
            _continueSendPumpCallback = ContinueSendPump;
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
        /// 待发送数据队列
        /// </summary>
        private readonly ConcurrentQueue<ByteBuffer> _sendQueue = new ConcurrentQueue<ByteBuffer>();

        /// <summary>
        /// 待提取的接收数据队列
        /// </summary>
        private readonly ConcurrentQueue<Message> _receQueue = new ConcurrentQueue<Message>();

        /// <summary>
        /// 是否正在发送数据,同时只能发送一个数据
        /// </summary>
        private int _isSending;

        /// <summary>
        /// 当前Peer尚未发送完成的总字节数，包含队列和正在发送的批次。
        /// </summary>
        private long _pendingSendBytes;

        /// <summary>
        /// 上一次输出发送背压警告的Stopwatch时间戳，用于避免过载时日志放大。
        /// </summary>
        private long _lastSendBackpressureLogTimestamp;

        /// <summary>
        /// 同步发送完成达到公平预算后，用于在线程池继续驱动发送泵的复用回调。
        /// </summary>
        private readonly WaitCallback _continueSendPumpCallback;

        /// <summary>
        /// 是否正在接收数据,同时只能接收一个数据
        /// </summary>
        private int _isReceiving;

        /// <summary>
        /// 标记当前连接是否已经发出过错误事件，避免收发回调同时失败时重复关闭和重复通知。
        /// </summary>
        private int _errorSignaled;

        /// <summary>
        /// 上次启动发送的时间戳
        /// </summary>
        private readonly Stopwatch _sendWatch = new Stopwatch();

        /// <summary>
        /// 一直递增的发送序号，由单连接发送泵按实际出队顺序写入协议头。
        /// </summary>
        private int _sendMsgId;

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
        /// 尚未发送完成的总字节数，包含队列和正在发送的批次。
        /// </summary>
        public long PendingSendBytes => Interlocked.Read(ref _pendingSendBytes);

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
        internal event Action<PeerSocket, ErrorType> ErrorOccurred;

        /// <summary>
        /// 连接成功的事件
        /// </summary>
        internal event Action<PeerSocket> ConnectCompleted;

        /// <summary>
        /// 数据发送完毕
        /// </summary>
        internal event Action<PeerSocket> SendCompleted;

        /// <summary>
        /// 数据接收完毕
        /// </summary>
        internal event Action<PeerSocket> ReceiveCompleted;

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
            TryAddSendData(data, offset, count, format, txrId, eventType);
        }

        /// <summary>
        /// 使用数据打包并尝试加入发送队列。达到单Peer待发送字节上限时返回false，避免高并发生产速度
        /// 长期超过网络吞吐后造成无界内存增长。
        /// </summary>
        /// <param name="data">要发送的数据。</param>
        /// <param name="offset">数据起始偏移。</param>
        /// <param name="count">数据长度。</param>
        /// <param name="format">数据格式。</param>
        /// <param name="txrId">事务ID。</param>
        /// <param name="eventType">事件类型。</param>
        /// <returns>成功打包并加入发送队列时返回true；被背压拒绝或打包失败时返回false。</returns>
        public bool TryAddSendData(byte[] data, int offset, int count,
            Format format = Format.Raw, int txrId = 0, int eventType = 0)
        {
            if (_disposed) {
                return false;
            }

            ByteBuffer packedData = null;
            bool reserved = false;
            bool enqueued = false;
            try {
                packedData = _packet.Pack(data, offset, count, format, txrId, eventType);
                if (!TryReservePendingSendBytes(packedData.Length)) {
                    LogSendBackpressure(packedData.Length);
                    packedData.Recycle();
                    return false;
                }
                reserved = true;

                _sendQueue.Enqueue(packedData);
                enqueued = true;
                packedData = null;
                if (_disposed) {
                    ClearQueuedSendData();
                    return false;
                }
                if (Config.EnableRttStatistics && txrId != 0) {
                    RttStatis.RecordSent(txrId); //记录发送
                }
                return true;
            } catch (Exception e) {
                if (reserved && packedData != null) {
                    ReleasePendingSendBytes(packedData.Length);
                }
                packedData?.Recycle();
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.AddSendData:[{Name}] 异常 {e}");
                return enqueued;
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
                socket.NoDelay = Config.EnableTcpNoDelay;

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
                socket.NoDelay = Config.EnableTcpNoDelay;

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
        /// 尝试取得当前Peer的发送泵所有权并开始发送。
        /// 同一个Peer始终只有一个发送泵，不同Peer可以由IOCP并行驱动。
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
            if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0) {
                return false;
            }

            try {
                return RunSendPump();
            } catch (Exception e) {
                AbortSendPumpAfterException(e);
                return false;
            }
        }

        /// <summary>
        /// 迭代驱动当前Peer的发送泵。异步提交后由IOCP回调继续；同步完成时在预算内原地循环，
        /// 避免递归增长调用栈。
        /// </summary>
        /// <returns>本轮至少向Socket提交过一次发送时返回true。</returns>
        private bool RunSendPump()
        {
            bool started = false;
            int synchronousCompletions = 0;
            int synchronousBudget = Math.Max(1, Config.MaxSynchronousSendCompletions);

            while (true) {
                if (_disposed || !IsConnected) {
                    Interlocked.Exchange(ref _isSending, 0);
                    return started;
                }

                PrepareSocketAsyncEventArgs();
                ConnectionContext context = _sendArgs.GetConnectionContext();
                if (context.sendBuffer == null && !TryBuildSendBatch(context)) {
                    if (TryReleaseAndReacquireSendPump()) {
                        continue;
                    }
                    return started;
                }

                _sendArgs.SetBuffer(
                    context.sendBuffer.Bytes,
                    context.sendOffset,
                    context.sendRemaining);

                try {
                    _sendWatch.Restart();
                    started = true;
                    if (socket.SendAsync(_sendArgs)) {
                        return true;
                    }
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.RunSendPump():[{Name}] 提交发送异常 {e}");
                    FailCurrentSend(_sendArgs);
                    return started;
                }

                if (!ProcessSendCompletion(_sendArgs)) {
                    return started;
                }

                synchronousCompletions++;
                if (synchronousCompletions < synchronousBudget) {
                    continue;
                }

                if (ScheduleSendPumpContinuation()) {
                    return true;
                }
                synchronousCompletions = 0;
            }
        }

        /// <summary>
        /// 从发送队列按配置上限创建下一批数据，并由单一发送泵按真实出队顺序写入消息ID。
        /// </summary>
        /// <param name="context">当前发送SocketAsyncEventArgs的连接上下文。</param>
        /// <returns>成功构造发送批次时返回true；队列为空时返回false。</returns>
        private bool TryBuildSendBatch(ConnectionContext context)
        {
            if (!_sendQueue.TryDequeue(out ByteBuffer first)) {
                return false;
            }

            int maxBatchBytes = Math.Max(Message.HeaderLength, Config.MaxSendBatchBytes);
            int maxBatchMessages = Math.Max(1, Config.MaxSendBatchMessages);
            List<ByteBuffer> buffers = context.sendBuffers;
            if (buffers == null) {
                buffers = new List<ByteBuffer>(16);
                context.sendBuffers = buffers;
            }
            buffers.Clear();

            int totalLength = 0;
            ByteBuffer sendBuffer = null;
            try {
                first.Bytes.SetHeaderId(_sendMsgId++);
                buffers.Add(first);
                totalLength = first.Length;

                while (buffers.Count < maxBatchMessages
                    && totalLength < maxBatchBytes
                    && _sendQueue.TryPeek(out ByteBuffer next)) {
                    if (next.Length > maxBatchBytes - totalLength) {
                        break;
                    }
                    if (!_sendQueue.TryDequeue(out next)) {
                        continue;
                    }

                    next.Bytes.SetHeaderId(_sendMsgId++);
                    buffers.Add(next);
                    totalLength += next.Length;
                }

                if (buffers.Count == 1) {
                    sendBuffer = first;
                }
                else {
                    sendBuffer = GlobalBuffer.Inst.Get(totalLength);
                    for (int i = 0; i < buffers.Count; i++) {
                        sendBuffer.Append(buffers[i]);
                    }
                    for (int i = 0; i < buffers.Count; i++) {
                        buffers[i].Recycle();
                    }
                }

                context.sendBuffer = sendBuffer;
                context.curSendMsgCount = buffers.Count;
                context.sendOffset = 0;
                context.sendRemaining = sendBuffer.Length;
                buffers.Clear();
                return true;
            } catch {
                if (sendBuffer != null && !ReferenceEquals(sendBuffer, first)) {
                    sendBuffer.Recycle();
                }
                for (int i = 0; i < buffers.Count; i++) {
                    buffers[i].Recycle();
                }
                ReleasePendingSendBytes(totalLength);
                buffers.Clear();
                throw;
            }
        }

        /// <summary>
        /// 在观察到队列为空时释放发送权，再重新检查一次队列并尝试抢回发送权，避免生产线程与
        /// 发送泵退出同时发生时丢失唤醒。
        /// </summary>
        /// <returns>当前线程重新取得发送权并应继续循环时返回true。</returns>
        private bool TryReleaseAndReacquireSendPump()
        {
            Interlocked.Exchange(ref _isSending, 0);
            if (_disposed || _sendQueue.IsEmpty) {
                return false;
            }
            return Interlocked.CompareExchange(ref _isSending, 1, 0) == 0;
        }

        /// <summary>
        /// 达到同步完成预算后，把发送泵续体投递到线程池，避免热点连接独占当前业务线程。
        /// </summary>
        /// <returns>成功投递时返回true。</returns>
        private bool ScheduleSendPumpContinuation()
        {
            try {
                return ThreadPool.QueueUserWorkItem(_continueSendPumpCallback);
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.ScheduleSendPumpContinuation():[{Name}] 异常 {e}");
                return false;
            }
        }

        /// <summary>
        /// 在线程池中继续运行已经持有所有权的发送泵。
        /// </summary>
        /// <param name="state">线程池回调状态，本实现不使用。</param>
        private void ContinueSendPump(object state)
        {
            if (_disposed) {
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }
            try {
                RunSendPump();
            } catch (Exception e) {
                AbortSendPumpAfterException(e);
            }
        }

        /// <summary>
        /// 原子预留待发送字节配额。
        /// </summary>
        /// <param name="byteCount">本次新消息占用的协议包字节数。</param>
        /// <returns>未超过单Peer背压上限并成功预留时返回true。</returns>
        private bool TryReservePendingSendBytes(int byteCount)
        {
            long limit = Config.MaxPendingSendBytesPerPeer;
            while (true) {
                long current = Interlocked.Read(ref _pendingSendBytes);
                if (limit > 0 && (byteCount > limit || current > limit - byteCount)) {
                    return false;
                }

                long next = current + byteCount;
                if (Interlocked.CompareExchange(ref _pendingSendBytes, next, current) == current) {
                    return true;
                }
            }
        }

        /// <summary>
        /// 原子释放已经发送完成或被丢弃的待发送字节，并在关闭竞态下把结果限制为非负数。
        /// </summary>
        /// <param name="byteCount">要释放的字节数。</param>
        private void ReleasePendingSendBytes(int byteCount)
        {
            while (true) {
                long current = Interlocked.Read(ref _pendingSendBytes);
                long next = Math.Max(0, current - byteCount);
                if (Interlocked.CompareExchange(ref _pendingSendBytes, next, current) == current) {
                    return;
                }
            }
        }

        /// <summary>
        /// 每秒最多输出一次发送背压警告，避免过载时日志成为新的性能瓶颈。
        /// </summary>
        /// <param name="rejectedBytes">被拒绝消息的协议包字节数。</param>
        private void LogSendBackpressure(int rejectedBytes)
        {
            if (LogProxy.Warning == null) {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref _lastSendBackpressureLogTimestamp);
            if (now - previous < Stopwatch.Frequency
                || Interlocked.CompareExchange(ref _lastSendBackpressureLogTimestamp, now, previous) != previous) {
                return;
            }

            LogProxy.Warning(
                $"PeerSocket.TryAddSendData():[{Name}] 触发发送背压，拒绝{rejectedBytes}字节，"
                + $"当前待发送{PendingSendBytes}字节，上限{Config.MaxPendingSendBytesPerPeer}字节");
        }

        /// <summary>
        /// 回收发送队列中尚未进入活动批次的全部缓冲区。
        /// </summary>
        private void ClearQueuedSendData()
        {
            while (_sendQueue.TryDequeue(out ByteBuffer buffer)) {
                ReleasePendingSendBytes(buffer.Length);
                buffer.Recycle();
            }
        }

        /// <summary>
        /// 清空当前所有的队列和数据结构
        /// </summary>
        public void Clear()
        {
            ClearQueuedSendData();
            while (_receQueue.TryDequeue(out var msg)) {
                msg.Recycle();
            }
            _packet.Clear();

            // 标记发送结束
            Interlocked.Exchange(ref _isSending, 0);

            RttStatis.Reset();

            _sendMsgId = 0;
            _receMsgId = 0;
            Interlocked.Exchange(ref _errorSignaled, 0);
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
                    ConnectCompleted?.Invoke(this);

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
                if (ProcessSendCompletion(args)) {
                    RunSendPump();
                }
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.OnSendCompleted():[{Name}] 异常 {e}");
                FailCurrentSend(args);
            }
        }

        /// <summary>
        /// 处理一次Socket发送完成。部分发送只推进当前缓冲区偏移，完整发送后才回收批次并统计消息数。
        /// </summary>
        /// <param name="args">本次发送使用的SocketAsyncEventArgs。</param>
        /// <returns>连接仍可继续由当前发送泵处理时返回true。</returns>
        private bool ProcessSendCompletion(SocketAsyncEventArgs args)
        {
            if (_disposed) {
                return false;
            }
            if (args.SocketError != SocketError.Success || args.BytesTransferred <= 0) {
                FailCurrentSend(args);
                return false;
            }

            ConnectionContext context = args.GetConnectionContext();
            if (context.sendBuffer == null
                || args.BytesTransferred > context.sendRemaining) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning(
                        $"PeerSocket.ProcessSendCompletion():[{Name}] 非法发送完成状态，"
                        + $"BytesTransferred={args.BytesTransferred}, Remaining={context.sendRemaining}");
                FailCurrentSend(args);
                return false;
            }

            if (Config.IsDebugMode && LogProxy.Debug != null)
                LogProxy.Debug(
                    $"PeerSocket.ProcessSendCompletion():[{Name}] 发送完成{args.BytesTransferred}字节，"
                    + $"批次剩余{context.sendRemaining - args.BytesTransferred}字节");

            context.sendOffset += args.BytesTransferred;
            context.sendRemaining -= args.BytesTransferred;
            bool batchCompleted = context.sendRemaining == 0;
            Status.RecordSentMessage(batchCompleted ? context.curSendMsgCount : 0, args.BytesTransferred);
            if (!batchCompleted) {
                return true;
            }

            ByteBuffer completedBuffer = context.sendBuffer;
            context.sendBuffer = null;
            context.curSendMsgCount = 0;
            context.sendOffset = 0;
            ReleasePendingSendBytes(completedBuffer.Length);
            completedBuffer.Recycle();

            try {
                SendCompleted?.Invoke(this);
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.ProcessSendCompletion():[{Name}] 执行SendCompleted事件异常 {e}");
            }
            return true;
        }

        /// <summary>
        /// 发送失败后丢弃当前连接的全部待发送数据、释放发送泵所有权并触发连接错误处理。
        /// TCP连接失败后无法确认远端已接收的边界，因此不能在同一连接内盲目重发。
        /// </summary>
        /// <param name="args">失败发送使用的SocketAsyncEventArgs。</param>
        private void FailCurrentSend(SocketAsyncEventArgs args)
        {
            ConnectionContext context = args.GetConnectionContext();
            ByteBuffer failedBuffer = context.sendBuffer;
            context.sendBuffer = null;
            context.curSendMsgCount = 0;
            context.sendOffset = 0;
            context.sendRemaining = 0;
            if (failedBuffer != null) {
                ReleasePendingSendBytes(failedBuffer.Length);
                failedBuffer.Recycle();
            }
            ClearQueuedSendData();
            Interlocked.Exchange(ref _isSending, 0);
            try {
                ProcessError(args);
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"PeerSocket.FailCurrentSend():[{Name}] 处理连接错误异常 {e}");
            }
        }

        /// <summary>
        /// 捕获发送泵状态机之外的意外异常，保证线程池异常不会逃逸并永久占用发送权。
        /// </summary>
        /// <param name="exception">发送泵运行时捕获的异常。</param>
        private void AbortSendPumpAfterException(Exception exception)
        {
            if (LogProxy.Warning != null)
                LogProxy.Warning($"PeerSocket.AbortSendPumpAfterException():[{Name}] 异常 {exception}");

            SocketAsyncEventArgs sendArgs = _sendArgs;
            if (sendArgs != null && sendArgs.UserToken is ConnectionContext) {
                FailCurrentSend(sendArgs);
                return;
            }

            ClearQueuedSendData();
            Interlocked.Exchange(ref _isSending, 0);
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
                if (msgCount > 0 && ReceiveCompleted != null) {
                    try {
                        ReceiveCompleted(this);
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
            if (Interlocked.CompareExchange(ref _errorSignaled, 1, 0) != 0) {
                return;
            }

            if (LogProxy.Warning != null)
                LogProxy.Warning($"PeerSocket.ProcessError():[{Name}] 进入了ProcessError,SocketError={args.SocketError}"); //显示下接收的信息
            ConnectionContext context = args.GetConnectionContext(); //使用传递的Token
            Socket errorSocket = context.socket;
            if (errorSocket != null) {
                try {
                    if (errorSocket.Connected && LogProxy.Debug != null)
                        LogProxy.Debug($"PeerSocket.ProcessError():[{Name}] 调用Shutdown()关闭连接");
                    if (errorSocket.Connected) {
                        errorSocket.Shutdown(SocketShutdown.Both);
                    }
                } catch (Exception ex) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerSocket.ProcessError():[{Name}] Shutdown()异常 {ex}");
                } finally {
                    try {
                        errorSocket.Close();
                    } catch (Exception ex) {
                        if (LogProxy.Warning != null)
                            LogProxy.Warning($"PeerSocket.ProcessError():[{Name}] Close()异常 {ex}");
                    }
                }
            }
            Interlocked.Exchange(ref _isSending, 0);
            //产生错误事件，这是一个很重要的事件，处理服务器连接断开等
            ErrorOccurred?.Invoke(this, ErrorType.IOError);
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

        private volatile bool _disposed;

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

                ConnectCompleted = null;
                ReceiveCompleted = null;
                SendCompleted = null;
                ErrorOccurred = null;

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
