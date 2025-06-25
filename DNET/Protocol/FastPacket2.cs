using System;
using System.Runtime.InteropServices;

namespace DNET
{
    /// <summary>
    /// 一种比较快速的打包方式，只用一个数据长度的int作分割(这个写的数据长度值不包含这个int头的长度),
    /// 非常常见的分包方式。
    /// </summary>
    public unsafe class FastPacket2 : IPacket2
    {
        /// <summary>
        /// 构造
        /// </summary>
        public FastPacket2()
        {
            //头就只有一个长度，最多是4
            _lastMsgHeadLenBuff = new byte[4];
            //使用这个指针读值发现了一个问题，有时候只能读到0，有些奇怪，就不用了
            //_lastMsgHeadLenBuffPtrInt = (int*)Marshal.UnsafeAddrOfPinnedArrayElement(_lastMsgHeadLenBuff, 0).ToPointer();
            _lastMsgHeadLenBuffCurIndex = 0;
        }

        /// <summary>
        /// 用户添加一段要发送的数据进来,会做一次数据的拷贝或者打包.之后输入的参数可以释放了.
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="offset">偏移</param>
        /// <param name="count">计数</param>
        public void AddSend(byte[] data, int offset, int count)
        {
            IntPtr msg = Marshal.AllocHGlobal(sizeof(int) + count);
            int* p = (int*)msg.ToPointer();
            p[0] = count; //消息首写一个包长度
            IntPtr dataPtr = new IntPtr(&p[1]);
            Marshal.Copy(data, offset, dataPtr, count);
            //item的长度是整个消息的长度，所以保函了前面的sizeof(int)
            IntPtr ipitem = Marshal.AllocHGlobal(sizeof(MsgItem));
            MsgItem* item = (MsgItem*)ipitem.ToPointer();
            item->msg = msg;
            item->index = 0;
            item->length = sizeof(int) + count;
            _queueSendData.Enqueue(ipitem);
        }

        /// <summary>
        /// 将待发送数据提取拷贝到待发送的buffer中,其中sendCount为可写的长度。这是为了拼接多条消息一起发送。
        /// </summary>
        /// <param name="sendBuff">要写入的发送buffer</param>
        /// <param name="sendBuffOffset">发送buffer的起始偏移</param>
        /// <param name="sendCount">期望的可发送长度(byte单位)</param>
        /// <returns>实际写入发送的长度(byte单位)</returns>
        public int WriteSendDataToBuffer(byte[] sendBuff, int sendBuffOffset, int sendCount)
        {
            if (sendBuff == null || sendBuffOffset + sendCount > sendBuff.Length) {
                throw new Exception("WriteSendDataToBuffer():sendBuff == null || sendBuffOffset + sendCount > sendBuff.Length");
            }
            if (_queueSendData.Count == 0) {
                return 0;
            }
            int copyedLength = 0;
            _queueSendData.LockEnter();
            while (true) {
                if (_queueSendData.Count == 0) {
                    break;
                }
                if (sendCount == 0) {
                    break;
                }
                IntPtr ipMsgItem = _queueSendData._queue.Peek();
                MsgItem* msgItem = (MsgItem*)ipMsgItem.ToPointer(); //得到当前的消息
                int copyLen = msgItem->length - msgItem->index;
                if (copyLen < sendCount) //这一整条消息都能写下
                {
                    //IntPtr p = Marshal.UnsafeAddrOfPinnedArrayElement(sendBuff, sendBuffOffset);
                    Marshal.Copy(msgItem->getCurCopyPtr(), sendBuff, sendBuffOffset, copyLen);
                    _queueSendData._queue.Dequeue(); //退出这一条
                    Marshal.FreeHGlobal(msgItem->msg); //释放这条
                    Marshal.FreeHGlobal(ipMsgItem);
                    copyedLength += copyLen;
                    sendBuffOffset += copyLen;
                    sendCount -= copyLen;
                }
                else //如果这一条消息整个sendBuff都写不下
                {
                    copyLen = sendCount; //那么使用最大长度为它的要发送的长度
                    Marshal.Copy(msgItem->getCurCopyPtr(), sendBuff, sendBuffOffset, copyLen);
                    msgItem->index += copyLen;
                    copyedLength += copyLen;
                    break; //那么也可以直接退出了
                }
            }
            _queueSendData.LockExit();

            return copyedLength;
        }

        /// <summary>
        /// 当前待发消息条数，程序会使用这个来判断当前是否还有未发送的数据
        /// </summary>
        public int SendMsgCount { get { return _queueSendData.Count; } }

