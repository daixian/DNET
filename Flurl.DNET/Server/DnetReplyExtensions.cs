using System;
using DNET;
using Newtonsoft.Json;

namespace Flurl.DNET
{
    /// <summary>
    /// 为DNET服务端提供自动回传请求事务ID的回复方法。
    /// 扩展只负责序列化与入队，不负责回收传入的请求Message。
    /// </summary>
    public static class DnetReplyExtensions
    {
        #region Peer回复

        /// <summary>
        /// 通过Peer回复字节正文，并自动沿用请求事务ID。
        /// </summary>
        /// <param name="peer">目标Peer。</param>
        /// <param name="request">正在回复的请求消息。</param>
        /// <param name="data">回复正文；null表示空正文。</param>
        /// <param name="format">回复格式。</param>
        /// <param name="eventType">回复事件类型；null时沿用请求事件类型。</param>
        /// <returns>回复成功进入发送队列时返回true。</returns>
        public static bool TryReply(this Peer peer,
            Message request,
            byte[] data,
            Format format = Format.Raw,
            int? eventType = null)
        {
            int count = data == null ? 0 : data.Length;
            return TryReply(peer, request, data, 0, count, format, eventType);
        }

        /// <summary>
        /// 通过Peer回复字节数组中的指定区间，并自动沿用请求事务ID。
        /// </summary>
        /// <param name="peer">目标Peer。</param>
        /// <param name="request">正在回复的请求消息。</param>
        /// <param name="data">回复正文；正文长度为零时可以为null。</param>
        /// <param name="offset">正文起始位置。</param>
        /// <param name="count">正文长度。</param>
        /// <param name="format">回复格式。</param>
        /// <param name="eventType">回复事件类型；null时沿用请求事件类型。</param>
        /// <returns>回复成功进入发送队列时返回true。</returns>
        public static bool TryReply(this Peer peer,
            Message request,
            byte[] data,
            int offset,
            int count,
            Format format = Format.Raw,
            int? eventType = null)
        {
            if (peer == null) return false;
            ValidateRequest(request);
            ValidateBufferRange(data, offset, count);
            return peer.TrySend(data,
                offset,
                count,
                format,
                request.TxrId,
                ResolveEventType(request, eventType));
        }

        /// <summary>
        /// 通过Peer回复UTF-8文本，并自动沿用请求事务ID。
        /// </summary>
        /// <param name="peer">目标Peer。</param>
        /// <param name="request">正在回复的请求消息。</param>
        /// <param name="text">回复文本；null或空字符串表示空正文。</param>
        /// <param name="eventType">回复事件类型；null时沿用请求事件类型。</param>
        /// <returns>回复成功进入发送队列时返回true。</returns>
        public static bool TryReplyString(this Peer peer,
            Message request,
            string text,
            int? eventType = null)
        {
            if (peer == null) return false;
            ValidateRequest(request);
            return peer.TrySend(text,
                Format.Text,
                request.TxrId,
                ResolveEventType(request, eventType));
        }

        /// <summary>
        /// 通过Peer回复JSON对象，并自动沿用请求事务ID。
        /// </summary>
        /// <typeparam name="T">回复对象类型。</typeparam>
        /// <param name="peer">目标Peer。</param>
        /// <param name="request">正在回复的请求消息。</param>
        /// <param name="value">要序列化的回复对象。</param>
        /// <param name="eventType">回复事件类型；null时沿用请求事件类型。</param>
        /// <param name="jsonSettings">Newtonsoft.Json设置；null时使用默认设置。</param>
        /// <returns>回复成功进入发送队列时返回true。</returns>
        public static bool TryReplyJson<T>(this Peer peer,
            Message request,
            T value,
            int? eventType = null,
            JsonSerializerSettings jsonSettings = null)
        {
            if (peer == null) return false;
            ValidateRequest(request);
            string json = SerializeJson(value, jsonSettings);
            return peer.TrySend(json,
                Format.Json,
                request.TxrId,
                ResolveEventType(request, eventType));
        }

        #endregion

        #region DNServer回复

        /// <summary>
        /// 通过DNServer回复字节正文，并自动沿用请求事务ID。
        /// </summary>
        /// <param name="server">执行回复的服务器。</param>
        /// <param name="peer">目标Peer。</param>
        /// <param name="request">正在回复的请求消息。</param>
        /// <param name="data">回复正文；null表示空正文。</param>
        /// <param name="format">回复格式。</param>
        /// <param name="eventType">回复事件类型；null时沿用请求事件类型。</param>
        /// <returns>回复成功进入发送队列时返回true。</returns>
        public static bool TryReply(this DNServer server,
            Peer peer,
            Message request,
            byte[] data,
            Format format = Format.Raw,
            int? eventType = null)
        {
            int count = data == null ? 0 : data.Length;
            return TryReply(server, peer, request, data, 0, count, format, eventType);
        }

