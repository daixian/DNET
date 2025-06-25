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
        /// 构造函数
        /// </summary>
        public GlobalData()
        {
            byteBufferPool = new ByteBufferPool(1024);
        }

        /// <summary>
        /// 内存buff池
        /// </summary>
        internal ByteBufferPool byteBufferPool { get; private set; }
    }
}
