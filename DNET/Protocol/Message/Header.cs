using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DNET.Protocol
{
    /// <summary>
    /// 通用消息头定义（确保结构体大小固定且字段顺序一致）
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Header
    {
        /// <summary>
        /// 魔数 'XMSG' in little-endian
        /// </summary>
        public uint magic;

        /// <summary>
        /// 协议版本
        /// </summary>
        public ushort version;

        /// <summary>
        /// 保留字段（用于对齐或扩展）
        /// </summary>
        public ushort reserved;

        /// <summary>
        /// 数据长度（不包含头部长度）
        /// </summary>
        public uint dataLen;

        /// <summary>
        /// 消息格式
        /// </summary>
        public Format format;

        /// <summary>
        /// 事务ID（用于标识响应与请求）
        /// </summary>
        public int txrId;

        /// <summary>
        /// 事件类型（用户自定义）
        /// </summary>
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
