using System.Net.Sockets;

using System.Collections.Generic;

namespace DNET
{
    /// <summary>
    /// 关联SocketAsyncEventArgs的上下文信息
    /// </summary>
    internal class ConnectionContext
    {
        /// <summary>
        /// 套接字记录
        /// </summary>
        public Socket socket;

        /// <summary>
        /// 当前发送buffer
        /// </summary>
        public ByteBuffer sendBuffer;

        /// <summary>
        /// 当前发送批次包含的消息数量，只有整个批次发送完成后才计入消息统计。
        /// </summary>
        public int curSendMsgCount;

        /// <summary>
        /// 当前发送批次下一次应从缓冲区开始发送的偏移。
        /// </summary>
        public int sendOffset;

        /// <summary>
        /// 当前发送批次尚未提交完成的字节数，用于处理Socket部分发送。
        /// </summary>
        public int sendRemaining;

        /// <summary>
        /// 单连接发送泵复用的合包列表，避免每个批次访问全局List池。
        /// </summary>
        public List<ByteBuffer> sendBuffers;

        /// <summary>
        /// 接收buffer这里也换着来吧.
        /// </summary>
        public ByteBuffer receiveBuffer;
    }
}
