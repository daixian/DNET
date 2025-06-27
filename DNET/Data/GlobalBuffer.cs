﻿using System;

namespace DNET
{
    /// <summary>
    /// 内部的buffer池
    /// </summary>
    public class GlobalBuffer
    {
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
        public ByteBuffer Get(int minSize)
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
