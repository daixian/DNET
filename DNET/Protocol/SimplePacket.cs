﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DNET
{
    /// <summary>
    /// 简单协议,这个在C++版本中采用.
    /// </summary>
    public class SimplePacket : IPacket3
    {
        // 魔数定义 'XMSG' 小端顺序
        private const uint MAGIC = 0x584D5347;

        // 最大允许消息大小 (16MB)
        private const int MAX_ALLOWED_SIZE = 16 * 1024 * 1024;

        // 接收缓冲区
        private readonly UnsafeByteBuffer _unpackBuff = new UnsafeByteBuffer();

        ///// <summary>
        ///// 由于Pack接口的传出都是ByteBuffer，所以这里用一个池存起来
        ///// </summary>
        //private ByteBufferPool _pool = new ByteBufferPool(2048);

        /// <summary>
        /// 打包数据
        /// </summary>
        /// <param name="data">要打包的数据</param>
        /// <param name="offset"></param>
        /// <param name="length">数据长度</param>
        /// <param name="format">数据实际格式</param>
        /// <param name="txrId">事务ID，用于标识本次通信的事务序号</param>
        /// <param name="eventType">事件类型，表示当前通信事件的类别</param>
        /// <returns>打包数据结果</returns>
        public ByteBuffer Pack(byte[] data, int offset, int length, Format format, int txrId, int eventType)
        {
            if (data == null || length < 0 || offset + length > data.Length)
                throw new ArgumentException("Invalid data length");

            int headerSize = Marshal.SizeOf<Header>();
            ByteBuffer result = GlobalBuffer.Inst.Get(headerSize + length);

            // 构造头
            Header header = Header.CreateDefault();
            header.dataLen = (uint)length;
            header.format = format;
            header.txrId = txrId;
            header.eventType = eventType;
            header.WriteToByteBuffer(result);
            result.Append(data, offset, data.Length);
            return result;
        }

        /// <summary>
        /// 直接打包一个 Message
        /// </summary>
        /// <param name="msg">Message 本身</param>
        /// <returns>协议打包结果</returns>
        public ByteBuffer Pack(Message msg)
        {
            int headerSize = Marshal.SizeOf<Header>();
            ByteBuffer result = GlobalBuffer.Inst.Get(headerSize + (int)msg.header.dataLen);
            msg.header.WriteToByteBuffer(result);
            result.Append(msg.data, 0, msg.data.Length);
            return result;
        }

        /// <summary>
        /// 持续的解包数据
        /// </summary>
        /// <param name="receBuff">接收数据缓冲区</param>
        /// <param name="offset">接收数据缓冲区起始</param>
        /// <param name="length">数据长度</param>
        /// <returns>解析到的完整数据包数量</returns>
        public List<Message> Unpack(byte[] receBuff, int offset, int length)
        {
            if (receBuff == null || length < 0 || offset + length > receBuff.Length)
                throw new ArgumentException("Invalid data length");

            List<Message> messages = new List<Message>();

            // 添加数据到缓存
            _unpackBuff.Append(receBuff, offset, length);

            int headerSize = Marshal.SizeOf<Header>();

            while (true) {
                if (!TrySyncToMagic())
                    break;

                if (_unpackBuff.Count < headerSize)
                    break;

                Header header = _unpackBuff.Read<Header>();

                if (header.magic != MAGIC) {
                    // 魔数错，清空缓存避免死循环
                    _unpackBuff.Clear();
                    throw new Exception("Invalid magic number in header");
                }

                if (header.dataLen > MAX_ALLOWED_SIZE) {
                    _unpackBuff.Clear();
                    throw new Exception("Message length exceeds max allowed size");
                }

                int totalLen = headerSize + (int)header.dataLen;
                if (_unpackBuff.Count < totalLen)
                    break; // 数据不够完整

                // 解析数据体
                Message msg = new Message {
                    header = header,
                    data = _unpackBuff.ToArray(headerSize, (int)header.dataLen)
                };

                messages.Add(msg);

                // 移除已消费数据
                _unpackBuff.Erase(0, totalLen);
            }

            return messages;
        }

        /// <summary>
        /// 当前是否有不完整的解析数据缓存着
        /// </summary>
        public bool IsUnpackCached => _unpackBuff.Count > 0;

        /// <summary>
        /// 同步到魔数位置
        /// </summary>
        /// <returns></returns>
        private unsafe bool TrySyncToMagic()
        {
            byte[] magicBytes = BitConverter.GetBytes(MAGIC); // 小端

            for (int i = 0; i + magicBytes.Length <= _unpackBuff.Count; i++) {
                bool found = true;
                for (int j = 0; j < magicBytes.Length; j++) {
                    if (_unpackBuff.Ptr[i + j] != magicBytes[j]) {
                        found = false;
                        break;
                    }
                }
                if (found) {
                    if (i > 0) {
                        LogProxy.LogWarning("SimplePacket.TrySyncToMagic():有丢弃数据!");
                        _unpackBuff.Erase(0, i);
                    }
                    return true;
                }
            }

            // 没找到魔数，保留最后几个字节以备拼接
            int keep = Math.Min(3, _unpackBuff.Count);
            if (_unpackBuff.Count > keep) {
                _unpackBuff.Erase(0, _unpackBuff.Count - keep);
            }
            return false;
        }

        void IPacket3.Clear()
        {
            _unpackBuff.Clear();
        }
    }
}
