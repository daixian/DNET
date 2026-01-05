namespace DNET
{
    /// <summary>
    /// 客户端线程消息
    /// </summary>
    public struct ClientCommand
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        public enum Type
        {
            /// <summary>
            ///  无
            /// </summary>
            None,

            /// <summary>
            /// 连接
            /// </summary>
            Connect,

            /// <summary>
            /// 向服务器发送
            /// </summary>
            Send,

            /// <summary>
            /// 接收事件消息
            /// </summary>
            Receive, // 将接收到缓存的数据进行处理

            /// <summary>
            /// 异步的关闭客户端
            /// </summary>
            Close,

            /// <summary>
            /// 定时检查
            /// </summary>
            TimerCheckStatus,
        }

        /// <summary>
        /// 消息类型
        /// </summary>
        public Type type;
    }
}
