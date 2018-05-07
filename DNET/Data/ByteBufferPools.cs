using System.Collections.Generic;

namespace DNET
{
    /// <summary>
    /// 一组ByteBufferPool
    /// </summary>
    public class ByteBufferPools
    {
        /// <summary>
        ///  构造
        /// </summary>
        public ByteBufferPools()
        {
            long curBlockSize = mixBlockSize;
            while (curBlockSize <= maxBlockSize)
            {
                //统一使用4M好了
                ByteBufferPool bfPool = new ByteBufferPool(curBlockSize, 4 * 1024 * 1024);
                curBlockSize = 2 * curBlockSize;
                _listPool.Add(bfPool);
            }
        }

        private List<ByteBufferPool> _listPool = new List<ByteBufferPool>();

        /// <summary>
        /// 最小一档的内存块size
        /// </summary>
        private long mixBlockSize = 32;

        /// <summary>
        /// 最大一档的内存块size
        /// </summary>
        private long maxBlockSize = 1024 * 1024 * 1;

        /// <summary>
        /// 根据期望大小获得一个buffer.
        /// autoSetValidLength为true则默认设置validLength为期望大小,否则设置为0.
        /// </summary>
        /// <param name="size">期望大小</param>
        /// <param name="autoSetValidLength">是否validLength会自动标记为size</param>
        /// <returns></returns>
        public ByteBuffer GetBuffer(long size, bool autoSetValidLength = false)
        {
            ByteBuffer bbf = null;
            if (size > maxBlockSize)
            {
                ByteBufferPool.countNew++;
                DxDebug.LogConsole("ByteBufferPools.GetBuffer():申请了一块过大的内存,size=" + size);
                //这个内存块太了，所以就不作缓存了
                bbf = new ByteBuffer(size);
            }
            else
            {
                ByteBufferPool bbPool = ChoosePool(size);
                bbf = bbPool.GetBuffer((int)size);
            }
            if (autoSetValidLength)
            {
                bbf.validLength = (int)size;
            }
            else
            {
                bbf.validLength = 0;
            }
            return bbf;
        }

        /// <summary>
        /// 将一组数据直接拷贝过来合成新的，isRecycle为true则会自动回收data。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="isRecycle"></param>
        /// <returns></returns>
        public ByteBuffer GetBufferCopy(ByteBuffer[] data, bool isRecycle = true)
        {
            //拼接需要的buffer长度
            long length = 0;
            for (int i = 0; i < data.Length; i++)
            {
                length += data[i].validLength;
            }
            //取一个buffer
            ByteBuffer res = GetBuffer(length);
            res.validLength = 0;
            //拷贝进来data的数据
            for (int i = 0; i < data.Length; i++)
            {
                res.CopyIn(data[i]);
                if (isRecycle)
                    data[i].Recycle();//这个数据已经没有用了，回收
            }
            return res;
        }

        /// <summary>
        /// 将一组数据直接拷贝过来合成新的，isRecycle为true则会自动回收data。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="isRecycle"></param>
        /// <returns></returns>
        public ByteBuffer GetBufferCopy(ICollection<ByteBuffer> data, bool isRecycle = true)
        {
            //拼接需要的buffer长度
            long length = 0;
            foreach (var item in data)
            {
                length += item.validLength;
            }

            //取一个buffer
            ByteBuffer res = GetBuffer(length);
            res.validLength = 0;
            //拷贝进来data的数据
            foreach (var item in data)
            {
                res.CopyIn(item);
                if (isRecycle)
                    item.Recycle();//这个数据已经没有用了，回收
            }
            return res;
        }

        ///// <summary>
        ///// 将一组数据直接拷贝过来合成新的，isRecycle为true则会自动回收data。
        ///// </summary>
        ///// <param name="data"></param>
        ///// <param name="isRecycle"></param>
        //public ByteBuffer GetBufferCopy(List<ByteBuffer> data, bool isRecycle = true)
        //{
        //    //拼接需要的buffer长度
        //    long length = 0;
        //    for (int i = 0; i < data.Count; i++)
        //    {
        //        length += data[i].validLength;
        //    }
        //    //取一个buffer
        //    ByteBuffer res = DNetPool.GetInst().byteBufPools.GetBuffer(length);

        //    //拷贝进来data的数据
        //    for (int i = 0; i < data.Count; i++)
        //    {
        //        res.CopyIn(data[i]);
        //        if (isRecycle)
        //            data[i].Recycle();//这个数据已经没有用了，回收
        //    }
        //    return res;
        //}

        /// <summary>
        /// 根据期望大小选择一个pool,如果期望大小不在pools里那么就返回null
        /// </summary>
        /// <param name="size">期望大小</param>
        /// <returns></returns>
        private ByteBufferPool ChoosePool(long size)
        {
            int curIndex = 0;
            long curBlockSize = mixBlockSize;
            while (size > curBlockSize)
            {
                curIndex++;
                curBlockSize = 2 * curBlockSize;
            }

            if (_listPool.Count > curIndex)
                return _listPool[curIndex];
            else
                return null;
        }
    }
}