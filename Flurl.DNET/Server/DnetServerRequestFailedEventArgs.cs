using System;

namespace Flurl.DNET
{
    /// <summary>
    /// 承载控制器请求解析、执行或回复失败的服务端诊断信息。
    /// 事件在Flurl.DNET工作线程上触发，订阅者异常会被框架隔离，不影响后续请求。
    /// </summary>
    public sealed class DnetServerRequestFailedEventArgs : EventArgs
    {
        /// <summary>
        /// 初始化一次服务端请求失败通知。
        /// </summary>
        /// <param name="context">发生失败的请求上下文。</param>
        /// <param name="errorCode">映射后的远端错误码。</param>
        /// <param name="exception">导致失败的真实服务端异常。</param>
        internal DnetServerRequestFailedEventArgs(DnetServerContext context,
            string errorCode,
            Exception exception)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ErrorCode = errorCode;
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        /// <summary>
        /// 获取发生失败的请求上下文。
        /// </summary>
        public DnetServerContext Context { get; }

        /// <summary>
        /// 获取映射到远端协议的错误码。
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// 获取真实服务端异常；该异常不会自动发送给客户端。
        /// </summary>
        public Exception Exception { get; }
    }
}
