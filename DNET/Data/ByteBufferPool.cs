using System;
using System.Collections.Concurrent;

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
        private readonly int _blockSize;

        /// <summary>
        /// 最大的容量个数
        /// </summary>
        private readonly int _capacityLimit;

        /// <summary>
        /// 已分配总个数
        /// </summary>
        private int _totalAllocated;

        /// <summary>
        /// 成功复用的次数
        /// </summary>
        private long _reusedCount;

        /// <summary>
        /// 创建一个 ByteBuffer 池
        /// </summary>
        /// <param name="blockSize">单个buffer块大小</param>
        /// <param name="capacityLimit">池容量上限</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public ByteBufferPool(int blockSize, int capacityLimit = 512)
        {
            if (blockSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(blockSize));
            // TODO: 此处使用了尚未赋值的 _blockSize，容量上限判断可能失效
            if (capacityLimit <= 0 || capacityLimit * _blockSize > 1024 * 1024 * 1024) // 大小不要太大(超过1GB)
                throw new ArgumentOutOfRangeException(nameof(capacityLimit));

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
        /// 成功复用的次数
        /// </summary>
        public long ReusedCount => _reusedCount;

        /// <summary>
        /// 从池中租一个ByteBuffer，如果池为空则创建新的.
        /// </summary>
        /// <param name="requestedSize">请求的最小容量</param>
        /// <returns>可用的ByteBuffer实例</returns>
        public ByteBuffer Get(int requestedSize)
        {
            if (_pool.TryPop(out ByteBuffer buffer)) {
                if (buffer.Capacity < requestedSize) {
                    // 这里一定要检查容量,如果容量不够，那么就重新分配一个
                    buffer = new ByteBuffer(GetCapacityForSize(requestedSize));
                    _totalAllocated++; // 这是allocated
                }
                else {
                    _reusedCount++; // 我只是大致统计,够用了，没必要 Interlocked
                }

                // buffer.Reset();
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
            _totalAllocated++; // 这是allocated
            return newBuf;
        }

        /// <summary>
        /// 将ByteBuffer回收进入池.
        /// </summary>
        /// <param name="buffer">要归还的buffer</param>
        public void Recycle(ByteBuffer buffer)
        {
            if (buffer == null) return;
            // dx: 下面的Push是原子的,所以这里可以Reset();
            buffer.Reset();

            if (_pool.Count < _capacityLimit) {
                _pool.Push(buffer);
            }
            // 超出上限丢弃
        }

        /// <summary>
        /// 按照2倍的递增获取容量
        /// </summary>
        /// <param name="requestedSize">请求的最小容量</param>
        /// <returns>符合要求的实际容量</returns>
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
