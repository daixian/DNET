using System;
using System.Collections.Generic;


namespace DNET
{
    /// <summary>
    /// 关于通信库的一些配置，目前只有心跳包相关，其它的汇总了一些设置。
    /// </summary>
    public static class Config
    {

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
            if (data == null)
            {
                return false;
            }
            if (data.Length != HeartBeatData.Length)
            {
                return false;
            }
            else
            {
                for (int i = 0; i < HeartBeatData.Length; i++)
                {
                    if (HeartBeatData[i] != data[i])
                    {
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
        public static long HeartBeatCheckTime = 1000 * 15 * 1;// 1min


        /// <summary>
        /// 创建日志文件
        /// </summary>
        public static void CreatLogFile()
        {
            LogFile.GetInst().CreatLogFile();

        }

        /// <summary>
        /// 创建日志文件(输入文件夹路径)
        /// </summary>
        /// <param name="folderPath">文件夹路径</param>
        public static void CreatLogFile(string folderPath)
        {
            LogFile.GetInst().CreatLogFile(folderPath);
        }

        /// <summary>
        /// 是否写日志文件
        /// </summary>
        public static bool IsLogFile
        {
            get
            {
                return DxDebug.IsLogFile;
            }
            set
            {
                DxDebug.IsLogFile = value;
            }
        }

        /// <summary>
        /// 是否输出到控制台
        /// </summary>
        public static bool IsLogConsole
        {
            get
            {
                return DxDebug.IsConsole;
            }
            set
            {
                DxDebug.IsConsole = value;
            }
        }

        /// <summary>
        /// 服务器和客户端一起设置一个缓存文件夹
        /// </summary>
        /// <param name="path">缓存文件夹路径</param>
        public static bool SetCacheDir(string path)
        {
            if (!DNClient.GetInstance().SetDirCache(path))
            {
                return false;
            }
            if (!DNServer.GetInstance().SetDirCache(path))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// windows平台下的一套默认设置，输出日志文件，打印到控制台，打开心跳包，Cache目录为根目录.
        /// Config.CreatLogFile();
        /// Config.IsLogFile = true;
        /// Config.IsLogConsole = true;
        /// Config.IsAutoHeartbeat = true;
        /// Config.SetCacheDir(""); 
        ///</summary>
        public static void DefaultConfigOnWindows()
        {
            Config.CreatLogFile();
            Config.IsLogFile = true;
            Config.IsLogConsole = true;
            Config.IsAutoHeartbeat = true;
            Config.SetCacheDir("");
        }

        /// <summary>
        /// windows平台下的一套默认设置，输出日志文件，打印到控制台，打开心跳包，Cache和日志目录为输入目录.
        /// Config.CreatLogFile(folderPath);
        /// Config.IsLogFile = true;
        /// Config.IsLogConsole = true;
        /// Config.IsAutoHeartbeat = true;
        /// Config.SetCacheDir(folderPath); 
        ///</summary>
        public static void DefaultConfigOnWindows(string folderPath)
        {
            Config.CreatLogFile(folderPath);
            Config.IsLogFile = true;
            Config.IsLogConsole = true;
            Config.IsAutoHeartbeat = true;
            Config.SetCacheDir(folderPath);
        }

        /// <summary>
        ///  iOS平台下的一套默认设置，日志和Cache文件夹为同一个文件夹，输出日志文件，不打印到控制台.
        /// </summary>
        /// <param name="path">app的临时文件夹路径</param>
        /// <param name="onEventPrint">u3d需要的打印日志事件</param>
        public static void DefaultConfigOnIOS(string path, Action<DxDebug.LogItem> onEventPrint)
        {

            Config.CreatLogFile(path);
            Config.IsLogFile = true;
            Config.IsLogConsole = false;
            Config.IsAutoHeartbeat = true;
            Config.SetCacheDir(path);
            DxDebug.EventPrint += onEventPrint;
        }
    }
}
