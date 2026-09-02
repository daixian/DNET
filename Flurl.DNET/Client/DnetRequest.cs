using System;
using System.Threading;
using System.Threading.Tasks;
using DNET;

namespace Flurl.DNET
{
    /// <summary>
    /// 表示一个不可变的DNET请求构建器。
    /// 每次WithTimeout都会返回新实例，因此同一个基础请求可以安全地派生不同超时配置。
    /// </summary>
    public sealed class DnetRequest
    {
        /// <summary>
        /// 执行请求的Flurl.DNET客户端。
        /// </summary>
        private readonly FlurlDnetClient _client;

        /// <summary>
        /// 初始化请求构建器。
        /// </summary>
        /// <param name="client">执行请求的客户端。</param>
        /// <param name="eventType">请求的事件类型。</param>
        /// <param name="timeout">请求的超时时间。</param>
        internal DnetRequest(FlurlDnetClient client, int eventType, TimeSpan timeout)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (eventType == DnetFrameworkEventTypes.Error) {
                throw new ArgumentOutOfRangeException(nameof(eventType),
                    "该事件类型由Flurl.DNET保留用于远端错误响应。");
            }
            ValidateTimeout(timeout, nameof(timeout));
            EventType = eventType;
            Timeout = timeout;
        }

        /// <summary>
        /// 请求的事件类型。
        /// </summary>
        public int EventType { get; }

        /// <summary>
        /// 请求的超时时间。
        /// </summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        /// 使用指定秒数派生一个新的请求构建器。
        /// </summary>
        /// <param name="seconds">大于零的超时秒数。</param>
        /// <returns>带有新超时设置的请求构建器。</returns>
        public DnetRequest WithTimeout(int seconds)
        {
            if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            return WithTimeout(TimeSpan.FromSeconds(seconds));
        }

        /// <summary>
        /// 使用指定时间派生一个新的请求构建器。
        /// </summary>
        /// <param name="timeout">大于零且不超过系统计时器上限的超时时间。</param>
        /// <returns>带有新超时设置的请求构建器。</returns>
        public DnetRequest WithTimeout(TimeSpan timeout)
        {
            ValidateTimeout(timeout, nameof(timeout));
            return new DnetRequest(_client, EventType, timeout);
        }

        /// <summary>
        /// 发送整个字节数组并等待匹配事务ID的响应。
        /// </summary>
        /// <param name="data">要发送的数据；null表示空正文。</param>
        /// <param name="format">消息格式。</param>
        /// <param name="cancellationToken">调用方取消标记。</param>
        /// <returns>最终匹配到的响应任务。</returns>
        public Task<DnetResponse> SendAsync(byte[] data,
            Format format = Format.Raw,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            int count = data == null ? 0 : data.Length;
            return SendAsync(data, 0, count, format, cancellationToken);
        }

        /// <summary>
        /// 发送字节数组中的指定区间并等待匹配事务ID的响应。
        /// </summary>
        /// <param name="data">要发送的数据；正文长度为零时可以为null。</param>
        /// <param name="offset">数据起始位置。</param>
        /// <param name="count">数据长度。</param>
        /// <param name="format">消息格式。</param>
        /// <param name="cancellationToken">调用方取消标记。</param>
        /// <returns>最终匹配到的响应任务。</returns>
        public Task<DnetResponse> SendAsync(byte[] data,
            int offset,
            int count,
            Format format = Format.Raw,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ValidateBufferRange(data, offset, count);
            return _client.SendAsync(data, offset, count, format, EventType, Timeout, cancellationToken);
        }

        /// <summary>
        /// 发送UTF-8文本并等待匹配事务ID的响应。
        /// </summary>
        /// <param name="text">要发送的文本；null或空字符串表示空正文。</param>
        /// <param name="cancellationToken">调用方取消标记。</param>
        /// <returns>最终匹配到的响应任务。</returns>
        public Task<DnetResponse> SendStringAsync(string text,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _client.SendStringAsync(text, EventType, Timeout, cancellationToken);
        }

        /// <summary>
        /// 使用客户端的Newtonsoft.Json设置序列化对象并等待匹配事务ID的响应。
        /// </summary>
        /// <typeparam name="T">请求对象类型。</typeparam>
        /// <param name="value">要序列化的对象。</param>
        /// <param name="cancellationToken">调用方取消标记。</param>
        /// <returns>最终匹配到的响应任务。</returns>
        public Task<DnetResponse> SendJsonAsync<T>(T value,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _client.SendJsonAsync(value, EventType, Timeout, cancellationToken);
        }

        /// <summary>
        /// 校验字节数组区间，确保传给DNET的参数不会越界。
        /// </summary>
        /// <param name="data">数据数组。</param>
        /// <param name="offset">数据起始位置。</param>
        /// <param name="count">数据长度。</param>
        private static void ValidateBufferRange(byte[] data, int offset, int count)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (data == null) {
                if (offset != 0 || count != 0) {
                    throw new ArgumentException("数据为null时offset和count必须同时为零。", nameof(data));
                }
                return;
            }
            if (offset > data.Length - count) {
                throw new ArgumentException("offset和count超出了数据数组范围。", nameof(data));
            }
        }

        /// <summary>
        /// 校验超时时间是否可以安全交给.NET计时器。
        /// </summary>
        /// <param name="timeout">要校验的超时时间。</param>
        /// <param name="parameterName">异常中使用的参数名。</param>
        internal static void ValidateTimeout(TimeSpan timeout, string parameterName)
        {
            const double maximumTimerMilliseconds = uint.MaxValue - 1D;
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > maximumTimerMilliseconds) {
                throw new ArgumentOutOfRangeException(parameterName,
                    $"超时时间必须大于零且不超过{maximumTimerMilliseconds}毫秒。");
            }
        }
    }
}
