namespace DNET
{
    /// <summary>
    /// 关于通信库的全局配置，包括诊断、心跳和单连接发送泵参数。
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// 是否是调试模式。该开关支持在运行期间跨线程动态调整。
        /// 它只控制受其保护的高频调试日志，不改变心跳、收发或连接状态机的执行。
        /// </summary>
        public static bool IsDebugMode {
            get => System.Threading.Volatile.Read(ref _isDebugMode);
            set => System.Threading.Volatile.Write(ref _isDebugMode, value);
        }

        /// <summary>
        /// 调试模式的跨线程共享状态，由IsDebugMode通过Volatile读写保证动态切换的可见性。
        /// </summary>
        private static bool _isDebugMode;

        /// <summary>
        /// 是否开启对Peer的统计信息
        /// </summary>
        public static bool EnablePeerStatistics { get; set; } = true;

        /// <summary>
        /// 是否开启对服务器往返延迟的RTT的统计信息(需要发送带有事务ID的消息)
        /// </summary>
        public static bool EnableRttStatistics { get; set; } = true;

        /// <summary>
        /// 自动心跳包。
        /// 只有当它为true的时候，客户端才会在timer中自动发送，服务器才会在timer中检查离线。
        /// </summary>
        public static bool IsAutoHeartbeat { get; set; } = true;

        /// <summary>
        /// 是否对TCP连接启用NoDelay。默认关闭Nagle算法，降低小消息请求响应的延迟。
        /// </summary>
        public static bool EnableTcpNoDelay { get; set; } = true;

        /// <summary>
        /// 单个Peer允许保留的待发送总字节数，包含正在发送的批次。
        /// 小于等于0表示不限制；默认32MB，可容纳一个协议允许的最大消息及后续排队数据。
        /// </summary>
        public static long MaxPendingSendBytesPerPeer { get; set; } = 32L * 1024 * 1024;

        /// <summary>
        /// 单次Socket发送批次建议合并的最大字节数。
        /// 单条消息超过此值时仍会独立发送，不会拆分协议消息。
        /// </summary>
        public static int MaxSendBatchBytes { get; set; } = 64 * 1024;

        /// <summary>
        /// 单次Socket发送批次最多合并的消息数量，防止热点连接长时间占用发送泵。
        /// </summary>
        public static int MaxSendBatchMessages { get; set; } = 256;

        /// <summary>
        /// 一个线程连续处理Socket同步发送完成的最大次数。
        /// 达到预算后会把发送泵续体投递到线程池，防止本地高速连接长期占用业务线程。
        /// </summary>
        public static int MaxSynchronousSendCompletions { get; set; } = 32;

        /// <summary>
        /// 心跳包的发送间隔时间（ms）,目前默认是5秒
        /// </summary>
        public static long HeartBeatSendTime = 1000 * 5; //5s

        /// <summary>
        /// 心跳包的检查间隔时间（ms）,目前默认是15秒
        /// </summary>
        public static long HeartBeatCheckTime = 1000 * 15 * 1; // 15s
    }
}