        /// <summary>
        /// 底层接收buffer将当前这次接收到的数据写入进来,这一步就需要进行数据包的解析了.
        /// </summary>
        /// <param name="receBuff">接收buffer</param>
        /// <param name="offset">接收buffer的offset</param>
        /// <param name="count">数据长度</param>
        /// <returns>当次接收到的数据条数</returns>
        public int AddRece(byte[] receBuff, int offset, int count)
        {
            if (receBuff == null || offset + count > receBuff.Length) {
                throw new Exception("AddRece():receBuff == null || offset + count > receBuff.Length");
            }

            //这次接收到的消息条数
            int receMsgCount = 0;
            // _queueReceMsg.LockEnter();
            lock (_lastMsgHeadLenBuff) //随便的使用这个buff来锁好了,但是本质上如果要实现多线程，那么外面给出的data顺序必须要正确，这是很难保证的
            {
                while (true) {
                    if (count == 0) {
                        break;
                    }

                    //当前还没有收到消息(无半截消息)
                    if (_lastMsgHeadLenBuffCurIndex == 0) {
                        if (count >= sizeof(int)) {
                            //接下来这个4字节一定是消息长度了
                            Buffer.BlockCopy(receBuff, offset, _lastMsgHeadLenBuff, 0, sizeof(int));
                            _lastMsgHeadLenBuffCurIndex += sizeof(int);
                            offset += sizeof(int);
                            count -= sizeof(int);

                            _lastMsgLength = BitConverter.ToInt32(_lastMsgHeadLenBuff, 0); //最后一条消息的实际内容长度  _lastMsgHeadLenBuffPtrInt[0];
                            _lastMsg = _msgPool.Get(_lastMsgLength); //取得一块buffer存放去头去尾的消息

                            //拷贝一次消息的实际内容
                            CopyMsgData(ref receBuff, ref offset, ref count, ref receMsgCount);
                        }
                        else //才接受了不到4个字节
                        {
                            Buffer.BlockCopy(receBuff, offset, _lastMsgHeadLenBuff, 0, count); //先拷贝到头消息buffer里去
                            _lastMsgHeadLenBuffCurIndex += count;
                            offset += count;
                            count -= count;
                        }
                    }
                    else //如果有接受到半截消息
                    {
                        //如果上一次消息的头还没接收完
                        if (_lastMsgHeadLenBuffCurIndex < sizeof(int)) {
                            //如果这一次还是不能接受完头的4个字节
                            if (count + _lastMsgHeadLenBuffCurIndex < sizeof(int)) {
                                Buffer.BlockCopy(receBuff, offset, _lastMsgHeadLenBuff, _lastMsgHeadLenBuffCurIndex, count); //先拷贝到头消息buffer里去
                                _lastMsgHeadLenBuffCurIndex += count;
                                offset += count;
                                count -= count;
                                break;
                            }
                            else //如果这一次可以接收完头4个字节了，知道最后一条消息长度了
                            {
                                int copyLen = sizeof(int) - _lastMsgHeadLenBuffCurIndex;
                                Buffer.BlockCopy(receBuff, offset, _lastMsgHeadLenBuff, _lastMsgHeadLenBuffCurIndex, copyLen); //先拷贝到头消息buffer里去
                                _lastMsgHeadLenBuffCurIndex = sizeof(int);
                                offset += copyLen;
                                count -= copyLen;

                                _lastMsgLength = BitConverter.ToInt32(_lastMsgHeadLenBuff, 0); //_lastMsgHeadLenBuffPtrInt[0];//最后一条消息的实际内容长度
                                _lastMsg = _msgPool.Get(_lastMsgLength); //取得一块buffer存放去头去尾的消息

                                //拷贝一次消息的实际内容
                                CopyMsgData(ref receBuff, ref offset, ref count, ref receMsgCount);
                            }
                        }
                        else //上次消息的头已经接受完了，长度已经知道了
                        {
                            //拷贝一次消息的实际内容
                            CopyMsgData(ref receBuff, ref offset, ref count, ref receMsgCount);
                        }
                    }
                }
            }
            //  _queueReceMsg.LockExit();
            return receMsgCount;
        }

        /// <summary>
        /// 当前保存的接收消息的长度，用于传递给用户查询当前消息条数.
        /// </summary>
        public int ReceMsgCount { get { return _queueReceMsg.Count; } }

        /// <summary>
        /// 得到一条接收的消息，用于传递给用户，没有消息则返回null.
        /// </summary>
        /// <returns>一条消息</returns>
        public ByteBuffer GetReceMsg()
        {
            return _queueReceMsg.Dequeue();
        }

