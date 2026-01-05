using System.Collections.Generic;

namespace DNET
{
    /// <summary>
    /// 协议数据包接口
    /// </summary>
    public interface IPacket3
    {
        /// <summary>
        /// 打包数据
        /// </summary>
        /// <param name="data">要打包的数据(需要支持传入null)</param>
        /// <param name="offset">数据起始偏移</param>
        /// <param name="length">数据长度</param>
        /// <param name="format">数据实际格式</param>
        /// <param name="txrId">事务ID，用于标识本次通信的事务序号</param>
        /// <param name="eventType">事件类型，表示当前通信事件的类别</param>
        /// <returns>打包数据结果</returns>
        ByteBuffer Pack(byte[] data, int offset, int length, Format format, int txrId, int eventType);

        /// <summary>
        /// 直接打包一个 Message
        /// </summary>
        /// <param name="msg">Message 本身</param>
        /// <returns>协议打包结果</returns>
        ByteBuffer Pack(Message msg);

        /// <summary>
        /// 持续的解包数据
        /// </summary>
        /// <param name="receBuff">接收数据缓冲区</param>
        /// <param name="offset">接收数据缓冲区起始</param>
        /// <param name="length">数据长度</param>
        /// <returns>解析到的完整数据包列表,可以为null</returns>
        List<Message> Unpack(byte[] receBuff, int offset, int length);

        /// <summary>
        /// 清理数据
        /// </summary>
        void Clear();

        /// <summary>
        /// 当前是否有不完整的解析数据缓存着
        /// </summary>
        int UnpackCachedCount { get; }
    }
}
