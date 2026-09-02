using System;
using DNET;

namespace Flurl.DNET
{
    /// <summary>
    /// 表示DNET连接建立失败、连接中断或调用方主动断开导致请求无法继续。
    /// </summary>
    public sealed class FlurlDnetConnectionException : FlurlDnetException
    {
        /// <summary>
        /// 初始化连接异常。
        /// </summary>
        /// <param name="message">异常说明。</param>
        /// <param name="errorType">DNET报告的错误类型；主动断开等场景为null。</param>
        /// <param name="innerException">导致当前连接异常的内部异常。</param>
        public FlurlDnetConnectionException(string message,
            ErrorType? errorType = null,
            Exception innerException = null)
            : base(message, null, null, innerException)
        {
            ErrorType = errorType;
        }

        /// <summary>
        /// DNET报告的错误类型；主动断开等场景为null。
        /// </summary>
        public ErrorType? ErrorType { get; }
    }
}
