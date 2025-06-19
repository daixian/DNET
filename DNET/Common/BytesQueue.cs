using System;
using System.Collections.Generic;

namespace DNET
{
    /// <summary>
    /// 一个线程同步Byte数组队列。这个队列有一个自动剔除前段消息的方法。
    /// 默认队列最大字节数为4M
    /// </summary>
    public class BytesQueue
    {
        /// <summary>
        /// 构造函数，maxCapacity定成队列的最大长度。
        /// 默认bytes大小为4M。
        /// </summary>
        /// <param name="maxCapacity">队列的最大长度</param>
        public BytesQueue(int maxCapacity)
        {
            this._queue = new Queue<byte[]>(maxCapacity);
            this.maxCount = maxCapacity;
        }

        /// <summary>
        /// 构造函数。maxCapacity设定成队列的最大长度。
        /// byteSize规定最大bytes大小。
        /// </summary>
        /// <param name="maxCapacity">队列的最大长度</param>
        /// <param name="byteSize">最大bytes大小</param>
        public BytesQueue(int maxCapacity, int byteSize)
        {
            this._queue = new Queue<byte[]>(maxCapacity);
            this.maxCount = maxCapacity;
            this.maxByteSize = byteSize;
        }

        /// <summary>
        /// 构造函数。maxCapacity设定成队列的最大长度。
        /// byteSize规定最大bytes大小,initCapacit规定初始大小。
        /// </summary>
        /// <param name="maxCapacity">队列的最大长度</param>
        /// <param name="byteSize">最大bytes大小</param>
        /// <param name="initCapacity">队列初始大小</param>
        public BytesQueue(int maxCapacity, int byteSize, int initCapacity)
        {
            this._queue = new Queue<byte[]>(initCapacity);
            this.maxCount = maxCapacity;
            this.maxByteSize = byteSize;
        }

        /// <summary>
        /// byte数据的队列
        /// </summary>
        private Queue<byte[]> _queue;

        /// <summary>
        /// 队列的最大长度
        /// </summary>
        private int maxCount;

        /// <summary>
        /// 队列的最大字节数，默认4M
        /// </summary>
        private int maxByteSize = 4 * 1024 * 1024; //4m

        /// <summary>
        /// 当前的队列中数据的总大小
        /// </summary>
        private int _curByteSize = 0;

