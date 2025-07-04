using System;
using System.Runtime.InteropServices;

namespace DNET
{
    /// <summary>
    /// 通用消息头定义（确保结构体大小固定且字段顺序一致）
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct Header
    {
        /// <summary>
        /// 魔数 'XMSG' in little-endian
        /// </summary>
        [FieldOffset(0)]
        public uint magic;

        /// <summary>
        /// 协议版本
        /// </summary>
        [FieldOffset(4)]
        public ushort version;

        /// <summary>
        /// 保留字段（用于对齐或扩展）
        /// </summary>
        [FieldOffset(6)]
        public ushort reserved;

        /// <summary>
        /// 数据长度（不包含头部长度）
        /// </summary>
        [FieldOffset(8)]
        public int dataLen;

        /// <summary>
        /// 消息格式
        /// </summary>
        [FieldOffset(12)]
        public Format format;

        /// <summary>
        /// 事务ID（用于标识响应与请求）
        /// </summary>
        [FieldOffset(16)]
        public int txrId;

        /// <summary>
        /// 事件类型（用户自定义）
        /// </summary>
        [FieldOffset(20)]
        public int eventType;

        /// <summary>
        /// 返回一个默认初始化的 Header
        /// </summary>
        public static Header CreateDefault()
        {
            return new Header {
                magic = 0x584D5347, // 'XMSG' 小端序
                version = 1,
                reserved = 0,
                dataLen = 0,
                format = Format.None,
                txrId = 0,
                eventType = 0
            };
        }

        /// <summary>
        /// 写入到一个ByteBuffer的起始位置
        /// </summary>
        /// <param name="buff"></param>
        public unsafe void WriteToByteBuffer(ByteBuffer buff)
        {
            if (buff == null) throw new ArgumentNullException(nameof(buff));

            int size = sizeof(Header); // 需要加上 [StructLayout(LayoutKind.Sequential, Pack = 1)] 保证结构体布局
            fixed (Header* srcPtr = &this) {
                buff.Write(srcPtr, size);
            }
        }
    }
}
