using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DNET;
using Newtonsoft.Json;

namespace Flurl.DNET
{
    /// <summary>
    /// 独占消费DNClient接收队列，并以事务ID在常数时间内匹配并发请求。
    /// 调度器只在短暂的接收回调内转移Message所有权，不在Socket线程执行用户continuation或未匹配事件。
    /// </summary>
    internal sealed class DnetRequestDispatcher : IDisposable
    {
        /// <summary>
        /// 调度器独占使用的底层客户端。
        /// </summary>
        private readonly DNClient _client;

        /// <summary>
        /// 当前客户端的JSON序列化设置，用于构造响应。
        /// </summary>
        private readonly JsonSerializerSettings _jsonSettings;

        /// <summary>
        /// 无法匹配等待请求时接收响应所有权的回调。
        /// </summary>
        private readonly Action<DnetResponse> _unmatchedResponseHandler;

        /// <summary>
        /// 以事务ID为键的并发等待表。
        /// </summary>
        private readonly ConcurrentDictionary<int, DnetPendingRequest> _pendingRequests =
            new ConcurrentDictionary<int, DnetPendingRequest>();

        /// <summary>
        /// 下一个事务ID的原子生成状态；溢出后通过掩码重新进入正数区间。
        /// </summary>
        private int _nextTxrId;

        /// <summary>
        /// 标记调度器是否已经释放。
        /// </summary>
        private int _disposed;

        /// <summary>
        /// 初始化请求调度器。
        /// </summary>
        /// <param name="client">由Flurl.DNET独占的底层客户端。</param>
        /// <param name="jsonSettings">客户端JSON设置。</param>
        /// <param name="unmatchedResponseHandler">未匹配响应处理回调。</param>
        internal DnetRequestDispatcher(DNClient client,
            JsonSerializerSettings jsonSettings,
            Action<DnetResponse> unmatchedResponseHandler)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _jsonSettings = jsonSettings ?? throw new ArgumentNullException(nameof(jsonSettings));
            _unmatchedResponseHandler = unmatchedResponseHandler
                ?? throw new ArgumentNullException(nameof(unmatchedResponseHandler));
        }

        #region 请求发送

        /// <summary>
        /// 登记请求、启动超时与取消监听，然后尝试将消息送入DNET发送队列。
        /// </summary>
        /// <param name="data">请求正文。</param>
        /// <param name="offset">正文起始位置。</param>
        /// <param name="count">正文长度。</param>
        /// <param name="format">请求格式。</param>
        /// <param name="eventType">请求事件类型。</param>
        /// <param name="timeout">请求超时时间。</param>
        /// <param name="cancellationToken">调用方取消标记。</param>
        /// <returns>最终匹配到响应、异常或取消状态的任务。</returns>
        internal Task<DnetResponse> SendAsync(byte[] data,
            int offset,
            int count,
            Format format,
            int eventType,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return SendCoreAsync(eventType,
                timeout,
                cancellationToken,
                txrId => _client.TrySend(data,
                    offset,
                    count,
                    format,
                    txrId,
                    eventType,
                    true));
        }

        /// <summary>
        /// 登记文本请求，并使用DNET的池化UTF-8编码路径将消息送入发送队列。
        /// </summary>
        /// <param name="text">请求文本。</param>
        /// <param name="format">请求格式。</param>
        /// <param name="eventType">请求事件类型。</param>
        /// <param name="timeout">请求超时时间。</param>
        /// <param name="cancellationToken">调用方取消标记。</param>
        /// <returns>最终匹配到响应、异常或取消状态的任务。</returns>
        internal Task<DnetResponse> SendStringAsync(string text,
            Format format,
            int eventType,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return SendCoreAsync(eventType,
                timeout,
                cancellationToken,
                txrId => _client.TrySend(text, format, txrId, eventType, true));
        }

