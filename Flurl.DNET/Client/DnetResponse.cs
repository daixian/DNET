using System;
using System.Text;
using System.Threading;
using DNET;
using Newtonsoft.Json;

namespace Flurl.DNET
{
    /// <summary>
    /// 表示一条已经与请求事务ID匹配的DNET响应。
    /// 响应直接持有DNET对象池中的Message，在释放前可以零复制访问正文；使用完毕后必须调用Dispose归还消息。
    /// </summary>
    public sealed class DnetResponse : IDisposable
    {
        /// <summary>
        /// 用于表示空正文的共享空数组。
        /// </summary>
        private static readonly byte[] _emptyBody = new byte[0];

        /// <summary>
        /// 当前响应持有的池化消息；Dispose后通过原子交换变为null。
        /// </summary>
        private Message _message;

        /// <summary>
        /// 当前客户端使用的JSON序列化设置。
        /// </summary>
        private readonly JsonSerializerSettings _jsonSettings;

        /// <summary>
        /// 使用池化消息和JSON设置初始化响应。
        /// </summary>
        /// <param name="message">已经从DNET接收队列取得所有权的消息。</param>
        /// <param name="jsonSettings">当前客户端使用的JSON序列化设置。</param>
        internal DnetResponse(Message message, JsonSerializerSettings jsonSettings)
        {
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _jsonSettings = jsonSettings ?? throw new ArgumentNullException(nameof(jsonSettings));
        }

        /// <summary>
        /// 响应的事务ID。
        /// </summary>
        public int TxrId => GetMessage().TxrId;

        /// <summary>
        /// 响应的事件类型。
        /// </summary>
        public int EventType => GetMessage().EventType;

        /// <summary>
        /// 响应的数据格式。
        /// </summary>
        public Format Format => GetMessage().Format;

        /// <summary>
        /// 响应正文的有效字节数。
        /// </summary>
        public int Length => GetMessage().Length;

        /// <summary>
        /// 获取响应正文的零复制视图。返回的数组段仅在当前响应释放前有效。
        /// </summary>
        public ArraySegment<byte> Body {
            get {
                Message message = GetMessage();
                if (message.data == null || message.data.Length == 0) {
                    return new ArraySegment<byte>(_emptyBody);
                }
                return new ArraySegment<byte>(message.data.Bytes, 0, message.data.Length);
            }
        }

        /// <summary>
        /// 将响应正文复制到一个可在响应释放后继续使用的新数组。
        /// </summary>
        /// <returns>响应正文的独立字节数组。</returns>
        public byte[] ToByteArray()
        {
            ArraySegment<byte> body = Body;
            if (body.Count == 0) {
                return _emptyBody;
            }

            var result = new byte[body.Count];
            Buffer.BlockCopy(body.Array, body.Offset, result, 0, body.Count);
            return result;
        }

        /// <summary>
        /// 按UTF-8读取Text或Json格式的响应正文。
        /// </summary>
        /// <returns>UTF-8解码后的字符串。</returns>
        /// <exception cref="FlurlDnetResponseFormatException">响应不是Text或Json格式时抛出。</exception>
        public string ReadAsString()
        {
            Format format = Format;
            if (format != Format.Text && format != Format.Json) {
                throw new FlurlDnetResponseFormatException(TxrId,
                    EventType,
                    format,
                    $"{Format.Text}或{Format.Json}");
            }

            ArraySegment<byte> body = Body;
            return body.Count == 0
                ? string.Empty
                : Encoding.UTF8.GetString(body.Array, body.Offset, body.Count);
        }

        /// <summary>
        /// 按UTF-8读取Text或Json格式正文，并使用当前客户端的Newtonsoft.Json设置反序列化。
        /// </summary>
        /// <typeparam name="T">目标对象类型。</typeparam>
        /// <returns>反序列化得到的对象。</returns>
        /// <exception cref="FlurlDnetResponseFormatException">响应不是Text或Json格式时抛出。</exception>
        public T ReadAsJson<T>()
        {
            return JsonConvert.DeserializeObject<T>(ReadAsString(), _jsonSettings);
        }

        /// <summary>
        /// 归还当前响应持有的池化Message。重复调用不会重复回收。
        /// </summary>
        public void Dispose()
        {
            Message message = Interlocked.Exchange(ref _message, null);
            message?.Recycle();
        }

        /// <summary>
        /// 获取尚未释放的池化消息。
        /// </summary>
        /// <returns>当前持有的消息。</returns>
        /// <exception cref="ObjectDisposedException">响应已经释放时抛出。</exception>
        private Message GetMessage()
        {
            Message message = Volatile.Read(ref _message);
            if (message == null) {
                throw new ObjectDisposedException(nameof(DnetResponse));
            }
            return message;
        }
    }
}