        /// <summary>
        /// 当前这个队列是否已经过大
        /// </summary>
        public bool IsFull {
            get {
                if (_curByteSize >= maxByteSize) {
                    return true;
                }
                if (_queue.Count > maxCount) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }

        /// <summary>
        /// 移除并返回位于 Queue 开始处的对象，没有则返回null。
        /// </summary>
        /// <returns>一个byte数据，没有则返回null</returns>
        public byte[] Dequeue()
        {
            lock (this._queue) {
                if (this._queue.Count > 0) {
                    byte[] data = this._queue.Dequeue();
                    _curByteSize -= data.Length;
                    return data;
                }
                else {
                    return null;
                }
            }
        }

        /// <summary>
        /// 将一个byte[]添加到 Queue的结尾处。
        /// </summary>
        /// <param name="item">byte数据</param>
        public void Enqueue(byte[] item)
        {
            if (item == null) {
                throw new ArgumentNullException("Items null");
            }
            lock (this._queue) {
                this._queue.Enqueue(item);
                _curByteSize += item.Length;
            }
        }

        /// <summary>
        /// 清空队列
        /// </summary>
        public void Clear()
        {
            lock (this._queue) {
                this._queue.Clear();
                _curByteSize = 0;
            }
        }

        /// <summary>
        /// 如果元素数小于当前容量的 90%，将容量设置为队列中的实际元素数。
        /// </summary>
        public void TrimExcess()
        {
            lock (this._queue) {
                this._queue.TrimExcess();
            }
        }

        /// <summary>
        /// 队列的数据个数
        /// </summary>
        public int Count { get { return _queue.Count; } }

        /// <summary>
        /// 返回byte[][]的形式,没有则返回null
        /// </summary>
        /// <returns>byte[]数据，没有则返回null</returns>
        public byte[][] GetData()
        {
            lock (this._queue) {
                if (this._queue.Count > 0) {
                    int count = this._queue.Count;

                    byte[][] data = new byte[count][];
                    for (int i = 0; i < count; i++) {
                        data[i] = _queue.Dequeue();
                        _curByteSize -= data[i].Length;
                    }
                    return data;
                }
                else {
                    return null;
                }
            }
        }

        /// <summary>
        /// 将整个队列的所有数据一次返回
        /// </summary>
        /// <returns>结果数据</returns>
        public byte[] GetDataOnce()
        {
            if (this._queue.Count > 0) {
                lock (this._queue) {
                    if (this._queue.Count == 1) //如果队列里只有一条数据，那么不需要整合复制
                    {
                        byte[] data = this._queue.Dequeue();
                        _curByteSize -= data.Length; //空间大小
                        return data;
                    }
                    else {
                        byte[] alldata = new byte[_curByteSize];
                        int count = this._queue.Count;
                        int index = 0;
                        for (int i = 0; i < count; i++) {
                            byte[] data = this._queue.Dequeue();
                            _curByteSize -= data.Length; //空间大小
                            Buffer.BlockCopy(data, 0, alldata, index, data.Length);
                            index += data.Length;
                        }

                        return alldata;
                    }
                }
            }
            else {
                return null;
            }
        }

        /// <summary>
        /// 将整个队列的所有数据拼接上在他们前端的一段数据，然后一次返回
        /// </summary>
        /// <param name="frontData">拼在前面的数据</param>
        /// <returns>结果数据</returns>
        public byte[] GetDataOnce(byte[] frontData)
        {
            //如果参数的是null
            if (frontData == null) {
                return GetDataOnce();
            }

            if (this._queue.Count > 0) {
                lock (this._queue) {
                    byte[] alldata = new byte[_curByteSize + frontData.Length];
                    int count = this._queue.Count;
                    int index = 0;
                    //先拼上最前面的数据
                    Buffer.BlockCopy(frontData, 0, alldata, index, frontData.Length);
                    index += frontData.Length;
                    for (int i = 0; i < count; i++) {
                        byte[] data = this._queue.Dequeue();
                        _curByteSize -= data.Length; //空间大小
                        Buffer.BlockCopy(data, 0, alldata, index, data.Length);
                        index += data.Length;
                    }
                    return alldata;
                }
            }
            else {
                return frontData;
            }
        }

        /// <summary>
        /// 如果队列达到了限定长度，就自动丢弃最前端的。
        /// 如果正常返回true,丢弃返回false.
        /// </summary>
        /// <param name="item">向队列中加入的一项数据</param>
        /// <returns>如果正常返回true,丢弃返回false</returns>
        public bool EnqueueMaxLimit(byte[] item)
        {
            //是否有丢弃
            bool isDiscard = false;
            if (item == null) {
                throw new ArgumentNullException("BytesQueue.EnqueueMaxLimit():输入参数为null"); //注意其实下面的队列支持null
            }
            else if (item.Length >= maxByteSize) {
                throw new OutOfMemoryException("BytesQueue.EnqueueMaxLimit():加入的数组过大，超出了该队列的maxByteSize");
            }
            lock (this._queue) {
                if (_queue.Count < maxCount) {
                    _queue.Enqueue(item);
                    _curByteSize += item.Length; //空间大小
                }
                else {
                    //移出
                    byte[] remove = _queue.Dequeue();
                    _curByteSize -= remove.Length; //空间大小
                    _queue.Enqueue(item);
                    _curByteSize += item.Length; //空间大小

                    isDiscard = true;
                }
                //如果太大了也要移出
                while (_curByteSize > maxByteSize) {
                    byte[] remove = _queue.Dequeue();
                    _curByteSize -= remove.Length; //空间大小
                    isDiscard = true;
                }
            }
            return !isDiscard;
        }

        /// <summary>
        /// 静态方法：将一组byte[]整合成一个，用来代替在某种情况下的GetDataOnce()函数。该方法未使用此类成员。
        /// </summary>
        /// <param name="dataArr">输入的要整合的一组byte[]</param>
        /// <returns>会new一个byte[]作为返回</returns>
        public static byte[] BytesArrayToBytes(byte[][] dataArr)
        {
            if (dataArr.Length == 1) //如果只有一条数据那么不需要拷贝
            {
                return dataArr[0];
            }
            else {
                int length = 0;
                for (int i = 0; i < dataArr.Length; i++) {
                    length += dataArr[i].Length;
                }
                byte[] alldata = new byte[length];
                int index = 0;
                for (int i = 0; i < dataArr.Length; i++) {
                    Buffer.BlockCopy(dataArr[i], 0, alldata, index, dataArr[i].Length);
                    index += dataArr[i].Length;
                }
                return alldata;
            }
        }
    }
}
