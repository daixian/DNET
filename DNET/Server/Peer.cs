using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DNET.Protocol;

namespace DNET
{
    //delegate void ProcessData(SocketAsyncEventArgs args);

    /// <summary>
    /// 用户对象,也就是一个TCP连接的一个端点.Peer里面起码要封装一个Socket对象.
    /// 它最主要的就是有一个ID.
    /// </summary>
    public sealed class Peer : IDisposable
    {
        #region Constructor

        /// <summary>
        /// 构造函数，应该是内部的。由内部机制创建
        /// </summary>
        /// <param name="socket">用户对应的 Socket</param>
        /// <param name="sendArgs">创建出来的发送用的SocketAsyncEventArgs（将来可以放到池里）</param>
        /// <param name="receiveArgs">创建出来的接收用的SocketAsyncEventArgs（将来可以放到池里）</param>
        /// <param name="receiveBufferSize">数据接收缓存大小</param>
        internal Peer(Socket socket, SocketAsyncEventArgs sendArgs, SocketAsyncEventArgs receiveArgs, int receiveBufferSize)
        {
            this._tokenScket = socket;
            this.SendArgs = sendArgs;
            this.ReceiveArgs = receiveArgs;

            ReceiveBuffer = new byte[receiveBufferSize];

            //_sendQueue = new BytesQueue(int.MaxValue, MAX_BYTES_SIZE, 256);
            //_receiveQueue = new BytesQueue(int.MaxValue, MAX_BYTES_SIZE, 256);
            //_reserveQueuePacked = new BytesQueue(int.MaxValue, MAX_BYTES_SIZE, 256);

            LastMsgReceTickTime = DateTime.Now.Ticks;
            LastMsgSendTickTime = DateTime.Now.Ticks;

            //设置一下timeout
            socket.SendTimeout = 8 * 1000; //最长发送8秒超时
            socket.ReceiveTimeout = 0;

            userObj = new UserObj();
        }

        /// <summary>
        /// 现在由于在客户端也添加了一个Token，用于在协议事件的时候方便统一逻辑，当初始化客户端的token的时候调用这个构造方法
        /// </summary>
        internal Peer()
        {
            userObj = new UserObj();
        }

        #endregion Constructor

        #region Event

        /// <summary>
        /// 事件：当这个Token被释放时产生，可以用于确保释放UserObj对象
        /// </summary>
        public event Action<Peer> EventDispose;

        #endregion Event

        #region Fields

        ///// <summary>
        ///// 发送/接收最大队列长度
        ///// </summary>
        //private int MAX_QUEUE_LENGTH = 4096;

        /// <summary>
        /// 队列的最大字节长度，为4M，虽然理论上这个大小没有限制，但是实测如果过大则通信不正常（win上15M可工作，31M不能工作）。
        /// 现规定这个占用为4M，留成6M
        /// </summary>
        private int MAX_BYTES_SIZE = 6 * 1024 * 1024;

        /// <summary>
        /// 记录该客户端的Socket
        /// </summary>
        private Socket _tokenScket;

        private SocketAsyncEventArgs _sendArgs;
        private SocketAsyncEventArgs _receiveArgs;

        /// <summary>
        /// 要发送的数据队列（逻辑部分，已经预打包）
        /// </summary>
        ConcurrentQueue<ByteBuffer> _sendQueue = new ConcurrentQueue<ByteBuffer>();

        /// <summary>
        /// 接收到的数据队列（对应逻辑部分，已解包）
        /// </summary>
        ConcurrentQueue<Message> _receiveQueue = new ConcurrentQueue<Message>();

        /// <summary>
        /// 当前接收到的还未解包的数据
        /// </summary>
        private ConcurrentQueue<ByteBuffer> _reserveQueuePacked = new ConcurrentQueue<ByteBuffer>();

        /// <summary>
        /// ReserveData的锁
        /// </summary>
        private object _lockReserveData = new object();

