using System;

namespace DNET
{
    /// <summary>
    /// 日志代理类，用于对接不同日志框架的日志输出方法。
    /// 提供了 Debug、Info、Warning、Error 四种级别的日志输出功能。
    /// </summary>
    public static class LogProxy
    {
        /// <summary>
        /// 信息级别日志的回调委托。
        /// </summary>
        public static Action<string> Info;

        /// <summary>
        /// 警告级别日志的回调委托。
        /// </summary>
        public static Action<string> Warning;

        /// <summary>
        /// 错误级别日志的回调委托。
        /// </summary>
        public static Action<string> Error;

        /// <summary>
        /// 调试级别日志的回调委托。
        /// </summary>
        public static Action<string> Debug;

        /// <summary>
        /// 简单的全部输出到控制台
        /// </summary>
        public static void SetupLogToConsole()
        {
            Info = s => { Console.WriteLine($"[INFO] {s}"); };
            Warning = s => { Console.WriteLine($"[WARN] {s}"); };
            Error = s => { Console.WriteLine($"[ERROR] {s}"); };
            Debug = s => { Console.WriteLine($"[DEBUG] {s}"); };
        }
    }
}
