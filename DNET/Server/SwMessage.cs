namespace DNET
{
    public struct SwMessage
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        public enum Type
        {
            None,

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
            SendAll
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
        /// 附加参数（服务器端用于记录了TokenID）
        /// </summary>
        public int arg1;

        /// <summary>
        /// 附加参数
        /// </summary>
        public string text1;

        /// <summary>
        /// 这个字段一般被直接赋值了，应该合并这个字段和arg1字段
        /// </summary>
        public Peer peer;
    }
}
