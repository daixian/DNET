namespace DNET
{
    /// <summary>
    /// 客户端和服务器之间通信的数据打包方法以及暂存数据管理的接口。
    /// </summary>
    internal interface IPacket2
    {
        /// <summary>
        /// 用户添加一段要发送的数据进来
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="offset">偏移</param>
        /// <param name="count">计数</param>
        void AddSend(byte[] data, int offset, int count);

        /// <summary>
        /// 讲待发送数据提取拷贝到待发送的buffer中,其中sendCount为可写的长度。这是为了拼接多条消息一起发送。
        /// </summary>
        /// <param name="sendBuff">要写入的发送buffer</param>
        /// <param name="sendBuffOffset">发送buffer的起始偏移</param>
        /// <param name="sendCount">期望的可发送长度</param>
        /// <returns>实际写入发送的长度</returns>
        int WriteSendDataToBuffer(byte[] sendBuff, int sendBuffOffset, int sendCount);

        /// <summary>
        /// 当前的待发送数据长度.(一般不用)
        /// </summary>
        int SendDataLength { get; }

        /// <summary>
        /// 当前待发消息条数，程序会使用这个来判断当前是否还有未发送的数据
        /// </summary>
        int SendMsgCount { get; }

        /// <summary>
        /// 底层接收buffer将当前这次接收到的数据写入进来,这一步就需要进行数据包的解析了.
        /// </summary>
        /// <param name="receBuff">接收buffer</param>
        /// <param name="offset">接收buffer的offset</param>
        /// <param name="count">数据长度</param>
        /// <returns>当次接收到的数据条数</returns>
        int AddRece(byte[] receBuff, int offset, int count);

        /// <summary>
        /// 当前保存的接收消息的长度，用于传递给用户查询当前消息条数.
        /// </summary>
        int ReceMsgCount { get; }

        /// <summary>
        /// 得到一条接收的消息，用于传递给用户.
        /// </summary>
        /// <returns>一条消息</returns>
        ByteBuffer GetReceMsg();

        /// <summary>
        /// 用户提供一组消息Buffer缓存，提取一组消息。offset是用户提供的msgBuffers的起始位置，count是希望提取的最多的长度.
        /// </summary>
        /// <param name="msgBuffers">用户提供一组消息Buffer缓存</param>
        /// <param name="offset">用户提供的msgBuffers的起始位置</param>
        /// <param name="count">希望提取的最大的长度</param>
        /// <returns>实际提取到的消息</returns>
        int GetReceMsg(ByteBuffer[] msgBuffers, int offset, int count);
    }
}