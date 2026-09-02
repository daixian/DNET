using System;

namespace Flurl.DNET
{
    /// <summary>
    /// 表示请求未能进入DNET发送队列，通常由客户端不可用、打包失败或发送背压导致。
    /// </summary>
    public sealed class FlurlDnetSendException : FlurlDnetException
    {
        /// <summary>
        /// 初始化发送失败异常。
        /// </summary>
        /// <param name="txrId">请求的事务ID。</param>
        /// <param name="eventType">请求的事件类型。</param>
        /// <param name="innerException">发送入口抛出的内部异常。</param>
        public FlurlDnetSendException(int txrId,
            int eventType,
            Exception innerException = null)
            : base($"DNET请求未能进入发送队列：TxrId={txrId}, EventType={eventType}。",
                txrId,
                eventType,
                innerException)
        {
        }
    }
}
