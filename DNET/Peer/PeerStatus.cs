using System.Diagnostics;

namespace DNET
{
    /// <summary>
    /// 一个Peer状态
    /// </summary>
    public class PeerStatus
    {
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
        private long LastMsgReceTickTime { get; set; }

        /// <summary>
        /// 上一个消息接收到现在的时间(ms)
        /// </summary>
        public double TimeSinceLastReceived {
            get {
                double ms = (Stopwatch.GetTimestamp() - LastMsgReceTickTime) * 1000.0 / Stopwatch.Frequency; // Frequency 是每秒 tick 数
                return ms;
            }
        }

        /// <summary>
        /// 用来记录最后一次向这个Token发送的消息时间的Tick,创建这Token对象的时候初始化,自动发送心跳包时用
        /// </summary>
        private long LastMsgSendTickTime { get; set; }

        /// <summary>
        /// 上一个消息发送到现在的时间(ms)
        /// </summary>
        public double TimeSinceLastSend {
            get {
                double ms = (Stopwatch.GetTimestamp() - LastMsgSendTickTime) * 1000.0 / Stopwatch.Frequency; // Frequency 是每秒 tick 数
                return ms;
            }
        }

        /// <summary>
        /// 重置
        /// </summary>
        public void Reset()
        {
            SendMessageCount = 0;
            ReceiveMessageCount = 0;
            SendBytesCount = 0;
            ReceiveBytesCount = 0;

            LastMsgSendTickTime = Stopwatch.GetTimestamp();
            LastMsgReceTickTime = Stopwatch.GetTimestamp();
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

            LastMsgSendTickTime = Stopwatch.GetTimestamp();
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

            LastMsgReceTickTime = Stopwatch.GetTimestamp();
        }
    }
}
