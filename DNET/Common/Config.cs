namespace DNET
{
    /// <summary>
    /// 关于通信库的一些配置，目前只有心跳包相关，其它的汇总了一些设置。
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// 是否是调试模式.这模式输出更多错误信息和开启一些记录，方便调试
        /// </summary>
        public static bool IsDebugMode { get; set; } = false;

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
        /// 心跳包的发送间隔时间（ms）,目前默认是5秒
        /// </summary>
        public static long HeartBeatSendTime = 1000 * 5; //5s

        /// <summary>
        /// 心跳包的检查间隔时间（ms）,目前默认是15秒
        /// </summary>
        public static long HeartBeatCheckTime = 1000 * 15 * 1; // 15s
    }
}
