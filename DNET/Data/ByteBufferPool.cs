using System;
using System.Collections.Concurrent;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// ByteBuffer的.net
    /// </summary>
    public class ByteBufferPool : IBufferPool
    {
        /// <summary>
        /// 这是线程安全的.
        /// </summary>
        private readonly ConcurrentStack<ByteBuffer> _pool = new ConcurrentStack<ByteBuffer>();

        /// <summary>
        /// 默认的块大小
        /// </summary>
        private int _blockSize;

        /// <summary>
        /// 最大的容量个数
        /// </summary>
        private int _capacityLimit;

        /// <summary>
        /// 已分配总个数
        /// </summary>
        private int _totalAllocated = 0;

        /// <summary>
        /// 创建一个 ByteBuffer 池
        /// </summary>
        /// <param name="blockSize"></param>
        /// <param name="capacityLimit"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public ByteBufferPool(int blockSize, int capacityLimit = 512)
        {
            if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));
            if (capacityLimit <= 0 || capacityLimit > 1024 * 64) throw new ArgumentOutOfRangeException(nameof(capacityLimit));

            _blockSize = blockSize;
            _capacityLimit = capacityLimit;
        }

        /// <summary>
        /// 池中可用数量
        /// </summary>
        public int InPoolCount => _pool.Count;

        /// <summary>
        /// 已分配总数
        /// </summary>
        public int TotalAllocated => _totalAllocated;

        /// <summary>
        /// 从池中租一个ByteBuffer，如果池为空则创建新的.
        /// </summary>
        /// <param name="requestedSize"></param>
        /// <returns></returns>
        public ByteBuffer Get(int requestedSize)
        {
            if (_pool.TryPop(out ByteBuffer buffer)) {
                if (buffer.Capacity < requestedSize) {
                    // 这里一定要检查容量,如果容量不够，那么就重新分配一个
                    buffer = new ByteBuffer(GetCapacityForSize(requestedSize));
                }

                buffer.Clear();
                buffer._bufferPool = this;
                return buffer;
            }

            ByteBuffer newBuf = null;
            if (requestedSize > _blockSize) {
                // 如果容量不够，那么就重新分配一个
                newBuf = new ByteBuffer(GetCapacityForSize(requestedSize));
            }
            else {
                newBuf = new ByteBuffer(_blockSize);
            }
            newBuf._bufferPool = this;
            Interlocked.Increment(ref _totalAllocated);
            return newBuf;
        }

        /// <summary>
        /// 将ByteBuffer回收进入池.
        /// </summary>
        /// <param name="buffer"></param>
        public void Recycle(ByteBuffer buffer)
        {
            if (buffer == null) return;
            buffer.Clear();

            if (_pool.Count < _capacityLimit) {
                _pool.Push(buffer);
            }
            else {
                // 超出上限丢弃
            }
        }

        /// <summary>
        /// 按照2倍的递增获取容量
        /// </summary>
        /// <param name="requestedSize"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private int GetCapacityForSize(int requestedSize)
        {
            int capacity = _blockSize;
            while (capacity < requestedSize) {
                capacity *= 2;
                if (capacity <= 0) // 防止溢出
                    throw new ArgumentOutOfRangeException(nameof(requestedSize), "Requested size too large");
            }
            return capacity;
        }
    }
}
