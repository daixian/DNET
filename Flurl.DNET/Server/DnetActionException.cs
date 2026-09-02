using System;

namespace Flurl.DNET
{
    /// <summary>
    /// 表示控制器主动拒绝请求并允许把指定业务错误码与安全消息返回客户端。
    /// 该异常用于预期内业务失败；未预期异常应保持原异常，由框架统一隐藏细节并记录。
    /// </summary>
    public sealed class DnetActionException : Exception
    {
        /// <summary>
        /// 使用业务错误码和安全消息创建Action异常。
        /// </summary>
        /// <param name="errorCode">非空的机器可读业务错误码。</param>
        /// <param name="message">允许传输给客户端的安全消息。</param>
        public DnetActionException(string errorCode, string message)
            : base(message)
        {
            if (string.IsNullOrWhiteSpace(errorCode)) {
                throw new ArgumentException("业务错误码不能为空。", nameof(errorCode));
            }
            ErrorCode = errorCode;
        }

        /// <summary>
        /// 获取要返回客户端的业务错误码。
        /// </summary>
        public string ErrorCode { get; }
    }
}