        /// <summary>
        /// 当前异步发送计数
        /// </summary>
        private int _snedingCount = 0; //volatile

        /// <summary>
        /// disposed接口实现相关
        /// </summary>
        public bool disposed = false;

        #endregion Fields

        #region Property

        /// <summary>
        /// 它的ID，这个ID会一直递增的被分配
        /// </summary>
        public int ID {
            get;
            //这个ID由于是在DNET库中被分配，所以考虑应该设置为internal
            internal set;
        }

        /// <summary>
        /// 用户自定义的绑定对象，用于简单的绑定关联一个对象
        /// </summary>
        public UserObj userObj { get; set; }

        /// <summary>
        /// 拥有这个token的DNClient对象。
        /// </summary>
        public DNClient client { get; internal set; }

        /// <summary>
        /// 拥有这个token的DNServer对象。
        /// </summary>
        public DNServer server { get; internal set; }

        /// <summary>
        /// 用来记录最后一次收到这个Token发来的消息时间的Tick,创建这Token对象的时候初始化
        /// </summary>
        public long LastMsgReceTickTime { get; internal set; }

        /// <summary>
        /// 用来记录最后一次向这个Token发送的消息时间的Tick,创建这Token对象的时候初始化
        /// </summary>
        public long LastMsgSendTickTime { get; internal set; }

        /// <summary>
        /// 接收buffer
        /// </summary>
        internal byte[] ReceiveBuffer;

        /// <summary>
        /// 当前异步发送计数,由SocketListener对象控制修改。如果为1，表示正在发送
        /// </summary>
        public int SendingCount {
            get { return _snedingCount; }
        }

        /// <summary>
        /// 未解包的发送队列的长度
        /// </summary>
        public int WaitSendMsgCount {
            get { return _sendQueue.Count; }
        }

        /// <summary>
        /// 接收队列的长度
        /// </summary>
        public int ReceiveQueueCount {
            get { return _receiveQueue.Count; }
        }

        /// <summary>
        /// 记录客户端连接的Socket
        /// </summary>
        public Socket socket {
            get { return _tokenScket; }
        }

        /// <summary>
        /// 客户端的IP
        /// </summary>
        public string IP {
            get {
                try {
                    IPEndPoint clientipe = (IPEndPoint)_tokenScket.RemoteEndPoint;
                    return clientipe.Address.ToString();
                } catch (Exception) {
                    return "Unknow";
                }
            }
        }

        internal SocketAsyncEventArgs SendArgs {
            get { return _sendArgs; }
            private set { _sendArgs = value; }
        }

        internal SocketAsyncEventArgs ReceiveArgs {
            get { return _receiveArgs; }
            private set { _receiveArgs = value; }
        }

        /// <summary>
        /// 发送队列
        /// </summary>
        internal ConcurrentQueue<ByteBuffer> waitSendQueue => _sendQueue;

        /// <summary>
        /// 正在发送的ByteBuffer
        /// </summary>
        internal ByteBuffer sendingByteBuffer { get; set; }

        #endregion Property

        #region Exposed Function

        /// <summary>
        ///面向逻辑层，获取目前所有的已接收的数据(已解包)，返回byte[][]的形式,没有则返回null
        /// </summary>
        /// <returns>已接收的byte[]数据,没有则返回null</returns>
        public List<Message> GetReceiveData()
        {
            List<Message> messages = new List<Message>();
            while (_receiveQueue.TryDequeue(out var msg)) {
                messages.Add(msg);
            }
            return messages;
        }

        /// <summary>
        /// 添加一条要发送的消息（未打包的数据）,不会自动发送。
        /// 这一个方法会进行数据的预打包
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="index">数据起始</param>
        /// <param name="length">数据长度</param>
        public void AddSendData(byte[] data, int index, int length)
        {
            IPacket3 packet = DNServer.Inst.Packet;
            // 进行预打包然后加入到队列
            _sendQueue.Enqueue(packet.Pack(data, index, length, Format.Raw, 0, 0));

            // TODO: 判断发送队列长度，如果超出最大限制，则进行丢弃
            // LogProxy.LogWarning("Token.AddSendData():要发送的数据队列 丢弃了一段数据");
        }