        /// <summary>
        /// 用户提供一组消息Buffer缓存，提取一组消息。offset是用户提供的msgBuffers的起始位置，count是希望提取的最多的长度.
        /// </summary>
        /// <param name="msgBuffers">用户提供一组消息Buffer缓存</param>
        /// <param name="offset">用户提供的msgBuffers的起始位置</param>
        /// <param name="count">希望提取的最大的长度</param>
        /// <returns>实际提取到的消息</returns>
        public int GetReceMsg(ByteBuffer[] msgBuffers, int offset, int count)
        {
            _queueReceMsg.LockEnter();
            int len = count < _queueReceMsg.Count ? count : _queueReceMsg.Count;
            int getCount = 0;
            for (int i = 0; i < len; i++) {
                ByteBuffer bf = _queueReceMsg.Dequeue();
                if (bf != null) {
                    msgBuffers[offset + i] = bf;
                    getCount++;
                }
                else {
                    break;
                }
            }
            _queueReceMsg.LockExit();
            return getCount;
        }

        /// <summary>
        /// 当前重启的时候用来清空内部数据
        /// </summary>
        public void Clear()
        {
            _queueSendData.LockEnter();
            while (_queueSendData._queue.Count > 0) {
                IntPtr ipMsgItem = _queueSendData._queue.Dequeue(); //退出这一条
                MsgItem* msgItem = (MsgItem*)ipMsgItem.ToPointer(); //得到当前的消息
                Marshal.FreeHGlobal(msgItem->msg); //释放这条
                Marshal.FreeHGlobal(ipMsgItem);
            }
            _queueSendData.LockExit();

            _queueReceMsg.LockEnter();
            while (_queueReceMsg._queue.Count > 0) {
                ByteBuffer bf = _queueReceMsg._queue.Dequeue();
                bf.Recycle(); //回收这一条
            }
            _queueReceMsg.LockExit();

            ClearTempLastMsg();
        }

        #region Send

        /// <summary>
        /// 待发送数据队列
        /// </summary>
        private DQueue<IntPtr> _queueSendData = new DQueue<IntPtr>(int.MaxValue, 256);

        private struct MsgItem
        {
            /// <summary>
            /// 消息
            /// </summary>
            public IntPtr msg;

            /// <summary>
            /// 当前已经拷贝出去的index
            /// </summary>
            public int index;

            /// <summary>
            /// 总长度
            /// </summary>
            public int length;

            /// <summary>
            /// 得到当前要拷贝的位置的指针
            /// </summary>
            /// <returns></returns>
            public IntPtr getCurCopyPtr()
            {
                unsafe {
                    byte* p = (byte*)msg.ToPointer();
                    return new IntPtr(&p[index]);
                }
            }
        }

        #endregion Send

        #region Receive

        /// <summary>
        /// 接收到的消息队列
        /// </summary>
        private DQueue<ByteBuffer> _queueReceMsg = new DQueue<ByteBuffer>(int.MaxValue, 256);

        /// <summary>
        /// 最后一条接收到的消息
        /// </summary>
        private ByteBuffer _lastMsg = null;

        /// <summary>
        /// 最后一条接收到的消息长度
        /// </summary>
        private int _lastMsgLength = 0;

        /// <summary>
        /// 用来保存最后一条消息的头和长度
        /// </summary>
        private byte[] _lastMsgHeadLenBuff;

        /// <summary>
        /// 消息对象池
        /// </summary>
        private ByteBufferPool _msgPool = new ByteBufferPool(4096);

        /// <summary>
        /// 当前接收到最后一条消息的头和长度的当前位置
        /// </summary>
        private int _lastMsgHeadLenBuffCurIndex;

        /// <summary>
        /// 清空临时记录的最后一条消息的所有记录
        /// </summary>
        private void ClearTempLastMsg()
        {
            _lastMsg = null;
            _lastMsgLength = 0;
            _lastMsgHeadLenBuffCurIndex = 0;
        }

        /// <summary>
        /// 拷贝一次消息数据的实际内容
        /// </summary>
        /// <param name="receBuff"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="receMsgCount"></param>
        private void CopyMsgData(ref byte[] receBuff, ref int offset, ref int count, ref int receMsgCount)
        {
            if (count + _lastMsg.Length >= _lastMsgLength) //如果这次接收到内容长度有完整消息
            {
                int copyLen = _lastMsgLength - _lastMsg.Length;
                //Buffer.BlockCopy(receBuff, offset, _lastMsg.buffer, _lastMsg.Length, copyLen);
                //_lastMsg.length = _lastMsgLength;
                _lastMsg.Append(receBuff, offset, copyLen);
                offset += copyLen;
                count -= copyLen;

                _queueReceMsg.Enqueue(_lastMsg); //记录这条消息
                receMsgCount++;
                ClearTempLastMsg();
            }
            else //如果这次接收到内容长度没有完整消息
            {
                //Buffer.BlockCopy(receBuff, offset, _lastMsg.buffer, _lastMsg.Length, count); //只好先拷贝count长度
                //_lastMsg.Length += count; //记录实际起始,好下次拷贝的时候利用
                _lastMsg.Append(receBuff, offset, count);
                offset += count;
                count -= count;
            }
        }

        #endregion Receive
    }
}
