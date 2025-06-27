namespace DNET
{
    /// <summary>
    /// 服务器删除一个Token时的删除原因
    /// </summary>
    public enum PeerErrorType
    {
        /// <summary>
        /// 用户逻辑上的手动删除
        /// </summary>
        UserManualDelete,

        /// <summary>
        /// 接收字节数为0
        /// </summary>
        BytesTransferredZero, //这个试试不去删除它

        /// <summary>
        /// 底层API能够捕获的错误
        /// </summary>
        SocketError,

        /// <summary>
        /// 心跳包超时
        /// </summary>
        HeartBeatTimeout,

        /// <summary>
        /// 清空所有Token
        /// </summary>
        ClearAllToken,
    }
}
