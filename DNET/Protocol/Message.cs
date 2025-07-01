namespace DNET
{
    /// <summary>
    /// 一条消息.
    /// </summary>
    public class Message
    {
        /// <summary>
        /// 实际的头,它其实也是各种字段.
        /// </summary>
        public Header header = Header.CreateDefault();

        /// <summary>
        /// 数据
        /// </summary>
        public byte[] data;

        /// <summary>
        /// 数据格式
        /// </summary>
        public Format Format => header.format;

        /// <summary>
        /// 事务ID
        /// </summary>
        public int TxrId => header.txrId;

        /// <summary>
        /// 事件类型
        /// </summary>
        public int EventType => header.eventType;

        /// <summary>
        /// 数据长度
        /// </summary>
        public uint Length => header.dataLen;

        /// <summary>
        /// 文本数据
        /// </summary>
        public string Text {
            get {
                if (Format == Format.Text) {
                    return System.Text.Encoding.UTF8.GetString(data);
                }
                else {
                    return null;
                }
            }
        }
    }
}
