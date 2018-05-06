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
        /// <param name="size">期望的buffer大小</param>
        /// <returns></returns>
        ByteBuffer GetBuffer(int size);

        /// <summary>
        /// 归还一个buffer
        /// </summary>
        /// <param name="buff"></param>
        void RecycleBuffer(ByteBuffer buff);
    }
}