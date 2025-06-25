namespace DNET
{
    /// <summary>
    /// BufferPool接口
    /// </summary>
    public interface IBufferPool
    {
        /// <summary>
        /// 获得一个buffer
        /// </summary>
        /// <param name="requestedSize">期望的buffer大小</param>
        /// <returns></returns>
        ByteBuffer Get(int requestedSize);

        /// <summary>
        /// 归还一个buffer
        /// </summary>
        /// <param name="buff"></param>
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
