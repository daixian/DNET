using System;

namespace DNET
{
    /// <summary>
    /// 内部的buffer池
    /// </summary>
    public class GlobalData
    {
        private static readonly Lazy<GlobalData> _instance = new Lazy<GlobalData>(() => new GlobalData());

        /// <summary>
        /// 单例
        /// </summary>
        public static GlobalData Inst => _instance.Value;

        /// <summary>
        /// 支持的 buffer 尺寸分档
        /// </summary>
        private static readonly int[] _sizes = new int[] { 128, 256, 512, 1024, 2048, 4096, 8192 };
        private readonly ByteBufferPool[] _pools;

        /// <summary>
        /// 构造函数
        /// </summary>
        private GlobalData()
        {
            _pools = new ByteBufferPool[_sizes.Length];
            for (int i = 0; i < _sizes.Length; i++) {
                _pools[i] = new ByteBufferPool(_sizes[i]);
            }
        }

        /// <summary>
        /// 获取适配的 ByteBuffer（会从最接近的池中获取）
        /// </summary>
        public ByteBuffer GetBuffer(int minSize)
        {
            for (int i = 0; i < _sizes.Length; i++) {
                if (minSize <= _sizes[i]) {
                    return _pools[i].Get(minSize);
                }
            }

            // 超过最大分档，直接创建临时 ByteBuffer
            return new ByteBuffer(minSize);
        }

    }
}
