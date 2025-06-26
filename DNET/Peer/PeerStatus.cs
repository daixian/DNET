using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DNET
{
    /// <summary>
    /// 一个Peer状态
    /// </summary>
    public class PeerStatus
    {
        public int Id { get; set; }

        /// <summary>
        /// 发送消息数
        /// </summary>
        public int SendMessageCount { get; set; }

        /// <summary>
        /// 接收消息数
        /// </summary>
        public int ReceiveMessageCount { get; set; }

        /// <summary>
        /// 发送字节数
        /// </summary>
        public long SendBytesCount { get; set; }

        /// <summary>
        /// 接收字节数
        /// </summary>
        public long ReceiveBytesCount { get; set; }

        /// <summary>
        /// 用来记录最后一次收到这个Token发来的消息时间的Tick,创建这Token对象的时候初始化,自动发送心跳包时用
        /// </summary>
        public long LastMsgReceTickTime { get; private set; }

        /// <summary>
        /// 用来记录最后一次向这个Token发送的消息时间的Tick,创建这Token对象的时候初始化,自动发送心跳包时用
        /// </summary>
        public long LastMsgSendTickTime { get; private set; }

        /// <summary>
        /// 重置
        /// </summary>
        public void Reset()
        {
            SendMessageCount = 0;
            ReceiveMessageCount = 0;
            SendBytesCount = 0;
            ReceiveBytesCount = 0;

            LastMsgSendTickTime = DateTime.Now.Ticks;
            LastMsgReceTickTime = DateTime.Now.Ticks;

        }

        /// <summary>
        /// 记录发送消息
        /// </summary>
        /// <param name="msgCount"></param>
        /// <param name="byteCount"></param>
        public void RecordSentMessage(int msgCount, int byteCount)
        {
            SendMessageCount += msgCount;
            SendBytesCount += byteCount;

            LastMsgSendTickTime = DateTime.Now.Ticks;
        }

        /// <summary>
        /// 记录接收消息
        /// </summary>
        /// <param name="msgCount"></param>
        /// <param name="byteCount"></param>
        public void RecordReceivedMessage(int msgCount, int byteCount)
        {
            ReceiveMessageCount += msgCount;
            ReceiveBytesCount += byteCount;

            LastMsgReceTickTime = DateTime.Now.Ticks;
        }

    }

}
