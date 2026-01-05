namespace DNET
{
    /// <summary>
    /// 给服务器的工作线程传递事件
    /// </summary>
    public struct ServerCommand
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        public enum Type
        {
            /// <summary>
            /// 无
            /// </summary>
            None,

            /// <summary>
            /// 异步的启动服务器(没有必要,去掉了)
            /// </summary>
            Start,

            /// <summary>
            /// 认证消息
            /// </summary>
            Accept,

            /// <summary>
            /// 向某个token发送
            /// </summary>
            Send,

            /// <summary>
            /// 接收事件
            /// </summary>
            Receive,

            /// <summary>
            /// 开始向所有用户的一次发送。自动发送这些用户待发送队列中的数据
            /// </summary>
            SendAll,

            /// <summary>
            /// 定时检查
            /// </summary>
            TimerCheckStatus,
        }

        /// <summary>
        /// 消息类型
        /// </summary>
        public Type type;

        /// <summary>
        /// 数据
        /// </summary>
        public byte[] data;

        /// <summary>
        /// 附加参数
        /// </summary>
        public int arg1;

        /// <summary>
        /// 附加参数
        /// </summary>
        public string text1;

        /// <summary>
        /// 附加参数
        /// </summary>
        public Peer peer;
    }
}
