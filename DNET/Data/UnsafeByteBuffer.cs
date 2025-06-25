using System;
using System.Runtime.InteropServices;

namespace DNET
{
    /// <summary>
    /// 非托管内存缓冲区
    /// </summary>
    public unsafe class UnsafeByteBuffer : IDisposable
    {
        private byte* _buffer;
        private int _capacity;
        private int _position;

        /// <summary>
        /// 也是当前有效数据的长度
        /// </summary>
        public int Position => _position;

        /// <summary>
        /// 当前有效数据的长度
        /// </summary>
        public int Count => _position;

        /// <summary>
        /// 实际缓冲区容量
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// 缓冲区指针
        /// </summary>
        public byte* Ptr => _buffer;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="initialCapacity"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public UnsafeByteBuffer(int initialCapacity = 4096)
        {
            if (initialCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            _capacity = initialCapacity;
            _buffer = (byte*)Marshal.AllocHGlobal(_capacity).ToPointer();
            _position = 0;
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~UnsafeByteBuffer()
        {
            Dispose();
        }

        /// <summary>
        /// 清空所有内容，重置写入位置。
        /// </summary>
        public void Clear() => _position = 0;

        /// <summary>
        /// 设置写入位置。
        /// </summary>
        /// <param name="pos"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Seek(int pos)
        {
            if (pos < 0 || pos > _capacity) throw new ArgumentOutOfRangeException(nameof(pos));
            _position = pos;
        }

        /// <summary>
        /// 确保容量至少为 minSize（按2倍扩容策略自动增长）
        /// </summary>
        /// <param name="minSize"></param>
        public void EnsureCapacity(int minSize)
        {
            if (minSize <= _capacity) return;

            int newCapacity = _capacity;
            while (newCapacity < minSize) {
                newCapacity *= 2;
            }

            byte* newBuffer = (byte*)Marshal.AllocHGlobal(newCapacity).ToPointer();
            Buffer.MemoryCopy(_buffer, newBuffer, newCapacity, _position); // 只复制已有内容
            Marshal.FreeHGlobal((IntPtr)_buffer);

            _buffer = newBuffer;
            _capacity = newCapacity;
        }

        /// <summary>
        /// 追加数据，会自动扩容
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Append(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException("offset or count is invalid");

            EnsureCapacity(_position + count);

            fixed (byte* src = &data[offset]) {
                Buffer.MemoryCopy(src, _buffer + _position, _capacity - _position, count);
                _position += count;
            }
        }

        /// <summary>
        /// 追加数据，会自动扩容
        /// </summary>
        /// <param name="data"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Append(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Append(data, 0, data.Length);
        }

        /// <summary>
        /// 从 buffer 中移除指定范围的数据（将 offset 之后的数据前移 count 字节）
        /// </summary>
        /// <param name="offset">起始位置</param>
        /// <param name="count">要移除的字节数</param>
        public void Erase(int offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > _position)
                throw new ArgumentOutOfRangeException("offset 或 count 超出有效范围");

            int bytesToMove = _position - (offset + count);
            if (bytesToMove > 0) {
                Buffer.MemoryCopy(_buffer + offset + count, _buffer + offset,
                    _capacity - offset,
                    bytesToMove);
            }

            _position -= count;
        }

        /// <summary>
        /// 写入一个 unmanaged 类型（如 int, float, struct），写到末尾,自动扩容
        /// </summary>
        public void Write<T>(T value) where T : unmanaged
        {
            int size = sizeof(T);
            EnsureCapacity(_position + size);

            *(T*)(_buffer + _position) = value;
            _position += size;
        }

        /// <summary>
        /// 读取一个 unmanaged 类型（如 int, float, struct）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="offset"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public T Read<T>(int offset = 0) where T : unmanaged
        {
            int pos = offset < 0 ? 0 : offset;
            if (pos + sizeof(T) > _position) throw new InvalidOperationException("Buffer overflow");
            return *(T*)(_buffer + pos);
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public byte[] ToArray(int offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > _position)
                throw new ArgumentOutOfRangeException("Invalid offset or count.");

            byte[] result = new byte[count];
            unsafe {
                Marshal.Copy(new IntPtr(_buffer + offset), result, 0, count);

            }
            return result;
        }


        /// <summary>
        /// 拷贝数据到新的byte[]
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            byte[] result = new byte[_position];
            Marshal.Copy((IntPtr)_buffer, result, 0, _position);
            return result;
        }

        /// <summary>
        /// 释放内存
        /// </summary>
        public void Dispose()
        {
            if (_buffer != null) {
                Marshal.FreeHGlobal((IntPtr)_buffer);
                _buffer = null;
                _capacity = 0;
                _position = 0;
            }
            // 不需要再调用析构函数了
            GC.SuppressFinalize(this);
        }
    }
}
