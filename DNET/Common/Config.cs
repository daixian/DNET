using System;
using System.IO;

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
        public static bool isDebugLog = false;

        /// <summary>
        /// 自动心跳包。
        /// 只有当它为true的时候，客户端才会在timer中自动发送，服务器才会在timer中检查离线。
        /// </summary>
        public static bool IsAutoHeartbeat = true;

        /// <summary>
        /// 自动心跳包的包数据内容，目前规定假如发送的数据为1个字节，然后这1个字节为0
        /// </summary>
        public static byte[] HeartBeatData = new byte[1] { 0 };

        /// <summary>
        /// 判断一个接收到的数据是不是心跳包
        /// </summary>
        /// <param name="data">要比较的接收到的数据</param>
        /// <returns></returns>
        public static bool CompareHeartBeat(byte[] data)
        {
            if (data == null) {
                return false;
            }
            if (data.Length != HeartBeatData.Length) {
                return false;
            }
            else {
                for (int i = 0; i < HeartBeatData.Length; i++) {
                    if (HeartBeatData[i] != data[i]) {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// 心跳包的发送间隔时间（ms）,目前默认是5秒
        /// </summary>
        public static long HeartBeatSendTime = 1000 * 5; //20s

        /// <summary>
        /// 心跳包的检查间隔时间（ms）,目前默认是15秒
        /// </summary>
        public static long HeartBeatCheckTime = 1000 * 15 * 1; // 1min
    }
}
