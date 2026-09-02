using System;

namespace Flurl.DNET
{
    /// <summary>
    /// 表示请求在指定超时时间内没有收到匹配事务ID的响应。
    /// </summary>
    public sealed class FlurlDnetTimeoutException : FlurlDnetException
    {
        /// <summary>
        /// 初始化请求超时异常。
        /// </summary>
        /// <param name="txrId">请求的事务ID。</param>
        /// <param name="eventType">请求的事件类型。</param>
        /// <param name="timeout">本次请求使用的超时时间。</param>
        public FlurlDnetTimeoutException(int txrId, int eventType, TimeSpan timeout)
            : base($"DNET请求等待响应超时：TxrId={txrId}, EventType={eventType}, Timeout={timeout}。",
                txrId,
                eventType)
        {
            Timeout = timeout;
        }

        /// <summary>
        /// 本次请求使用的超时时间。
        /// </summary>
        public TimeSpan Timeout { get; }
    }
}
