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
        /// 用户添加一段要发送的数据进来
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="offset">偏移</param>
        /// <param name="count">计数</param>
        public void AddSend(byte[] data, int offset, int count)
        {
            IntPtr msg = Marshal.AllocHGlobal(sizeof(int) + count);
            int* p = (int*)msg.ToPointer();
            p[0] = count;//消息首写一个包长度
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
            if (sendBuff == null || sendBuffOffset + sendCount > sendBuff.Length)
            {
                throw new Exception("WriteSendDataToBuffer():sendBuff == null || sendBuffOffset + sendCount > sendBuff.Length");
            }
            if (_queueSendData.Count == 0)
            {
                return 0;
            }
            int copyedLength = 0;
            _queueSendData.LockEnter();
            while (true)
            {
                if (_queueSendData.Count == 0)
                {
                    break;
                }
                if (sendCount == 0)
                {
                    break;
                }
                IntPtr ipMsgItem = _queueSendData._queue.Peek();
                MsgItem* msgItem = (MsgItem*)ipMsgItem.ToPointer();//得到当前的消息
                int copyLen = msgItem->length - msgItem->index;
                if (copyLen < sendCount)//这一整条消息都能写下
                {
                    //IntPtr p = Marshal.UnsafeAddrOfPinnedArrayElement(sendBuff, sendBuffOffset);
                    Marshal.Copy(msgItem->getCurCopyPtr(), sendBuff, sendBuffOffset, copyLen);
                    _queueSendData.Dequeue();//退出这一条
                    Marshal.FreeHGlobal(msgItem->msg);//释放这条
                    Marshal.FreeHGlobal(ipMsgItem);
                    copyedLength += copyLen;
                    sendBuffOffset += copyLen;
                    sendCount -= copyLen;
                }
                else//如果这一条消息整个sendBuff都写不下
                {
                    copyLen = sendCount;//那么使用最大长度为它的要发送的长度
                    Marshal.Copy(msgItem->getCurCopyPtr(), sendBuff, sendBuffOffset, copyLen);
                    msgItem->index += copyLen;
                    copyedLength += copyLen;
                    break;//那么也可以直接退出了
                }
            }
            _queueSendData.LockExit();

            return copyedLength;
        }

        /// <summary>
        /// 当前的待发送数据长度.
        /// </summary>
        public int SendDataLength { get; }

        /// <summary>
        /// 当前待发消息条数，程序会使用这个来判断当前是否还有未发送的数据
        /// </summary>
        public int SendMsgCount { get { return _queueSendData.Count; } }

        /// <summary>
        /// 底层接收buffer将当前这次接收到的数据写入进来.
        /// </summary>
        /// <param name="receBuff">接收buffer</param>
        /// <param name="offset">接收buffer的offset</param>
        /// <param name="count">数据长度</param>
        /// <returns>当次接收到的数据条数</returns>
        public int AddRece(byte[] receBuff, int offset, int count)
        {
            return 0;
        }

        /// <summary>
        /// 当前保存的接收消息的长度，用于传递给用户查询当前消息条数.
        /// </summary>
        public int ReceMsgCount { get; }

        /// <summary>
        /// 得到一条接收的消息，用于传递给用户.
        /// </summary>
        /// <returns>一条消息</returns>
        public ByteBuffer GetReceMsg()
        {
            return null;
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
            return 0;
        }

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
                unsafe
                {
                    byte* p = (byte*)msg.ToPointer();
                    return new IntPtr(&p[index]);
                }
            }
        }
    }
}