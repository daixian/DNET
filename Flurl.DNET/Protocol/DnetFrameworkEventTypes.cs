namespace Flurl.DNET
{
    /// <summary>
    /// 定义Flurl.DNET在DNET消息头中保留使用的框架事件类型。
    /// 业务代码不得注册或主动请求这些事件类型，避免框架控制消息与业务消息发生冲突。
    /// </summary>
    public static class DnetFrameworkEventTypes
    {
        /// <summary>
        /// 标准远端错误响应事件类型。错误响应仍沿用原请求的事务ID。
        /// </summary>
        public const int Error = int.MinValue;
    }
}
