using System;
using System.Text;
using System.Threading;
using DNET;

namespace Flurl.DNET
{
    /// <summary>
    /// 持有一条已经从DNET接收列表转移所有权、但尚未完成控制器调度的请求。
    /// 排队期间保留池化Message；正文解析完成后可以提前释放，最终Dispose仍保持幂等。
    /// </summary>
    internal sealed class DnetInboundRequest : IDisposable
    {
        /// <summary>
        /// 当前请求仍持有的池化消息。
        /// </summary>
        private Message _message;

        /// <summary>
        /// 使用底层Peer和池化消息初始化请求。
        /// </summary>
        /// <param name="peer">发送请求的Peer。</param>
        /// <param name="message">已经取得所有权的池化消息。</param>
        internal DnetInboundRequest(Peer peer, Message message)
        {
            Peer = peer ?? throw new ArgumentNullException(nameof(peer));
            _message = message ?? throw new ArgumentNullException(nameof(message));
            TxrId = message.TxrId;
            EventType = message.EventType;
            Format = message.Format;
            Length = message.Length;
        }

        /// <summary>
        /// 获取发送请求的底层Peer。
        /// </summary>
        internal Peer Peer { get; }

        /// <summary>
        /// 获取请求事务ID。
        /// </summary>
        internal int TxrId { get; }

        /// <summary>
        /// 获取请求事件类型。
        /// </summary>
        internal int EventType { get; }

        /// <summary>
        /// 获取请求消息格式。
        /// </summary>
        internal Format Format { get; }

        /// <summary>
        /// 获取消息头声明的正文长度。
        /// </summary>
        internal int Length { get; }

        /// <summary>
        /// 把当前正文按UTF-8复制为字符串，并立即归还池化Message。
        /// 即使解码失败也会释放消息，避免业务异常延长缓冲区占用时间。
        /// </summary>
        /// <returns>UTF-8请求正文。</returns>
        internal string ReadUtf8AndRelease()
        {
            Message message = Interlocked.Exchange(ref _message, null);
            if (message == null) throw new ObjectDisposedException(nameof(DnetInboundRequest));

            try {
                if (message.data == null || message.data.Length == 0) return string.Empty;
                return Encoding.UTF8.GetString(message.data.Bytes, 0, message.data.Length);
            } finally {
                message.Recycle();
            }
        }

        /// <summary>
        /// 归还仍由当前请求持有的池化Message。重复调用不会重复回收。
        /// </summary>
        internal void ReleaseMessage()
        {
            Interlocked.Exchange(ref _message, null)?.Recycle();
        }

        /// <summary>
        /// 释放尚未提前回收的请求消息。
        /// </summary>
        public void Dispose()
        {
            ReleaseMessage();
        }
    }
}
