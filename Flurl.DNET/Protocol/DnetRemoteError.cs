namespace Flurl.DNET
{
    /// <summary>
    /// 表示服务端沿用请求事务ID返回的标准错误正文。
    /// 该类型是协议数据承载对象，因此直接使用稳定的小写公开字段参与JSON序列化。
    /// </summary>
    public sealed class DnetRemoteError
    {
        /// <summary>
        /// 稳定的机器可读错误码。
        /// </summary>
        public string code;

        /// <summary>
        /// 面向调用方的安全错误说明，不包含未处理异常堆栈。
        /// </summary>
        public string message;

        /// <summary>
        /// 产生错误的原始请求事件类型。
        /// </summary>
        public int requestEventType;
    }
}
