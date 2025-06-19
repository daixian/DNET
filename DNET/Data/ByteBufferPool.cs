namespace DNET
{
    /// <summary>
    /// 一个byteBuffer池
    /// </summary>
    public class ByteBufferPool : IBufferPool
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public ByteBufferPool(long blockSize, long maxByteSize)
        {
            this._bolckSize = blockSize;

            _queueFree = new DQueue<ByteBuffer>((int)(maxByteSize / blockSize), 16);
        }

        /// <summary>
        /// 有效队列
        /// </summary>
        private DQueue<ByteBuffer> _queueFree;

        /// <summary>
        /// buffer块大小
        /// </summary>
        private long _bolckSize;

        /// <summary>
        /// buffer块大小
        /// </summary>
        public long bolckSize { get { return _bolckSize; } }

        /// <summary>
        /// 获得一个buffer
        /// </summary>
        /// <returns></returns>
        public ByteBuffer GetBuffer(int wantLength)
        {
            //如果期望大小直接大于实际能力大小
            if (wantLength > _bolckSize) {
                ByteBufferPool.countNew++;
                //这实际是一个错误，那就只好随便new一个
                return new ByteBuffer(wantLength);
            }

            ByteBuffer buff = _queueFree.Dequeue();
            //如果可用队列中取不到了（已经空了），那么就new一个
            if (buff == default(ByteBuffer)) {
                //DxDebug.LogConsole(string.Format("ByteBufferPool.GetBuffer():可用队列中取不到了(已经空了) poolBolckSize={0} wantLength={1}", _bolckSize, wantLength));
                //统计工作效果
                ByteBufferPool.countNew++;

                buff = new ByteBuffer(_bolckSize);
                buff._bufferPool = this; //设置所属是自己
            }
            else {
                //统计工作效果
                ByteBufferPool.countSaveGC++;

                buff.validLength = 0;
            }

            buff.isInFreePool = false;
            return buff;
        }

        /// <summary>
        /// 归还Buffer
        /// </summary>
        /// <param name="buff">要归还的buffer</param>
        public void RecycleBuffer(ByteBuffer buff)
        {
            _queueFree.LockEnter();
            _queueFree.EnqueueMaxLimit(buff);
            buff.validLength = 0;
            buff.isInFreePool = true;
            _queueFree.LockExit();
        }

        #region 统计实际运行效果

        /// <summary>
        /// 成功节省GC次数
        /// </summary>
        public static int countSaveGC = 0;

        /// <summary>
        /// 仍然消耗了的gc次数
        /// </summary>
        public static int countBadGC = 0;

        /// <summary>
        /// 成功节省GC次数
        /// </summary>
        public static int countNew = 0;

        #endregion 统计实际运行效果
    }
}
