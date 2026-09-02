namespace Flurl.DNET
{
    /// <summary>
    /// 定义Flurl.DNET服务端控制器框架返回的标准错误码。
    /// 错误码使用稳定字符串表达，便于不同版本客户端按名称判断并保持向后兼容。
    /// </summary>
    public static class DnetRemoteErrorCodes
    {
        /// <summary>
        /// 请求事件类型没有注册对应控制器Action。
        /// </summary>
        public const string RouteNotFound = "RouteNotFound";

        /// <summary>
        /// 请求消息格式不是控制器要求的JSON格式。
        /// </summary>
        public const string InvalidFormat = "InvalidFormat";

        /// <summary>
        /// 请求正文无法反序列化为Action声明的请求类型。
        /// </summary>
        public const string InvalidPayload = "InvalidPayload";

        /// <summary>
        /// 控制器Action执行失败或响应序列化失败。
        /// </summary>
        public const string HandlerFailed = "HandlerFailed";

        /// <summary>
        /// 服务端待处理请求数量已经达到配置上限。
        /// </summary>
        public const string ServerBusy = "ServerBusy";

        /// <summary>
        /// 服务端正在停止，不能再接受新的控制器请求。
        /// </summary>
        public const string ServerStopping = "ServerStopping";

        /// <summary>
        /// 客户端收到的框架错误响应本身不是有效的标准错误载荷。
        /// </summary>
        public const string InvalidErrorResponse = "InvalidErrorResponse";
    }
}
