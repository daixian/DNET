using System;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 一个可回收重复使用的Byte Buffer,使用完毕之后需要调用Recycle()归还自己。
    /// </summary>
    public class ByteBuffer
    {
        /// <summary>
        /// 构造，输入块大小
        /// </summary>
        /// <param name="blockLength">内存buffer实际大小</param>
        public ByteBuffer(long blockLength)
        {
            buffer = new byte[blockLength];
        }

        /// <summary>
        /// 析构
        /// </summary>
        ~ByteBuffer()
        {
            //统计一个坏的GC
            ByteBufferPool.countBadGC++;
        }

        /// <summary>
        /// 实际有效长度，即在buffer成员中实际有效的区间就是从0到validLength
        /// </summary>
        public volatile int validLength;

        /// <summary>
        /// 实际buffer块
        /// </summary>
        public byte[] buffer;

        /// <summary>
        /// 是否被标记的正在空闲池里
        /// </summary>
        public volatile bool isInFreePool = false;

        /// <summary>
        /// 归还自己
        /// </summary>
        public void Recycle()
        {
            if (_bufferPool != null)
                _bufferPool.RecycleBuffer(this);
            validLength = 0;
        }

        /// <summary>
        /// 将一个外面的ByteBuffer的内容拷到自己里面来,会提升validLength。所以要注意在拷贝前设置
        /// validLength=0;
        /// </summary>
        /// <param name="other"></param>
        public void CopyIn(ByteBuffer other)
        {
            if (other == null || other.validLength == 0) {
                return;
            }

            Buffer.BlockCopy(other.buffer, 0, this.buffer, validLength, other.validLength);
            validLength += other.validLength;
        }

        /// <summary>
        /// 原子操作设置长度值，实际上对这个对象的拷贝操作等等都显然不是线程安全的，所以这个函数实际上没有必有。
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public int setValidLength(int value)
        {
            return Interlocked.Exchange(ref validLength, value);
        }


        /// <summary>
        /// 它的所属buffer池
        /// </summary>
        internal IBufferPool _bufferPool = null;

        #region 静态公共方法

        /// <summary>
        /// 归还一堆buff
        /// </summary>
        /// <param name="buffs">一组buff</param>
        public static void Recycle(ByteBuffer[] buffs)
        {
            if (buffs == null) {
                return;
            }
            for (int i = 0; i < buffs.Length; i++) {
                buffs[i].Recycle();
            }
        }

        /// <summary>
        /// 一组buff的实际有效总长度
        /// </summary>
        /// <param name="buffs">一组buff</param>
        /// <returns></returns>
        public static int Length(ByteBuffer[] buffs)
        {
            if (buffs == null) {
                return 0;
            }
            int length = 0;
            for (int i = 0; i < length; i++) {
                length += buffs[i].validLength;
            }
            return length;
        }

        #endregion 静态公共方法
    }
}
