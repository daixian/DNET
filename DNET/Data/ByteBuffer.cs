﻿using System;

namespace DNET
{
    /// <summary>
    /// 一个可回收重复使用的Byte Buffer,使用完毕之后需要调用Recycle()归还自己。
    /// </summary>
    public class ByteBuffer
    {
        /// <summary>
        /// 构造，输入块大小
        /// </summary>
        /// <param name="blockLength">内存buffer实际大小</param>
        public ByteBuffer(int blockLength)
        {
            _buffer = new byte[blockLength];
            // 当前长度为0
        }

        /// <summary>
        /// 构造，输入数据,会copy一次,一般不使用这个.
        /// </summary>
        /// <param name="data"></param>
        public ByteBuffer(byte[] data)
        {
            _buffer = new byte[data.Length];
            Write(data, 0, data.Length);
        }

        /// <summary>
        /// 实际数据的有效长度，即在buffer成员中实际有效的数据就是从0到validLength
        /// </summary>
        private int _length;

        /// <summary>
        /// 实际buffer块
        /// </summary>
        private readonly byte[] _buffer;

        /// <summary>
        /// buffer的有效的数据长度
        /// </summary>
        public int Length => _length;

        /// <summary>
        /// buffer块的容量
        /// </summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        /// 获取buffer块
        /// </summary>
        public byte[] buffer => _buffer;

        /// <summary>
        /// 清空buffer
        /// </summary>
        public void Reset() => _length = 0;

        /// <summary>
        /// 转为byte[],进行一次拷贝.
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            var result = new byte[_length];
            Buffer.BlockCopy(_buffer, 0, result, 0, _length);
            return result;
        }

        /// <summary>
        /// 归还自己
        /// </summary>
        public void Recycle()
        {
            if (_bufferPool != null) {
                _bufferPool.Recycle(this);
            }
            //else {
            //    _buffer = null;
            //} 
        }

        /// <summary>
        /// 将其他 buffer 的内容追加进来,会提升validLength。所以要注意在拷贝前设置
        /// validLength=0; 
        /// </summary>
        /// <param name="other"></param>
        public void Append(ByteBuffer other)
        {
            if (other == null || other._length == 0) return;
            if (_length + other._length > buffer.Length)
                throw new InvalidOperationException("Buffer overflow");

            Buffer.BlockCopy(other.buffer, 0, buffer, _length, other._length);
            _length += other._length;
        }

        /// <summary>
        /// 追加数据
        /// </summary>
        /// <param name="src"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Append(byte[] src, int offset, int count)
        {
            if (src == null || count <= 0) return;
            if (_length + count > _buffer.Length)
                throw new InvalidOperationException("Buffer overflow");

            Buffer.BlockCopy(src, offset, _buffer, _length, count);
            _length += count;
        }

        /// <summary>
        /// 从头写入数据
        /// </summary>
        /// <param name="src"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Write(byte[] src, int offset, int count)
        {
            if (src == null || count <= 0) return;
            if (count > _buffer.Length)
                throw new InvalidOperationException("Buffer overflow");

            Buffer.BlockCopy(src, offset, _buffer, 0, count);
            _length = count;
        }

        /// <summary>
        /// 从头写入数据
        /// </summary>
        /// <param name="source"></param>
        /// <param name="sourceBytesToCopy"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public unsafe void Write(void* source, long sourceBytesToCopy)
        {
            if (sourceBytesToCopy > Capacity)
                throw new InvalidOperationException($"Buffer capacity {Capacity} is less than source bytes to copy {sourceBytesToCopy}");

            fixed (byte* destPtr = _buffer) {
                Buffer.MemoryCopy(source, destPtr, Capacity, sourceBytesToCopy);
            }
            _length = (int)sourceBytesToCopy;
        }

        /// <summary>
        /// 它的所属buffer池
        /// </summary>
        internal IBufferPool _bufferPool = null;

    }
}
