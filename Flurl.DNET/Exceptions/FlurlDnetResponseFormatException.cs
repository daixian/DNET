using DNET;

namespace Flurl.DNET
{
    /// <summary>
    /// 表示调用的响应读取方式与DNET消息格式不兼容，或当前版本不支持该格式。
    /// </summary>
    public sealed class FlurlDnetResponseFormatException : FlurlDnetException
    {
        /// <summary>
        /// 初始化响应格式异常。
        /// </summary>
        /// <param name="txrId">响应的事务ID。</param>
        /// <param name="eventType">响应的事件类型。</param>
        /// <param name="actualFormat">响应携带的实际格式。</param>
        /// <param name="expectedFormat">当前读取操作支持的格式说明。</param>
        public FlurlDnetResponseFormatException(int txrId,
            int eventType,
            Format actualFormat,
            string expectedFormat)
            : base($"DNET响应格式不受当前读取操作支持：TxrId={txrId}, EventType={eventType}, "
                + $"Actual={actualFormat}, Expected={expectedFormat}。",
                txrId,
                eventType)
        {
            ActualFormat = actualFormat;
            ExpectedFormat = expectedFormat;
        }

        /// <summary>
        /// 响应携带的实际格式。
        /// </summary>
        public Format ActualFormat { get; }

        /// <summary>
        /// 当前读取操作支持的格式说明。
        /// </summary>
        public string ExpectedFormat { get; }
    }
}
