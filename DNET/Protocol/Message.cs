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
        public ByteBuffer data;

        /// <summary>
        /// 发送端ID
        /// </summary>
        public int Id => header.id;

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
        public int Length => header.dataLen;

        /// <summary>
        /// Header的字节长度
        /// </summary>
        /// <returns>Header结构体的字节长度</returns>
        public static int HeaderLength {
            get {
                unsafe {
                    return sizeof(Header);
                }
            }
        }

        /// <summary>
        /// 文本数据
        /// </summary>
        public string Text {
            get {
                if (Format == Format.Text) {
                    // TODO: data 可能为 null，需确认上游是否保证存在有效数据
                    return System.Text.Encoding.UTF8.GetString(data.buffer, 0, data.Length);
                }
                else {
                    return null;
                }
            }
        }

        /// <summary>
        /// 如果有对象池,那么可以使用这个函数来重置.
        /// </summary>
        public void Reset()
        {
            header.Reset();
            data?.Recycle();
        }

        /// <summary>
        /// 获取一个对象
        /// </summary>
        /// <returns>从对象池中取得的Message实例</returns>
        public static Message Rent()
        {
            return Pool<Message>.Shared.Get();
        }

        /// <summary>
        /// 回收自己.
        /// </summary>
        /// <returns>无</returns>
        public void Recycle()
        {
            Reset();
            Pool<Message>.Shared.Recycle(this);
        }
    }
}