        /// <summary>
        /// 通过DNServer回复字节数组中的指定区间，并自动沿用请求事务ID。
        /// </summary>
        /// <param name="server">执行回复的服务器。</param>
        /// <param name="peer">目标Peer。</param>
        /// <param name="request">正在回复的请求消息。</param>
        /// <param name="data">回复正文；正文长度为零时可以为null。</param>
        /// <param name="offset">正文起始位置。</param>
        /// <param name="count">正文长度。</param>
        /// <param name="format">回复格式。</param>
        /// <param name="eventType">回复事件类型；null时沿用请求事件类型。</param>
        /// <returns>回复成功进入发送队列时返回true。</returns>
        public static bool TryReply(this DNServer server,
            Peer peer,
            Message request,
            byte[] data,
            int offset,
            int count,
            Format format = Format.Raw,
            int? eventType = null)
        {
            if (server == null || peer == null) return false;
            ValidateRequest(request);
            ValidateBufferRange(data, offset, count);
            return server.TrySend(peer,
                data,
                offset,
                count,
                format,
                request.TxrId,
                ResolveEventType(request, eventType),
                true);
        }

        /// <summary>
        /// 通过DNServer回复UTF-8文本，并自动沿用请求事务ID。
        /// </summary>
        /// <param name="server">执行回复的服务器。</param>
        /// <param name="peer">目标Peer。</param>
        /// <param name="request">正在回复的请求消息。</param>
        /// <param name="text">回复文本；null或空字符串表示空正文。</param>
        /// <param name="eventType">回复事件类型；null时沿用请求事件类型。</param>
        /// <returns>回复成功进入发送队列时返回true。</returns>
        public static bool TryReplyString(this DNServer server,
            Peer peer,
            Message request,
            string text,
            int? eventType = null)
        {
            if (server == null || peer == null) return false;
            ValidateRequest(request);
            return server.TrySend(peer,
                text,
                Format.Text,
                request.TxrId,
                ResolveEventType(request, eventType),
                true);
        }

        /// <summary>
        /// 通过DNServer回复JSON对象，并自动沿用请求事务ID。
        /// </summary>
        /// <typeparam name="T">回复对象类型。</typeparam>
        /// <param name="server">执行回复的服务器。</param>
        /// <param name="peer">目标Peer。</param>
        /// <param name="request">正在回复的请求消息。</param>
        /// <param name="value">要序列化的回复对象。</param>
        /// <param name="eventType">回复事件类型；null时沿用请求事件类型。</param>
        /// <param name="jsonSettings">Newtonsoft.Json设置；null时使用默认设置。</param>
        /// <returns>回复成功进入发送队列时返回true。</returns>
        public static bool TryReplyJson<T>(this DNServer server,
            Peer peer,
            Message request,
            T value,
            int? eventType = null,
            JsonSerializerSettings jsonSettings = null)
        {
            if (server == null || peer == null) return false;
            ValidateRequest(request);
            string json = SerializeJson(value, jsonSettings);
            return server.TrySend(peer,
                json,
                Format.Json,
                request.TxrId,
                ResolveEventType(request, eventType),
                true);
        }

        #endregion

        #region 参数与序列化

        /// <summary>
        /// 校验请求消息是否存在。
        /// </summary>
        /// <param name="request">正在回复的请求消息。</param>
        private static void ValidateRequest(Message request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
        }

        /// <summary>
        /// 校验回复正文区间是否有效。
        /// </summary>
        /// <param name="data">正文数组。</param>
        /// <param name="offset">正文起始位置。</param>
        /// <param name="count">正文长度。</param>
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
        /// 决定回复使用的事件类型。
        /// </summary>
        /// <param name="request">正在回复的请求消息。</param>
        /// <param name="eventType">调用方指定的可选事件类型。</param>
        /// <returns>显式事件类型或请求原事件类型。</returns>
        private static int ResolveEventType(Message request, int? eventType)
        {
            return eventType ?? request.EventType;
        }

        /// <summary>
        /// 使用指定或默认Newtonsoft.Json设置序列化回复对象。
        /// </summary>
        /// <typeparam name="T">回复对象类型。</typeparam>
        /// <param name="value">回复对象。</param>
        /// <param name="jsonSettings">可选JSON设置。</param>
        /// <returns>序列化后的JSON文本。</returns>
        private static string SerializeJson<T>(T value, JsonSerializerSettings jsonSettings)
        {
            return jsonSettings == null
                ? JsonConvert.SerializeObject(value)
                : JsonConvert.SerializeObject(value, jsonSettings);
        }

        #endregion
    }
}
