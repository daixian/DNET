namespace DNET
{
    /// <summary>
    /// 消息格式
    /// </summary>
    public enum Format
    {
        /// <summary>
        ///  无
        /// </summary>
        None = 0,

        /// <summary>
        /// 文本
        /// </summary>
        Text,

        /// <summary>
        /// JSON
        /// </summary>
        Json,

        /// <summary>
        /// 原始数据
        /// </summary>
        Raw,

        /// <summary>
        /// 这是GZIP类型守门员,在C++部分有一些自动压缩操作
        /// </summary>
        GZIP_,

        /// <summary>
        /// GZIP压缩的文本
        /// </summary>
        GZIP_Text,

        /// <summary>
        /// GZIP压缩的JSON
        /// </summary>
        GZIP_Json,

        /// <summary>
        /// GZIP压缩的原始数据
        /// </summary>
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
