using System;
using System.Text;

namespace DNET
{
    /// <summary>
    /// 内部的buffer池
    /// </summary>
    public class GlobalBuffer
    {
        /// <summary>
        /// 单例实例懒加载容器
        /// </summary>
        private static readonly Lazy<GlobalBuffer> _instance = new Lazy<GlobalBuffer>(() => new GlobalBuffer());

        /// <summary>
        /// 单例
        /// </summary>
        public static GlobalBuffer Inst => _instance.Value;

        /// <summary>
        /// 支持的 buffer 大小分档
        /// </summary>
        private static readonly int[] _sizes = { 256, 512, 1024, 2048, 4096, 8192, 1024 * 16, 1024 * 32, 1024 * 64 };

        /// <summary>
        /// 缓冲池组
        /// </summary>
        private readonly ByteBufferPool[] _pools;

        /// <summary>
        /// 构造函数
        /// </summary>
        private GlobalBuffer()
        {
            _pools = new ByteBufferPool[_sizes.Length];
            for (int i = 0; i < _sizes.Length; i++) {
                _pools[i] = new ByteBufferPool(_sizes[i]);
            }
        }

        /// <summary>
        /// 获取适配的 ByteBuffer（会从最接近的池中获取）
        /// </summary>
        /// <param name="minSize">期望的最小容量</param>
        /// <returns>可用的ByteBuffer实例</returns>
        public ByteBuffer Get(int minSize)
        {
            // TODO: 如果 minSize <= 0，可考虑直接返回最小分档或抛出更明确的异常
            for (int i = 0; i < _sizes.Length; i++) {
                if (minSize <= _sizes[i]) {
                    return _pools[i].Get(minSize);
                }
            }

            // 超过最大分档，直接创建临时 ByteBuffer
            return new ByteBuffer(minSize);
        }

        /// <summary>
        /// 将字符串转换为 UTF-8 编码的字节数组,直接传出ByteBuffer
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public ByteBuffer GetEncodedUtf8(string text)
        {
            int maxBytes = Encoding.UTF8.GetMaxByteCount(text.Length);
            ByteBuffer buffer = Get(maxBytes);
            // 直接编码到 buffer 内部数组
            int byteCount = Encoding.UTF8.GetBytes(
                text,
                0,
                text.Length,
                buffer.Bytes,   // 你的内部 byte[]
                0   // 写入起始位置
            );
            buffer.SetLength(byteCount);
            return buffer;
        }
    }
}
