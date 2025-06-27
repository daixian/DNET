namespace DNET
{
    /// <summary>
    /// DNET模块的事件类型
    /// </summary>
    public enum ErrorType
    {
        /// <summary>
        /// 连接服务器失败
        /// </summary>
        ConnectError,

        /// <summary>
        /// 目前这是代表一种广泛的通信错误，
        /// 发生了这个错误的时候服务器的连接已经断开了需要重新连接
        /// </summary>
        IOError,
    }
}
