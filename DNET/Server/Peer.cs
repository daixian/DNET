using System;
using System.Collections.Generic;

namespace DNET
{
    /// <summary>
    /// 用户对象,也就是一个TCP连接的一个端点.Peer里面起码要封装一个Socket对象.
    /// 它最主要的就是有一个ID.
    /// </summary>
    public sealed class Peer : IDisposable
    {
        /// <summary>
        /// 标记是否已经被disposed
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// 现在由于在客户端也添加了一个Token，用于在协议事件的时候方便统一逻辑，当初始化客户端的token的时候调用这个构造方法
        /// </summary>
        internal Peer()
        {
            peerSocket = new PeerSocket();
        }

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
        public object user { get; set; }

        /// <summary>
        /// 拥有这个token的DNClient对象。
        /// </summary>
        public PeerSocket peerSocket { get; internal set; }

        /// 它的状态.
        /// </summary>
        public PeerStatus Status => peerSocket.peerStatus;

        /// <summary>
        /// 发送队列是否太长
        /// </summary>
        public bool IsSendQueueOverflow(int queueLen = 1024)
        {
            return peerSocket.WaitSendMsgCount >= queueLen;
        }

        /// <summary>
        /// 发送一条数据，有起始和长度控制.这是立即发送.
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据的起始位置</param>
        /// <param name="count">数据的长度</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        public void Send(byte[] data,
            int offset,
            int count,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0)
        {
            // 这里其实已经开始打包了.
            peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
            peerSocket.TryBeginSend(); //这个函数可以直接启动
        }

        /// <summary>
        /// 获取这个token的接收数据
        /// </summary>
        /// <returns></returns>
        public List<Message> GetReceiveData() => peerSocket?.GetReceiveMessages();

        /// <summary>
        /// 释放
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try {
                //执行Dispose事件
                //if (EventDispose != null) //事件
                //{
                //    try {
                //        EventDispose(this);
                //    } catch (Exception e) {
                //        LogProxy.LogWarning("Peer.Dispose()：执行事件EventDispose异常！" + e.Message);
                //    }
                //}

                peerSocket.Dispose();
            } catch (Exception) {
                //不要的客户端，不抛出错误，直接Close()
            } finally {
                user = null;
                peerSocket = null;
            }
        }
    }
}