        /// <summary>
        /// 执行所有请求类型共享的登记、竞态检查、发送和失败完成流程。
        /// </summary>
        /// <param name="eventType">请求事件类型。</param>
        /// <param name="timeout">请求超时时间。</param>
        /// <param name="cancellationToken">调用方取消标记。</param>
        /// <param name="trySend">使用已分配事务ID尝试发送消息的回调。</param>
        /// <returns>最终匹配到响应、异常或取消状态的任务。</returns>
        private Task<DnetResponse> SendCoreAsync(int eventType,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            Func<int, bool> trySend)
        {
            if (trySend == null) throw new ArgumentNullException(nameof(trySend));
            if (Volatile.Read(ref _disposed) != 0) {
                return CreateFaultedTask(new ObjectDisposedException(nameof(DnetRequestDispatcher)));
            }
            if (cancellationToken.IsCancellationRequested) {
                return CreateCanceledTask();
            }
            if (!_client.IsConnected) {
                return CreateFaultedTask(
                    new FlurlDnetConnectionException("DNET客户端尚未连接，无法发送请求。"));
            }

            DnetPendingRequest pendingRequest = ReservePendingRequest(eventType, timeout);
            if (Volatile.Read(ref _disposed) != 0) {
                CompleteWithException(pendingRequest,
                    new ObjectDisposedException(nameof(DnetRequestDispatcher)));
                return pendingRequest.Task;
            }
            try {
                pendingRequest.Start(cancellationToken);
            } catch (Exception exception) {
                CompleteWithException(pendingRequest, exception);
                return pendingRequest.Task;
            }

            if (pendingRequest.IsCompleted) return pendingRequest.Task;

            if (!_client.IsConnected) {
                CompleteWithException(pendingRequest,
                    new FlurlDnetConnectionException("DNET连接已在请求发送前中断。"));
                return pendingRequest.Task;
            }

            bool queued;
            try {
                queued = trySend(pendingRequest.TxrId);
            } catch (Exception exception) {
                CompleteWithException(pendingRequest,
                    new FlurlDnetSendException(pendingRequest.TxrId,
                        eventType,
                        exception));
                return pendingRequest.Task;
            }
            if (!queued) {
                CompleteWithException(pendingRequest,
                    new FlurlDnetSendException(pendingRequest.TxrId, eventType));
            }
            return pendingRequest.Task;
        }

        #endregion

        #region 响应分发与批量失败

        /// <summary>
        /// 独占提取DNClient当前接收队列，并把每条消息转交匹配请求或未匹配消息处理器。
        /// </summary>
        internal void HandleReceived()
        {
            List<Message> messages = _client.GetReceiveData();
            if (messages == null) return;

            try {
                for (int i = 0; i < messages.Count; i++) {
                    Message message = messages[i];
                    messages[i] = null;
                    if (message == null) continue;

                    if (Volatile.Read(ref _disposed) != 0) {
                        message.Recycle();
                        continue;
                    }
                    DispatchMessage(message);
                }
            } finally {
                // 发生意外异常时，只回收尚未转移所有权的消息。
                for (int i = 0; i < messages.Count; i++) {
                    messages[i]?.Recycle();
                }
                ListPool<Message>.Shared.Recycle(messages);
            }
        }

        /// <summary>
        /// 使用给定异常工厂终止当前全部等待请求。
        /// </summary>
        /// <param name="exceptionFactory">根据单个等待请求创建异常的工厂。</param>
        internal void FailAll(Func<DnetPendingRequest, Exception> exceptionFactory)
        {
            if (exceptionFactory == null) throw new ArgumentNullException(nameof(exceptionFactory));

            foreach (KeyValuePair<int, DnetPendingRequest> pair in _pendingRequests) {
                if (((ICollection<KeyValuePair<int, DnetPendingRequest>>)_pendingRequests)
                    .Remove(pair)) {
                    pair.Value.TrySetException(exceptionFactory(pair.Value));
                }
            }
        }

        #endregion

        #region 生命周期与请求登记

