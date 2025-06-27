namespace DNET
{
    /// <summary>
    /// 消息格式
    /// </summary>
    public enum Format
    {
        None = 0,
        Text,
        Json,
        Raw,
        GZIP_, // 这是GZIP类型守门员,在C++部分有一些自动压缩操作
        GZIP_Text,
        GZIP_Json,
        GZIP_Raw,

        /// <summary>
        /// 原始数据,不要对其做任何自动压缩操作.压缩可能没有意义的时候.
        /// </summary>
        OriRaw,

        /// <summary>
        /// 心跳包
        /// </summary>
        Heart = 100
    }
}
