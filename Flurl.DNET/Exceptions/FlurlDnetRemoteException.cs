using System;

namespace Flurl.DNET
{
    /// <summary>
    /// 表示服务端已经匹配请求事务ID，但控制器路由、载荷解析、业务执行或过载检查失败。
    /// 与连接和超时异常不同，该异常证明服务端已经接收到请求并返回了标准错误响应。
    /// </summary>
    public sealed class FlurlDnetRemoteException : FlurlDnetException
    {
        /// <summary>
        /// 使用远端错误信息初始化异常。
        /// </summary>
        /// <param name="errorCode">服务端错误码。</param>
        /// <param name="remoteMessage">服务端返回的安全消息。</param>
        /// <param name="txrId">请求事务ID。</param>
        /// <param name="requestEventType">原始请求事件类型。</param>
        /// <param name="innerException">解析错误响应失败时的内部异常。</param>
        public FlurlDnetRemoteException(string errorCode,
            string remoteMessage,
            int txrId,
            int requestEventType,
            Exception innerException = null)
            : base(CreateMessage(errorCode, remoteMessage, txrId, requestEventType),
                txrId,
                requestEventType,
                innerException)
        {
            ErrorCode = string.IsNullOrWhiteSpace(errorCode)
                ? DnetRemoteErrorCodes.InvalidErrorResponse
                : errorCode;
            RemoteMessage = remoteMessage ?? string.Empty;
        }

        /// <summary>
        /// 获取服务端返回的稳定错误码。
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// 获取服务端返回的安全错误说明。
        /// </summary>
        public string RemoteMessage { get; }

        /// <summary>
        /// 生成包含事务信息的异常说明。
        /// </summary>
        /// <param name="errorCode">服务端错误码。</param>
        /// <param name="remoteMessage">服务端安全消息。</param>
        /// <param name="txrId">请求事务ID。</param>
        /// <param name="requestEventType">原始请求事件类型。</param>
        /// <returns>供异常基类使用的完整说明。</returns>
        private static string CreateMessage(string errorCode,
            string remoteMessage,
            int txrId,
            int requestEventType)
        {
            return $"DNET远端请求失败：Code={errorCode ?? DnetRemoteErrorCodes.InvalidErrorResponse}, "
                + $"TxrId={txrId}, EventType={requestEventType}, Message={remoteMessage ?? string.Empty}";
        }
    }
}
