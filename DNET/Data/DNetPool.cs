namespace DNET
{
    /// <summary>
    /// 内部的buffer池
    /// </summary>
    public class DNetPool
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public DNetPool()
        {
            //初始化内存池
            byteBufPools = new ByteBufferPools();
        }

        private static DNetPool _instance = new DNetPool();

        /// <summary>
        /// 获得实例
        /// </summary>
        /// <returns></returns>
        public static DNetPool GetInst()
        {
            return _instance;
        }

        /// <summary>
        /// 内存buff池
        /// </summary>
        internal ByteBufferPools byteBufPools;

        /// <summary>
        /// 内存buff池
        /// </summary>
        public ByteBufferPools ByteBuffPools
        {
            get
            {
                return byteBufPools;
            }
        }
    }
}