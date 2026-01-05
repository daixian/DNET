namespace DNET
{
    /// <summary>
    /// BufferPool接口
    /// </summary>
    public interface IBufferPool
    {
        /// <summary>
        /// 获得一个buffer.注意实现的时候由调用这个函数的线程Rest()
        /// </summary>
        /// <param name="requestedSize">期望的buffer大小</param>
        /// <returns>可用的ByteBuffer实例</returns>
        ByteBuffer Get(int requestedSize);

        /// <summary>
        /// 归还一个buffer
        /// </summary>
        /// <param name="buff">要归还的buffer</param>
        void Recycle(ByteBuffer buff);

        /// <summary>
        /// 获取已分配的buffer数量
        /// </summary>
        int TotalAllocated { get; }

        /// <summary>
        /// 获取已放入池中的buffer数量 
        /// </summary>
        int InPoolCount { get; }
    }
}
