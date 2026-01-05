using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 通信传输的客户端类.
    /// 主要就是再加上一层工作线程的异步封装
    /// </summary>
    public class DNClient : IWorkHandler<CwMessage>
    {
        /// <summary>
        /// 单例实例懒加载容器
        /// </summary>
        private static readonly Lazy<DNClient> _instance = new Lazy<DNClient>(() => new DNClient());

        /// <summary>
        /// 单例
        /// </summary>
        public static DNClient Inst => _instance.Value;

        /// <summary>
        /// 服务器(远程)主机名
        /// </summary>
        private string _host; // = "127.0.0.1";//

        /// <summary>
        /// 服务器端口号
        /// </summary>
        private int _port; //9900

        /// <summary>
        /// 工作线程
        /// </summary>
        private WorkThread<CwMessage> _workThread;

        /// <summary>
        /// 底层的通信类
        /// </summary>
        private PeerSocket _peerSocket;

        /// <summary>
        /// 一个定时器
        /// </summary>
        private Timer _timer;

        /// <summary>
        /// 这个对象是否已经被释放掉
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// 这个客户端的名字
        /// </summary>
        public string Name { get; set; } = "Client";

        /// <summary>
        /// 是否已经连接上了服务器
        /// </summary>
        public bool IsConnected {
            get {
                // 这个检查会比工作线程完成更快,所以这里先确定工作线程准备好了,再启动.
                if (IsConnecting)
                    return false;

                if (_peerSocket == null) {
                    return false;
                }
                return _peerSocket.IsConnected;
            }
        }

        /// <summary>
        /// 是否正在连接
        /// </summary>
        public bool IsConnecting { get; private set; }

        /// <summary>
        /// 它的状态.
        /// </summary>
        public PeerStatus Status => _peerSocket?.Status;

        /// <summary>
        /// 往返延迟统计(它包括服务器的CPU执行时间),在发送带事务的类型的消息的时候，会记录延迟
        /// </summary>
        public RttStatistics RttStatis => _peerSocket?.RttStatis;

        /// <summary>
        /// 等待发送的消息数量
        /// </summary>
        public int WaitSendMsgCount {
            get {
                if (_peerSocket == null || _disposed) {
                    return 0;
                }
                return _peerSocket.WaitSendMsgCount;
            }
        }

        /// <summary>
        /// 等待提取的消息队列长度
        /// </summary>
        public int WaitReceMsgCount {
            get {
                if (_peerSocket == null || _disposed) {
                    return 0;
                }
                return _peerSocket.WaitReceMsgCount;
            }
        }

        #region 对外事件

        /// <summary>
        /// 事件：连接服务器成功
        /// </summary>
        public event Action<DNClient> EventConnectSuccess;

        /// <summary>
        /// 事件：接收到了新的消息，可以将任务加入这个事件交给数据解包线程。
        /// 有几条数据就会有几次事件，但是由于粘包问题这些事件可能会一连串的来。
        /// 用户在这个事件中应该自己调用GetReceiveData()。
        /// </summary>
        public event Action<DNClient> EventReceive;

        /// <summary>
        /// 事件：错误,可以用来通知服务器断线，关闭等。当进入这个事件的时候，此时与服务器的连接肯定已经断开了
        /// </summary>
        public event Action<DNClient, ErrorType> EventError;

        #endregion

        /// <summary>
        /// 连接服务器,输入IP和端口号。如果没有初始化在第一次会初始化。这是异步函数。
        /// </summary>
        /// <param name="host">主机IP</param>
        /// <param name="port">端口号</param>
        public void Connect(string host, int port)
        {
            lock (this) {
                if (IsConnecting) return;
                IsConnecting = true;

                try {
                    if (LogProxy.Debug != null)
                        LogProxy.Debug($"DNClient.Connect():{Name}连接服务器 主机:" + host + "  端口:" + port);
                    _host = host;
                    _port = port;

                    // 工作线程总是启动
                    if (_workThread == null) {
                        _workThread = new WorkThread<CwMessage>("DNClientWorkThread");
                    }
                    _workThread.ClearQueue();

                    // 定时检查的定时器
                    if (_timer == null) {
                        _timer = new Timer(state => {
                            // 让工作线程定时检查
                            var check = new CwMessage { type = CwMessage.Type.TimerCheckStatus };
                            _workThread.Post(in check, this);
                        });
                        _timer.Change(1000, 1000); //一秒后启动
                    }

                    if (_peerSocket == null) {
                        _peerSocket = new PeerSocket();
                        _peerSocket.Name = Name;
                        _peerSocket.EventError += OnError;
                        _peerSocket.EventReceiveCompleted += OnReceiveCompleted;
                    }

                    // 让工作线程处理这个消息
                    var msg = new CwMessage { type = CwMessage.Type.Connect };
                    _workThread.Post(in msg, this);
                } catch (Exception e) {
                    // 一般来说其实不会进入这个异常.因为这个函数只是吧一个Message添加到队列中，不会发生异常.
                    IsConnecting = false; //连接失败了
                    if (LogProxy.Error != null)
                        LogProxy.Error($"DNClient.Connect():{Name}未能启动连接,异常 {e}");
                }
            }
        }

        /// <summary>
        /// 关闭当前连接
        /// </summary>
        public void Disconnect()
        {
            lock (this) {
                try {
                    _workThread?.ClearQueue();
                    // 中断连接必须要释放socket,必须要Close().
                    if (_peerSocket != null) {
                        _peerSocket.Dispose(); // 这样可以保证正在连接中的也直接异常掉
                        _peerSocket = null;

                        IsConnecting = false;
                    }
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNClient.DisConnect():{Name}执行DisConnect异常 {e}");
                }
            }
        }

        /// <summary>
        /// 直接关闭,不会等待所有数据发送完成.
        /// </summary>
        /// <param name="clearEvent">是否清空事件</param>
        public void Close(bool clearEvent = true)
        {
            if (LogProxy.Info != null)
                LogProxy.Info($"DNClient.Close():{Name}准备关闭Socket和停止工作线程...");
            lock (this) {
                try {
                    _timer?.Dispose();
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNClient.Close():{Name}停止Timer异常 {e}");
                } finally {
                    _timer = null;
                }

                try {
                    _peerSocket?.Dispose();
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNClient.Close():{Name}关闭Socket异常 {e}");
                } finally {
                    _peerSocket = null;
                }

                try {
                    _workThread?.Stop();
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNClient.Close():{Name}停止工作线程异常 {e}");
                } finally {
                    _workThread = null;
                }

                IsConnecting = false;

                // 清空事件算了
                if (clearEvent) {
                    if (LogProxy.Info != null)
                        LogProxy.Info($"DNClient.Close():{Name}清空了所有绑定事件...");
                    EventConnectSuccess = null;
                    EventReceive = null;
                    EventError = null;
                    // EventSendQueueIsFull = null;
                    // EventSendQueueIsAvailable = null;
                }
            }
        }

        /// <summary>
        /// 发送一条数据
        /// </summary>
        /// <param name="data">要发送的整个数据</param>
        public void Send(byte[] data)
        {
            if (data == null) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.Send():传入data为null");
                return; //如果数据为null,那么返回算了
            }
            Send(data, 0, data.Length);
        }

        /// <summary>
        /// 发送一条数据，有起始和长度控制
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据的起始位置</param>
        /// <param name="count">数据的长度</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        /// <param name="immediately">是否立刻尝试开始发送</param>
        public void Send(byte[] data,
            int offset,
            int count,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0,
            bool immediately = true)
        {
            if (_peerSocket == null || _disposed) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.Send():{Name} PeerSocket为null或者disposed,不能发送.");
                return;
            }

            try {
                // 这里其实已经开始打包了.
                _peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
                if (immediately)
                    _peerSocket.TryStartSend(); //这个函数可以直接启动
                else {
                    // TODO: _workThread 可能为 null，需确认调用前已启动工作线程
                    // 让工作线程处理这个消息
                    var msg = new CwMessage { type = CwMessage.Type.Send };
                    _workThread.Post(in msg, this);
                }
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.Send():{Name}异常 " + e.Message);
            }
        }

        /// <summary>
        /// 发送字符串数据
        /// </summary>
        /// <param name="text">字符串数据</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        /// <param name="immediately">是否立刻尝试开始发送</param>
        public void Send(string text,
            Format format = Format.Text,
            int txrId = 0,
            int eventType = 0,
            bool immediately = true)
        {
            if (_peerSocket == null || _disposed) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.Send():{Name} PeerSocket为null或者disposed,不能发送.");
                return;
            }

            try {

                if (string.IsNullOrEmpty(text)) {
                    // TODO: 确认底层AddSendData对null的处理逻辑，避免空消息引发异常
                    Send(null, 0, 0, format, txrId, eventType, immediately); //发送一个没有内容的空消息
                    return;
                }
                // 直接编码到 buffer 内部数组
                // 替代 dataBytes = Encoding.UTF8.GetBytes(text);
                ByteBuffer buffer = GlobalBuffer.Inst.GetEncodedUtf8(text);
                Send(buffer.Bytes, 0, buffer.Length, format, txrId, eventType, immediately);
                buffer.Recycle();
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.Send:异常 {e}");
            }
        }

        /// <summary>
        /// 使用数据打包,然后添加到发送队列,用于合并一些消息一起发送,客户端一般不使用.
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据的起始位置</param>
        /// <param name="count">数据的长度</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        public void AddSendData(byte[] data, int offset, int count,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0)
        {
            if (_peerSocket == null || _disposed) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.AddSendData():{Name} PeerSocket为null或者disposed,不能发送.");
                return;
            }
            _peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
        }

        /// <summary>
        /// 尝试开始启动发送
        /// </summary>
        /// <param name="forceUseWorkThread">是否强制使用工作线程补偿发送</param>
        /// <returns>true表示确实启动了一个发送</returns>
        public bool TryStartSend(bool forceUseWorkThread = true)
        {
            if (_workThread == null || _peerSocket == null || _disposed) {
                return false;
            }

            if (_peerSocket.TryStartSend() && forceUseWorkThread == false)
                return true;
            // 在超高并发的时候TryStartSend()可能会漏掉一个发送,所以这里用工作线程再次尝试
            var msg = new CwMessage { type = CwMessage.Type.Send };
            _workThread.Post(in msg, this);
            return false;
        }

        /// <summary>
        /// 尝试用工作线程去驱动一次发送
        /// </summary>
        public void TryStartSendOnWorkThread()
        {
            if (_workThread == null || _peerSocket == null || _disposed) {
                return;
            }

            // 让工作线程处理这个消息
            var msg = new CwMessage { type = CwMessage.Type.Send };
            _workThread.Post(in msg, this);
        }

        /// <summary>
        /// 获取目前所有的已接收的数据.返回的结果是从ListPool中取的.处理完了之后可以送回ListPool.
        /// </summary>
        /// <returns>所有的收到的数据,没有则返回null</returns>
        /// <example>
        /// 使用示例：
        /// <code>
        /// // 使用完毕后将其归还
        /// ListPool&lt;Message&gt;.Shared.Recycle(msgs);
        /// </code>
        /// </example>
        public List<Message> GetReceiveData()
        {
            return _peerSocket?.GetReceiveMessages();
        }

        /// <summary>
        /// 发送队列是否太长
        /// </summary>
        /// <param name="queueLen">队列上限</param>
        public bool IsSendQueueOverflow(int queueLen = 1024)
        {
            if (_peerSocket == null) return false;
            return _peerSocket.WaitSendMsgCount >= queueLen;
        }

        #region 工作线程

        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="msg">要处理的消息。</param>
        /// <param name="waitTimeMs">这条消息等待了多长时间(ms)。</param>
        public void Handle(ref CwMessage msg, double waitTimeMs)
        {
            if (waitTimeMs > 500 && msg.type != CwMessage.Type.TimerCheckStatus) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.Handle():[{Name}]工作{msg.type}等待处理时间过长！waitTime:{waitTimeMs}ms");
            }

            switch (msg.type) {
                case CwMessage.Type.Connect:
                    DoConnect();
                    break;
                case CwMessage.Type.Send:
                    DoSend();
                    break;
                case CwMessage.Type.Receive:
                    DoReceive();
                    break;
                case CwMessage.Type.Close:
                    DoClose();
                    break;
                case CwMessage.Type.TimerCheckStatus:
                    DoTimerCheckStatus();
                    break;
            }
        }

        /// <summary>
        /// 工作线程内执行连接
        /// </summary>
        private void DoConnect()
        {
            try {
                // 如果已经连接上了,那么返回.,不要断开连接和重连接.因为用户那边的连接函数是异步的.
                // 用户那边判断还没有连接上那么就连接
                if (_peerSocket.IsConnected) {
                    IsConnecting = false;
                    return;
                }

                if (Config.IsDebugMode && LogProxy.Debug != null)
                    LogProxy.Debug($"DNClient.DoConnect():{Name}执行Connect...");

                //标记正在连接
                IsConnecting = true;

                //// 断开原先连接，绑定新ip，清理状态
                //_peerSocket.Disconnect();
                if (LogProxy.Info != null)
                    LogProxy.Info($"DNClient.DoConnect():{Name}正在连接...");
                _peerSocket.BindRemote(_host, _port);
                _peerSocket.Connect(); //这个函数连接失败会异常

                //标记已经结束了连接
                IsConnecting = false;

                if (_peerSocket.IsConnected) {
                    if (LogProxy.Info != null)
                        LogProxy.Info($"DNClient.DoConnect():{Name}连接服务器成功！{_host}:{_port}");
                    try {
                        EventConnectSuccess?.Invoke(this);
                    } catch (Exception e) {
                        if (LogProxy.Error != null)
                            LogProxy.Error($"DNClient.DoConnect():{Name}执行 EventConnectSuccess 事件异常 {e}");
                    }
                }
                else {
                    throw new Exception("连接失败,异常或取消了..");
                }
            } catch (Exception e) {
                if (LogProxy.Info != null)
                    LogProxy.Info($"DNClient.DoConnect():{Name}连接服务器失败！{e.Message}");

                try {
                    EventError?.Invoke(this, ErrorType.ConnectError); //事件类型：ConnectError
                } catch (Exception e2) {
                    if (LogProxy.Error != null)
                        LogProxy.Error($"DNClient.DoConnect():{Name}执行 EventError 事件异常 {e2}");
                }
            } finally {
                IsConnecting = false;
            }
        }

        /// <summary>
        /// 响应Timer的执行,目前是一秒一次
        /// </summary>
        private void DoTimerCheckStatus()
        {
            if (!IsConnected || Status == null)
                return;

            // 驱动一下未发送的数据,按理这里不需要
            if (_peerSocket.TryStartSend()) {
                // dx: 如果有时候正在合并发送数据,那么会刚好触发,提起发送一部分出去.这是正常的
                //LogProxy.LogWarning($"DNClient.DoTimerCheckStatus():{Name}这里TryStartSend成功了,这是不太应该的");
            }

            if (Config.IsAutoHeartbeat) {
                //如果时间已经超过了那么就发送心跳包
                if (Status.TimeSinceLastSend > Config.HeartBeatSendTime) {
                    //发送一次心跳包
                    Send(null, 0, 0, Format.Heart); //发个心跳包
                    if (LogProxy.Debug != null)
                        LogProxy.Debug($"DNClient.DoTimerCheckStatus():{Name}发送 HeartBeatData ~❤");
                }

                if (Status.TimeSinceLastReceived > Config.HeartBeatCheckTime) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNClient.DoTimerCheckStatus():{Name}长时间没有收到心跳包，判断可能已经掉线！");
                    Disconnect(); //关闭连接?
                }
            }

            if (Config.EnablePeerStatistics) {
                _peerSocket.Status.UpdateStatus();
            }
        }

        /// <summary>
        /// 工作线程内执行发送驱动
        /// </summary>
        private void DoSend()
        {
            try {
                if (IsConnected == false) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DNClient.DoSend:{Name}当前还未连接到一个主机!");
                    return;
                }
                // 尝试驱动一次,之后PeerSocket会一直发送直到没有数据
                _peerSocket.TryStartSend();
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.DoSend():{Name}异常: {e}");
            }
        }

        /// <summary>
        /// 工作线程内执行接收处理
        /// </summary>
        private void DoReceive()
        {
            // 原来这里是使用工作线程解包,现在省略了这些设计
            // 考虑在这里发出接收事件吗
        }

        /// <summary>
        /// 目前实际没有使用这个
        /// </summary>
        private void DoClose()
        {
            try {
                if (LogProxy.Debug != null)
                    LogProxy.Debug($"DNClient.DoClose():{Name}开始释放资源 ");

                if (_peerSocket != null) {
                    _peerSocket.Dispose();
                }

                IsConnecting = false;
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.DoClose():{Name}异常: " + e.Message);
            } finally {
                _peerSocket = null;
            }
        }

        #endregion

        #region PeerSocket的完成事件

        /// <summary>
        /// PeerSocket接收完成回调
        /// </summary>
        /// <param name="sender">触发事件的PeerSocket</param>
        private void OnReceiveCompleted(PeerSocket sender)
        {
            try {
                // dx: 这里也是立刻响应事件处理.不处理完不会开启下一个接收.
                EventReceive?.Invoke(this); //发出事件：接收到了数据
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.OnReceiveCompleted():{Name}执行外部事件 EventReceive 异常 {e}");
            }
        }

        /// <summary>
        /// PeerSocket发送完成回调
        /// </summary>
        private void OnSendCompleted()
        {
        }

        /// <summary>
        /// PeerSocket错误回调
        /// </summary>
        /// <param name="sender">触发事件的PeerSocket</param>
        /// <param name="errorType">错误类型</param>
        private void OnError(PeerSocket sender, ErrorType errorType)
        {
            try {
                EventError?.Invoke(this, errorType);
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.OnError():{Name}执行 EventError 事件异常 {e}");
            }
        }

        #endregion

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try {
                Close();
            } catch (Exception e) {
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DNClient.Dispose():{Name}异常 {e}");
            }
        }
    }
}