        /// <summary>
        /// 释放调度器并终止全部等待请求。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            FailAll(request => new ObjectDisposedException(
                nameof(DnetRequestDispatcher),
                $"请求已因调度器释放而终止：TxrId={request.TxrId}, EventType={request.EventType}。"));
        }

        /// <summary>
        /// 分配一个未使用的正数事务ID，并把请求加入并发等待表。
        /// </summary>
        /// <param name="eventType">请求事件类型。</param>
        /// <param name="timeout">请求超时时间。</param>
        /// <returns>已经成功登记的等待请求。</returns>
        private DnetPendingRequest ReservePendingRequest(int eventType, TimeSpan timeout)
        {
            while (true) {
                int txrId = Interlocked.Increment(ref _nextTxrId) & int.MaxValue;
                if (txrId == 0) continue;

                var pendingRequest = new DnetPendingRequest(txrId,
                    eventType,
                    timeout,
                    OnRequestTimeout,
                    OnRequestCanceled);
                if (_pendingRequests.TryAdd(txrId, pendingRequest)) {
                    return pendingRequest;
                }
            }
        }

        #endregion

        #region 单请求完成

        /// <summary>
        /// 将单条DNET消息转换为响应并按事务ID分发。
        /// </summary>
        /// <param name="message">已经从接收队列取得所有权的消息。</param>
        private void DispatchMessage(Message message)
        {
            var response = new DnetResponse(message, _jsonSettings);
            bool ownershipTransferred = false;
            try {
                if (response.TxrId != 0
                    && _pendingRequests.TryRemove(response.TxrId,
                        out DnetPendingRequest pendingRequest)) {
                    if (response.EventType == DnetFrameworkEventTypes.Error) {
                        FlurlDnetRemoteException remoteException =
                            CreateRemoteException(response, pendingRequest);
                        if (pendingRequest.TrySetException(remoteException)) {
                            response.Dispose();
                            ownershipTransferred = true;
                            return;
                        }
                    } else if (pendingRequest.TrySetResponse(response)) {
                        ownershipTransferred = true;
                        return;
                    }
                }

                _unmatchedResponseHandler(response);
                ownershipTransferred = true;
            } finally {
                if (!ownershipTransferred) {
                    response.Dispose();
                }
            }
        }

        /// <summary>
        /// 解析服务端标准错误响应，并始终以等待请求登记的事件类型作为可信原请求类型。
        /// 错误协议固定使用Newtonsoft默认设置，不受业务JsonSerializerSettings影响。
        /// </summary>
        /// <param name="response">已匹配事务ID的框架错误响应。</param>
        /// <param name="pendingRequest">对应等待请求。</param>
        /// <returns>用于结束等待任务的远端异常。</returns>
        private static FlurlDnetRemoteException CreateRemoteException(DnetResponse response,
            DnetPendingRequest pendingRequest)
        {
            try {
                if (response.Format != Format.Json) {
                    throw new JsonSerializationException(
                        $"框架错误响应必须使用JSON格式，实际为{response.Format}。");
                }
                DnetRemoteError error = JsonConvert.DeserializeObject<DnetRemoteError>(
                    response.ReadAsString());
                if (error == null || string.IsNullOrWhiteSpace(error.code)) {
                    throw new JsonSerializationException("框架错误响应缺少有效错误码。");
                }
                return new FlurlDnetRemoteException(error.code,
                    error.message,
                    pendingRequest.TxrId,
                    pendingRequest.EventType);
            } catch (Exception exception) {
                return new FlurlDnetRemoteException(
                    DnetRemoteErrorCodes.InvalidErrorResponse,
                    "服务端返回了无法解析的框架错误响应。",
                    pendingRequest.TxrId,
                    pendingRequest.EventType,
                    exception);
            }
        }

        /// <summary>
        /// 响应单个请求的超时通知。
        /// </summary>
        /// <param name="pendingRequest">发生超时的等待请求。</param>
        private void OnRequestTimeout(DnetPendingRequest pendingRequest)
        {
            if (!TryRemoveExact(pendingRequest)) return;
            pendingRequest.TrySetException(new FlurlDnetTimeoutException(
                pendingRequest.TxrId,
                pendingRequest.EventType,
                pendingRequest.Timeout));
        }

        /// <summary>
        /// 响应单个请求的调用方取消通知。
        /// </summary>
        /// <param name="pendingRequest">被调用方取消的等待请求。</param>
        private void OnRequestCanceled(DnetPendingRequest pendingRequest)
        {
            if (!TryRemoveExact(pendingRequest)) return;
            pendingRequest.TrySetCanceled();
        }

        /// <summary>
        /// 从等待表移除指定请求，并以异常完成它。
        /// </summary>
        /// <param name="pendingRequest">要完成的等待请求。</param>
        /// <param name="exception">请求失败异常。</param>
        private void CompleteWithException(DnetPendingRequest pendingRequest, Exception exception)
        {
            if (!TryRemoveExact(pendingRequest)) return;
            pendingRequest.TrySetException(exception);
        }

        /// <summary>
        /// 仅当等待表中仍是同一个实例时移除请求，避免事务ID复用时误删后继请求。
        /// </summary>
        /// <param name="pendingRequest">期望移除的请求实例。</param>
        /// <returns>成功移除同一实例时返回true。</returns>
        private bool TryRemoveExact(DnetPendingRequest pendingRequest)
        {
            var pair = new KeyValuePair<int, DnetPendingRequest>(pendingRequest.TxrId,
                pendingRequest);
            return ((ICollection<KeyValuePair<int, DnetPendingRequest>>)_pendingRequests)
                .Remove(pair);
        }

        #endregion

        #region 任务工厂

        /// <summary>
        /// 创建一个已经失败的响应任务，兼容.NET Framework 4.6.2。
        /// </summary>
        /// <param name="exception">任务异常。</param>
        /// <returns>已经失败的任务。</returns>
        private static Task<DnetResponse> CreateFaultedTask(Exception exception)
        {
            var completionSource = new TaskCompletionSource<DnetResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            completionSource.SetException(exception);
            return completionSource.Task;
        }

        /// <summary>
        /// 创建一个已经取消的响应任务，兼容.NET Framework 4.6.2。
        /// </summary>
        /// <returns>已经取消的任务。</returns>
        private static Task<DnetResponse> CreateCanceledTask()
        {
            var completionSource = new TaskCompletionSource<DnetResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            completionSource.SetCanceled();
            return completionSource.Task;
        }

        #endregion
    }
}
