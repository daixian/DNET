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

            // 速度统计字段
            _lastSendTickTime = LastMsgSendTickTime;
            _lastSendBytesCount = 0;
            _lastReceiveTickTime = LastMsgReceTickTime;
            _lastReceiveBytesCount = 0;

            SendBytesPerSecond = 0;
            ReceiveBytesPerSecond = 0;
        }

        /// <summary>
        /// 记录发送消息
        /// </summary>
        /// <param name="msgCount">消息数量</param>
        /// <param name="byteCount">字节数量</param>
        public void RecordSentMessage(int msgCount, int byteCount)
        {
            SendMessageCount += msgCount;
            SendBytesCount += byteCount;

            LastMsgSendTickTime = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// 记录接收消息
        /// </summary>
        /// <param name="msgCount">消息数量</param>
        /// <param name="byteCount">字节数量</param>
        public void RecordReceivedMessage(int msgCount, int byteCount)
        {
            ReceiveMessageCount += msgCount;
            ReceiveBytesCount += byteCount;

            LastMsgReceTickTime = Stopwatch.GetTimestamp();
        }

        #region 速度统计

        /// <summary>
        /// 上一次统计时的发送字节数
        /// </summary>
        private long _lastSendBytesCount;

        /// <summary>
        /// 上一次统计时的发送时间戳
        /// </summary>
        private long _lastSendTickTime;

        /// <summary>
        /// 上一次统计时的接收字节数
        /// </summary>
        private long _lastReceiveBytesCount;

        /// <summary>
        /// 上一次统计时的接收时间戳
        /// </summary>
        private long _lastReceiveTickTime;

        /// <summary>
        /// 发送速度,字节/秒
        /// </summary>
        public double SendBytesPerSecond { get; private set; }

        /// <summary>
        /// 接收速度,字节/秒
        /// </summary>
        public double ReceiveBytesPerSecond { get; private set; }

        /// <summary>
        /// 发送速度的文本
        /// </summary>
        public string SendBytesPerSecondText => FormatSpeed(SendBytesPerSecond);

        /// <summary>
        /// 接收速度的文本
        /// </summary>
        public string ReceiveBytesPerSecondText => FormatSpeed(ReceiveBytesPerSecond);

        #endregion

        /// <summary>
        /// 统计信息,从上一次调用 UpdateStatus() 到这一次之间的平均字节/秒速率
        /// </summary>
        public void UpdateStatus()
        {
            long now = Stopwatch.GetTimestamp();

            // 发送速率
            long sendBytesDelta = SendBytesCount - _lastSendBytesCount;
            double sendTimeDelta = (now - _lastSendTickTime) / (double)Stopwatch.Frequency;
            SendBytesPerSecond = sendTimeDelta > 0 ? sendBytesDelta / sendTimeDelta : 0;

            _lastSendBytesCount = SendBytesCount;
            _lastSendTickTime = now;

            // 接收速率
            long recvBytesDelta = ReceiveBytesCount - _lastReceiveBytesCount;
            double recvTimeDelta = (now - _lastReceiveTickTime) / (double)Stopwatch.Frequency;
            ReceiveBytesPerSecond = recvTimeDelta > 0 ? recvBytesDelta / recvTimeDelta : 0;

            _lastReceiveBytesCount = ReceiveBytesCount;
            _lastReceiveTickTime = now;
        }

        /// <summary>
        /// 字节每秒发送速率
        /// </summary>
        /// <param name="bps">字节/秒</param>
        /// <returns>格式化后的速率字符串</returns>
        public static string FormatSpeed(double bps)
        {
            if (bps > 1024 * 1024) return $"{bps / (1024 * 1024):F2} MB/s";
            if (bps > 1024) return $"{bps / 1024:F2} KB/s";
            return $"{bps:F2} B/s";
        }
    }
}
