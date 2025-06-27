namespace DNET
{
    /// <summary>
    /// 关于通信库的一些配置，目前只有心跳包相关，其它的汇总了一些设置。
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// 是否打印调试型的日志.
        /// </summary>
        public static bool IsDebugLog = false;

        /// <summary>
        /// 自动心跳包。
        /// 只有当它为true的时候，客户端才会在timer中自动发送，服务器才会在timer中检查离线。
        /// </summary>
        public static bool IsAutoHeartbeat = true;

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
