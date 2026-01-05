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
        /// 消息序号主要用户框架排查发送流是否有错误.
        /// 在一次通信中的发送端所发出的消息这个id应该是递增的.
        /// </summary>
        [FieldOffset(4)]
        public int id;

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
                id = 0,
                dataLen = 0,
                format = Format.None,
                txrId = 0,
                eventType = 0
            };
        }

        /// <summary>
        /// 重置一下吧
        /// </summary>
        public void Reset()
        {
            magic = 0x584D5347; // 'XMSG' 小端序
            id = 0;
            dataLen = 0;
            format = Format.None;
            txrId = 0;
            eventType = 0;
        }

        /// <summary>
        /// 写入到一个ByteBuffer的起始位置
        /// </summary>
        /// <param name="buff">目标缓冲区</param>
        public unsafe void WriteToByteBuffer(ByteBuffer buff)
        {
            if (buff == null) throw new ArgumentNullException(nameof(buff));

            int size = sizeof(Header); // 需要加上 [StructLayout(LayoutKind.Sequential, Pack = 1)] 保证结构体布局
            fixed (Header* srcPtr = &this) {
                buff.Write(srcPtr, size);
            }
        }
    }

    /// <summary>
    /// 扩展方法
    /// </summary>
    public static class HeaderExtension
    {
        /// <summary>
        /// 从字节数组中读取Header
        /// </summary>
        /// <param name="buffer">包含Header数据的字节数组</param>
        /// <returns>解析得到的Header</returns>
        public static Header GetHeader(this byte[] buffer)
        {
            unsafe {
                if (buffer == null || buffer.Length < sizeof(Header))
                    throw new ArgumentException("Buffer too small for Header");
                fixed (byte* srcPtr = buffer) {
                    return *(Header*)srcPtr;
                }
            }
        }

        /// <summary>
        /// 得到Header的id
        /// </summary>
        /// <param name="buffer">包含Header数据的字节数组</param>
        /// <returns>Header中的id</returns>
        /// <exception cref="ArgumentException"></exception>
        public static int GetHeaderId(this byte[] buffer)
        {
            if (buffer == null || buffer.Length < 8) // 前4字节是 magic, 接下来4字节是 id
                throw new ArgumentException("Buffer too small to read Header.id");

            unsafe {
                fixed (byte* ptr = buffer) {
                    return *(int*)(ptr + 4); // offset 4 是 id
                }
            }
        }

        /// <summary>
        /// 从字节数组中设置Header的id
        /// </summary>
        /// <param name="buffer">包含Header数据的字节数组</param>
        /// <param name="id">要写入的id</param>
        public static void SetHeaderId(this byte[] buffer, int id)
        {
            if (buffer == null || buffer.Length < 8)
                throw new ArgumentException("Buffer too small to set Header.id");

            unsafe {
                fixed (byte* ptr = buffer) {
                    *(int*)(ptr + 4) = id;
                }
            }
        }
    }
}