        /// <summary>
        /// 关闭这个Token和它的连接
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        #endregion Exposed Function

        #region internal Function

        /// <summary>
        /// 记录下从客户端的接收到的未解包数据
        /// </summary>
        /// <param name="args"></param>
        internal void AddReceiveData(SocketAsyncEventArgs args)
        {
            int count = args.BytesTransferred;

            ByteBuffer receDate = GlobalBuffer.Inst.Get(args.BytesTransferred);
            receDate.Write(args.Buffer, args.Offset, args.BytesTransferred);
            _reserveQueuePacked.Enqueue(receDate);
            //if (!) {
            //    LogProxy.LogWarning("Token.SetData():接收的还未解包的数据队列 丢弃了一段数据");
            //}
        }

        /// <summary>
        /// 解包当前已经接收到的原始数据，结果存放进了已接收消息队列, 返回true如果接收到了消息.
        /// </summary>
        /// <param name="packeter"> 打包方法. </param>
        /// <param name="bytesLen">   [out] 接收到数据长度. </param>
        ///
        /// <returns> 解包出来的消息条数(注意不是长度). </returns>
        internal int UnpackReceiveData(IPacket3 packeter, out int bytesLen)
        {
            lock (this._lockReserveData) {
                // 解包所有的数据
                bytesLen = 0;
                int count = 0;

                while (_reserveQueuePacked.TryDequeue(out ByteBuffer item)) {
                    bytesLen += item.Length; // 顺便记录一个接收到的数据长度

                    var msgs = packeter.Unpack(item.buffer, 0, item.Length);
                    if (msgs != null && msgs.Count > 0) {
                        LastMsgReceTickTime = DateTime.Now.Ticks; //记录最近一次接收到消息的时间
                        foreach (var msg in msgs) {
                            if (msg.header.format == Format.Heart) {
                                LogProxy.LogDebug("Peer.UnpackReceiveData():接收到了心跳包 TokenID:" + this.ID);
                            }
                            else {
                                _receiveQueue.Enqueue(msg);
                                count++;
                            }
                        }
                    }
                    item.Recycle();
                }

                return count;
            }
        }

        /// <summary>
        /// 递减一个正在发送计数
        /// </summary>
        internal void DecrementSendingCount()
        {
            Interlocked.Decrement(ref _snedingCount);
        }

        /// <summary>
        /// 递增一个正在发送计数
        /// </summary>
        internal void IncrementSendingCount()
        {
            Interlocked.Increment(ref _snedingCount);

            //调用这个函数的时候，表示开始发送了
            LastMsgSendTickTime = DateTime.Now.Ticks; //记录最近一次发送消息的时间
        }

        #endregion internal Function

        #region IDisposable Members

        /// <summary>
        /// Dispose，会断开连接
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposed) {
                return;
            }
            //让类型知道自己已经被释放
            disposed = true;

            try {
                //执行Dispose事件
                if (EventDispose != null) //事件
                {
                    try {
                        EventDispose(this);
                    } catch (Exception e) {
                        LogProxy.LogWarning("Peer.Dispose()：执行事件EventDispose异常！" + e.Message);
                    }
                }

                if (disposing) {
                    // 清理托管资源
                }
                // 清理非托管资源

                _receiveArgs.Dispose();
                _sendArgs.Dispose();

                this._tokenScket.Shutdown(SocketShutdown.Send);
            } catch (Exception) {
                //不要的客户端，不抛出错误，直接Close()
            } finally {
                try {
                    this._tokenScket.Close();
                } catch (Exception) {
                }
            }
        }

        #endregion IDisposable Members
    }
}
