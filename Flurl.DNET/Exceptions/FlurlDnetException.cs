using System;

namespace Flurl.DNET
{
    /// <summary>
    /// Flurl.DNET在连接、发送、等待响应或解析响应过程中产生的异常基类。
    /// 异常携带事务ID和事件类型时，可以用它们定位到对应的协议请求。
    /// </summary>
    public class FlurlDnetException : Exception
    {
        /// <summary>
        /// 初始化一个不绑定具体请求的Flurl.DNET异常。
        /// </summary>
        /// <param name="message">异常说明。</param>
        public FlurlDnetException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化一个绑定到具体请求的Flurl.DNET异常。
        /// </summary>
        /// <param name="message">异常说明。</param>
        /// <param name="txrId">事务ID；不属于具体请求时为null。</param>
        /// <param name="eventType">事件类型；不属于具体请求时为null。</param>
        /// <param name="innerException">导致当前异常的内部异常。</param>
        public FlurlDnetException(string message,
            int? txrId,
            int? eventType,
            Exception innerException = null)
            : base(message, innerException)
        {
            TxrId = txrId;
            EventType = eventType;
        }

        /// <summary>
        /// 当前异常关联的事务ID；不属于具体请求时为null。
        /// </summary>
        public int? TxrId { get; }

        /// <summary>
        /// 当前异常关联的事件类型；不属于具体请求时为null。
        /// </summary>
        public int? EventType { get; }
    }
}
