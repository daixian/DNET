using System.Net.Sockets;

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
        /// 因为会对消息进行整合发送,所以这里记录一下当前发送消息数量
        /// </summary>
        public int curSendMsgCount;

        /// <summary>
        /// 接收buffer这里也换着来吧.
        /// </summary>
        public ByteBuffer receiveBuffer;
    }
}
