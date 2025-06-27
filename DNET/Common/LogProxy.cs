using System;
using System.Diagnostics;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 日志代理类，用于对接不同日志框架的日志输出方法。
    /// 提供了 Debug、Info、Warning、Error 四种级别的日志输出功能。
    /// </summary>
    public static class LogProxy
    {
        /// <summary>
        /// 输出调试级别日志。
        /// </summary>
        /// <param name="e">要输出的日志内容。</param>
        public static void LogDebug(string e)
        {
            if (actionLogDebug != null)
                actionLogDebug(e); // 调用调试日志回调方法
        }

        /// <summary>
        /// 输出信息级别日志。
        /// </summary>
        /// <param name="e">要输出的日志内容。</param>
        public static void Log(string e)
        {
            if (actionLog != null)
                actionLog(e); // 调用信息日志回调方法
        }

        /// <summary>
        /// 输出警告级别日志。
        /// </summary>
        /// <param name="e">要输出的日志内容。</param>
        public static void LogWarning(string e)
        {
            if (actionLogWarning != null)
                actionLogWarning(e); // 调用警告日志回调方法
        }

        /// <summary>
        /// 输出错误级别日志。
        /// </summary>
        /// <param name="e">要输出的日志内容。</param>
        public static void LogError(string e)
        {
            if (actionLogError != null)
                actionLogError(e); // 调用错误日志回调方法
        }

        /// <summary>
        /// 信息级别日志的回调委托。
        /// </summary>
        public static Action<string> actionLog;

        /// <summary>
        /// 警告级别日志的回调委托。
        /// </summary>
        public static Action<string> actionLogWarning;

        /// <summary>
        /// 错误级别日志的回调委托。
        /// </summary>
        public static Action<string> actionLogError;

        /// <summary>
        /// 调试级别日志的回调委托。
        /// </summary>
        public static Action<string> actionLogDebug;

        /// <summary>
        /// 简单的全部输出到控制台
        /// </summary>
        public static void SetupLogToConsole()
        {
            actionLog = (s) => { Console.WriteLine($"[INFO] {s}"); };
            actionLogWarning = (s) => { Console.WriteLine($"[WARN] {s}"); };
            actionLogError = (s) => { Console.WriteLine($"[ERROR] {s}"); };
            actionLogDebug = (s) => { Console.WriteLine($"[DEBUG] {s}"); };
        }
    }
}
