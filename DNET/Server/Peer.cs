using System;
using System.Collections.Generic;
using System.Text;

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
        /// 这个id十分重要.
        /// </summary>
        private int _id;

        /// <summary>
        /// 关联的PeerSocket
        /// </summary>
        private PeerSocket _peerSocket;

        /// <summary>
        /// 构造.
        /// </summary>
        internal Peer()
        {
            _peerSocket = new PeerSocket() { User = this };
        }

        /// <summary>
        /// 创建一个Peer对象
        /// </summary>
        /// <param name="name">Peer名称，用于调试区分</param>
        internal Peer(string name)
        {
            peerSocket = new PeerSocket() { Name = name, User = this };
        }

        /// <summary>
        /// 它的ID
        /// </summary>
        public int ID {
            get => _id;
            set {
                _id = value;
                if (peerSocket != null) {
                    peerSocket.ID = value; // 同步设置它的ID
                    peerSocket.User = this;
                }
            }
        }

        /// <summary>
        /// 名字主要是调试用
        /// </summary>
        public string Name => peerSocket.Name;

        /// <summary>
        /// 远程IP
        /// </summary>
        public string RemoteIP => peerSocket.RemoteIP;

        /// <summary>
        /// 用户自定义的绑定对象，用于简单的绑定关联一个对象
        /// </summary>
        public object User { get; set; }

        /// <summary>
        /// 拥有这个token的DNClient对象。
        /// </summary>
        public PeerSocket peerSocket {
            get => _peerSocket;
            set {
                _peerSocket = value;
                if (_peerSocket != null) {
                    _peerSocket.ID = ID; // 同步设置ID
                    _peerSocket.User = this;
                }
            }
        }

        /// <summary>
        /// 它的状态
        /// </summary>
        public PeerStatus Status => peerSocket?.Status;

        /// <summary>
        /// 往返延迟统计(它包括服务器的CPU执行时间),在发送带事务的类型的消息的时候，会记录延迟
        /// </summary>
        public RttStatistics RttStatis => peerSocket?.RttStatis;

        /// <summary>
        /// 等待发送消息队列长度
        /// </summary>
        public int WaitSendMsgCount {
            get {
                if (peerSocket == null || _disposed) {
                    return 0;
                }
                return peerSocket.WaitSendMsgCount;
            }
        }

        /// <summary>
        /// 等待提取的消息队列长度
        /// </summary>
        public int WaitReceMsgCount {
            get {
                if (peerSocket == null || _disposed) {
                    return 0;
                }
                return peerSocket.WaitReceMsgCount;
            }
        }

        /// <summary>
        /// 有等待提取的消息.
        /// </summary>
        public bool HasReceiveMsg {
            get {
                if (peerSocket == null || _disposed) {
                    return false;
                }
                return peerSocket.HasReceiveMsg;
            }
        }


        /// <summary>
        /// 发送队列是否太长
        /// </summary>
        public bool IsSendQueueOverflow(int queueLen = 1024)
        {
            if (peerSocket == null) {
                return false;
            }

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
            if (peerSocket == null || _disposed) return;

            // 这里其实已经开始打包了.
            peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
            peerSocket.TryStartSend(); //这个函数可以直接启动
        }

        /// <summary>
        /// 发送字符串数据
        /// </summary>
        /// <param name="text">字符串数据</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        public void Send(string text,
            Format format = Format.Text,
            int txrId = 0,
            int eventType = 0)
        {
            if (peerSocket == null || _disposed) return;

            try {
                if (string.IsNullOrEmpty(text)) {
                    Send(null, 0, 0, format, txrId, eventType); //发送一个没有内容的空消息
                    return;
                }
                // 直接编码到 buffer 内部数组
                ByteBuffer buffer = GlobalBuffer.Inst.GetEncodedUtf8(text);
                Send(buffer.Bytes, 0, buffer.Length, format, txrId, eventType);
                buffer.Recycle();
            } catch (Exception e) {
                if (LogProxy.Error != null)
                    LogProxy.Error($"Peer.Send():异常 {e}");
            }
        }


        /// <summary>
        /// 使用数据打包,然后添加到发送队列.
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据的起始位置</param>
        /// <param name="count">数据的长度</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        public void AddSendData(byte[] data, int offset, int count,
            Format format = Format.Raw, int txrId = 0, int eventType = 0)
        {
            if (peerSocket == null || _disposed) return;

            peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
        }

        /// <summary>
        /// 尝试开始启动发送
        /// </summary>
        /// <returns>true表示确实启动了一个发送</returns>
        public bool TryStartSend()
        {
            if (peerSocket == null || _disposed) return false;

            return peerSocket.TryStartSend();
        }

        /// <summary>
        /// 获取这个token的接收数据,,返回的结果是从ListPool中取的.处理完了之后可以送回ListPool.
        /// </summary>
        /// <returns></returns>
        /// <example>
        /// 使用示例：
        /// <code>
        /// // 使用完毕后将其归还
        /// ListPool&lt;Message&gt;.Shared.Recycle(msgs);
        /// </code>
        /// </example>
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
                User = null;
                peerSocket = null;
            }
        }
    }
}
