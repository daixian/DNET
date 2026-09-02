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
    /// 独占消费DNServer接收队列，把池化消息快速转移到Peer邮箱，并在工作线程完成路由、JSON和自动回复。
    /// 调度器使用全局有界请求计数防止慢控制器造成消息池与内存无界增长。
    /// </summary>
    internal sealed class DnetServerDispatcher
    {
        /// <summary>
        /// 底层独占服务器。
        /// </summary>
        private readonly DNServer _server;

        /// <summary>
        /// 启动后冻结的事件路由表。
        /// </summary>
        private readonly DnetRouteTable _routes;

        /// <summary>
        /// 服务器JSON设置。
        /// </summary>
        private readonly JsonSerializerSettings _jsonSettings;

        /// <summary>
        /// 全服务器允许排队和执行的最大请求数。
        /// </summary>
        private readonly int _maxPendingRequests;

        /// <summary>
        /// 是否允许向客户端返回未处理异常消息。
        /// </summary>
        private readonly bool _includeExceptionDetails;

        /// <summary>
        /// 向FlurlDnetServer报告真实请求异常的回调。
        /// </summary>
        private readonly Action<DnetServerRequestFailedEventArgs> _requestFailed;

        /// <summary>
        /// 以Peer ID为键的连接内有序邮箱。
        /// </summary>
        private readonly ConcurrentDictionary<int, DnetPeerMailbox> _mailboxes =
            new ConcurrentDictionary<int, DnetPeerMailbox>();

        /// <summary>
        /// 记录已收到断开事件的Peer ID，关闭与接收并发时阻止重新创建孤立邮箱。
        /// </summary>
        private readonly ConcurrentDictionary<int, byte> _closedPeerIds =
            new ConcurrentDictionary<int, byte>();

        /// <summary>
        /// 已断开但仍有Action退出中的邮箱停止任务，确保服务器停止时不会提前释放单例控制器。
        /// </summary>
        private readonly ConcurrentDictionary<int, Task> _retiredMailboxStops =
            new ConcurrentDictionary<int, Task>();

        /// <summary>
        /// 在网络回调之外异步发送ServerBusy等拒绝响应的队列。
        /// </summary>
        private readonly ConcurrentQueue<DnetRejectedResponse> _rejectedResponses =
            new ConcurrentQueue<DnetRejectedResponse>();

        /// <summary>
        /// 当前排队和执行中的请求总数。
        /// </summary>
        private int _pendingRequestCount;

        /// <summary>
        /// 当前待发送拒绝响应数量。
        /// </summary>
        private int _rejectedResponseCount;

        /// <summary>
        /// 标记拒绝响应泵是否已经投递或正在运行。
        /// </summary>
        private int _rejectedResponsePumpRunning;

        /// <summary>
        /// 标记调度器是否正在停止。
        /// </summary>
        private int _stopping;

        /// <summary>
        /// 保护幂等停止任务创建的同步对象。
        /// </summary>
        private readonly object _stopLock = new object();

        /// <summary>
        /// 首次StopAsync创建的共享停止任务。
        /// </summary>
        private Task _stopTask;

        /// <summary>
        /// 初始化服务器请求调度器。
        /// </summary>
        /// <param name="server">由FlurlDnetServer独占的DNServer。</param>
        /// <param name="routes">冻结路由表。</param>
        /// <param name="options">启动时服务器选项快照。</param>
        /// <param name="requestFailed">请求失败诊断回调。</param>
        internal DnetServerDispatcher(DNServer server,
            DnetRouteTable routes,
            FlurlDnetServerOptions options,
            Action<DnetServerRequestFailedEventArgs> requestFailed)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _routes = routes ?? throw new ArgumentNullException(nameof(routes));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _jsonSettings = options.JsonSettings;
            _maxPendingRequests = options.MaxPendingRequests;
            _includeExceptionDetails = options.IncludeExceptionDetails;
            _requestFailed = requestFailed ?? throw new ArgumentNullException(nameof(requestFailed));
        }

        #region DNET事件入口

        /// <summary>
        /// 响应Peer连接事件，并提前建立稳定连接上下文。
        /// </summary>
        /// <param name="server">事件来源服务器。</param>
        /// <param name="peer">新建立的Peer。</param>
        internal void HandlePeerConnected(DNServer server, Peer peer)
        {
            if (peer == null || Volatile.Read(ref _stopping) != 0) return;
            // Peer ID在DNET进程生命周期内递增，不复用已断开的ID；closed标记存在时不能重新开放。
            if (_closedPeerIds.ContainsKey(peer.ID)) return;
            _mailboxes.GetOrAdd(peer.ID, ignored => CreateMailbox(peer));

            // SetAcceptSocket启动接收后连接可能立刻断开，创建邮箱后再次核对关闭标记。
            if (!_closedPeerIds.ContainsKey(peer.ID)) return;
            Task stopTask = null;
            lock (_stopLock) {
                if (_mailboxes.TryRemove(peer.ID, out DnetPeerMailbox mailbox)) {
                    stopTask = Task.Run((Func<Task>)mailbox.StopAsync);
                    _retiredMailboxStops[peer.ID] = stopTask;
                }
            }
            if (stopTask != null) TrackRetiredMailbox(peer.ID, stopTask);
        }

        /// <summary>
        /// 响应统一Peer断开事件，取消活动Action并回收尚未执行的请求。
        /// </summary>
        /// <param name="server">事件来源服务器。</param>
        /// <param name="peer">即将释放的Peer。</param>
        /// <param name="errorType">断开原因。</param>
        internal void HandlePeerDisconnected(DNServer server,
            Peer peer,
            PeerErrorType errorType)
        {
            if (peer == null) return;
            _closedPeerIds.TryAdd(peer.ID, 0);
            Task stopTask = null;
            lock (_stopLock) {
                if (_mailboxes.TryRemove(peer.ID, out DnetPeerMailbox mailbox)) {
                    // Cancel会执行CancellationToken注册回调，因此整个停止入口切到线程池，
                    // 避免用户注册的取消逻辑占用DNET Socket或心跳线程。
                    stopTask = Task.Run((Func<Task>)mailbox.StopAsync);
                    _retiredMailboxStops[peer.ID] = stopTask;
                }
            }
            if (stopTask != null) {
                TrackRetiredMailbox(peer.ID, stopTask);
            }
        }

        /// <summary>
        /// 独占提取指定Peer接收队列，并在最短回调时间内转移每条Message所有权。
        /// </summary>
        /// <param name="server">事件来源服务器。</param>
        /// <param name="peer">收到数据的Peer。</param>
        internal void HandlePeerReceived(DNServer server, Peer peer)
        {
            if (peer == null) return;
            List<Message> messages = peer.GetReceiveData();
            if (messages == null) return;

            try {
                for (int i = 0; i < messages.Count; i++) {
                    Message message = messages[i];
                    messages[i] = null;
                    if (message == null) continue;
                    EnqueueMessage(peer, message);
                }
            } finally {
                for (int i = 0; i < messages.Count; i++) {
                    messages[i]?.Recycle();
                }
                ListPool<Message>.Shared.Recycle(messages);
            }
        }

        #endregion

        #region 消息入队与邮箱生命周期

        /// <summary>
        /// 过滤基础设施消息、执行全局过载检查并把业务消息加入Peer邮箱。
        /// </summary>
        /// <param name="peer">发送消息的Peer。</param>
        /// <param name="message">已经从接收列表取得所有权的Message。</param>
        private void EnqueueMessage(Peer peer, Message message)
        {
            if (message.Format == Format.Heart) {
                message.Recycle();
                return;
            }
            if (Volatile.Read(ref _stopping) != 0) {
                QueueError(peer,
                    message.TxrId,
                    message.EventType,
                    DnetRemoteErrorCodes.ServerStopping,
                    "DNET服务器正在停止。");
                message.Recycle();
                return;
            }
            if (_closedPeerIds.ContainsKey(peer.ID)) {
                message.Recycle();
                return;
            }

            if (Interlocked.Increment(ref _pendingRequestCount) > _maxPendingRequests) {
                Interlocked.Decrement(ref _pendingRequestCount);
                QueueError(peer,
                    message.TxrId,
                    message.EventType,
                    DnetRemoteErrorCodes.ServerBusy,
                    "DNET服务器当前请求过多，请稍后重试。");
                message.Recycle();
                return;
            }

            var request = new DnetInboundRequest(peer, message);
            DnetPeerMailbox mailbox;
            try {
                mailbox = _mailboxes.GetOrAdd(peer.ID, ignored => CreateMailbox(peer));
            } catch (Exception exception) {
                CompleteRequest(request);
                QueueError(peer,
                    request.TxrId,
                    request.EventType,
                    DnetRemoteErrorCodes.ServerStopping,
                    "当前连接已经关闭。");
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DnetServerDispatcher.EnqueueMessage():创建Peer邮箱失败 {exception}");
                return;
            }

            if (_closedPeerIds.ContainsKey(peer.ID)) {
                Task closedStopTask = null;
                lock (_stopLock) {
                    if (_mailboxes.TryRemove(peer.ID, out DnetPeerMailbox closedMailbox)) {
                        closedStopTask = Task.Run((Func<Task>)closedMailbox.StopAsync);
                        _retiredMailboxStops[peer.ID] = closedStopTask;
                    }
                }
                if (closedStopTask != null) TrackRetiredMailbox(peer.ID, closedStopTask);
                CompleteRequest(request);
                return;
            }

            if (!mailbox.TryEnqueue(request)) {
                CompleteRequest(request);
                QueueError(peer,
                    request.TxrId,
                    request.EventType,
                    DnetRemoteErrorCodes.ServerStopping,
                    "当前连接已经关闭。");
            }
        }

        /// <summary>
        /// 为指定Peer创建连接上下文和串行邮箱。
        /// </summary>
        /// <param name="peer">邮箱所属Peer。</param>
        /// <returns>新建的Peer邮箱。</returns>
        private DnetPeerMailbox CreateMailbox(Peer peer)
        {
            return new DnetPeerMailbox(new DnetConnectionContext(peer),
                ProcessRequestAsync,
                CompleteRequest);
        }

        /// <summary>
        /// 归还单请求消息并释放全局待处理额度。
        /// </summary>
        /// <param name="request">已经执行或被丢弃的请求。</param>
        private void CompleteRequest(DnetInboundRequest request)
        {
            request?.Dispose();
            Interlocked.Decrement(ref _pendingRequestCount);
        }

        /// <summary>
        /// 观察断线触发的邮箱停止任务，防止未来实现变化产生未观察任务异常。
        /// </summary>
        /// <param name="peerId">已断开Peer的唯一ID。</param>
        /// <param name="stopTask">邮箱停止任务。</param>
        private void TrackRetiredMailbox(int peerId, Task stopTask)
        {
            stopTask.ContinueWith(task => {
                    _retiredMailboxStops.TryRemove(peerId, out _);
                    if (task.IsFaulted && LogProxy.Error != null)
                        LogProxy.Error($"DnetServerDispatcher:停止Peer邮箱异常 {task.Exception}");
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        #endregion

        #region Action执行与自动回复

        /// <summary>
        /// 在邮箱线程中完成路由、JSON解析、Action调用和自动回复，并隔离全部用户异常。
        /// </summary>
        /// <param name="request">当前有序请求。</param>
        /// <param name="connection">请求所属连接上下文。</param>
        private async Task ProcessRequestAsync(DnetInboundRequest request,
            DnetConnectionContext connection)
        {
            var context = new DnetServerContext(connection, request.TxrId, request.EventType);
            try {
                if (!_routes.TryGetAction(request.EventType, out DnetActionDescriptor action)) {
                    request.ReleaseMessage();
                    FailRequest(context,
                        DnetRemoteErrorCodes.RouteNotFound,
                        $"没有找到事件类型{request.EventType}对应的DNET控制器路由。",
                        new InvalidOperationException($"未注册DNET路由：EventType={request.EventType}。"));
                    return;
                }
                if (request.Format != Format.Json) {
                    request.ReleaseMessage();
                    FailRequest(context,
                        DnetRemoteErrorCodes.InvalidFormat,
                        "控制器请求必须使用JSON格式。",
                        new InvalidOperationException($"控制器路由收到不支持的格式：{request.Format}。"));
                    return;
                }
                if (request.Length <= 0) {
                    request.ReleaseMessage();
                    FailRequest(context,
                        DnetRemoteErrorCodes.InvalidPayload,
                        "JSON请求正文不能为空。",
                        new JsonSerializationException("JSON请求正文为空。"));
                    return;
                }

                object requestValue;
                try {
                    string json = request.ReadUtf8AndRelease();
                    if (string.IsNullOrWhiteSpace(json)) {
                        throw new JsonSerializationException("JSON请求正文只包含空白字符。");
                    }
                    requestValue = action.DeserializeRequest(json, _jsonSettings);
                } catch (Exception exception) {
                    FailRequest(context,
                        DnetRemoteErrorCodes.InvalidPayload,
                        "JSON请求正文无法转换为控制器参数。",
                        exception);
                    return;
                }

                if (context.CancellationToken.IsCancellationRequested) return;

                object replyValue;
                try {
                    replyValue = await action.InvokeAsync(requestValue,
                            context,
                            context.CancellationToken)
                        .ConfigureAwait(false);
                } catch (DnetActionException exception) {
                    FailRequest(context, exception.ErrorCode, exception.Message, exception);
                    return;
                } catch (OperationCanceledException)
                    when (context.CancellationToken.IsCancellationRequested) {
                    return;
                } catch (Exception exception) {
                    string message = _includeExceptionDetails
                        ? exception.Message
                        : "控制器处理请求时发生内部错误。";
                    FailRequest(context,
                        DnetRemoteErrorCodes.HandlerFailed,
                        message,
                        exception);
                    return;
                }

                if (request.TxrId == 0 || context.CancellationToken.IsCancellationRequested) return;

                string replyJson;
                try {
                    replyJson = JsonConvert.SerializeObject(replyValue, _jsonSettings);
                } catch (Exception exception) {
                    FailRequest(context,
                        DnetRemoteErrorCodes.HandlerFailed,
                        "控制器响应无法序列化。",
                        exception);
                    return;
                }

                if (!_server.TrySend(connection.Peer,
                    replyJson,
                    Format.Json,
                    request.TxrId,
                    request.EventType,
                    true)) {
                    ReportFailure(context,
                        DnetRemoteErrorCodes.HandlerFailed,
                        new InvalidOperationException("控制器响应未能进入DNET发送队列。"));
                }
            } catch (Exception exception) {
                request.ReleaseMessage();
                FailRequest(context,
                    DnetRemoteErrorCodes.HandlerFailed,
                    "服务端请求调度失败。",
                    exception);
            }
        }

        /// <summary>
        /// 报告真实异常，并在事务请求仍可回复时发送标准远端错误。
        /// </summary>
        /// <param name="context">失败请求上下文。</param>
        /// <param name="errorCode">稳定错误码。</param>
        /// <param name="safeMessage">允许返回客户端的安全说明。</param>
        /// <param name="exception">真实服务端异常。</param>
        private void FailRequest(DnetServerContext context,
            string errorCode,
            string safeMessage,
            Exception exception)
        {
            ReportFailure(context, errorCode, exception);
            if (context.TxrId == 0 || context.CancellationToken.IsCancellationRequested) return;
            SendErrorNow(context.Connection.Peer,
                context.TxrId,
                context.EventType,
                errorCode,
                safeMessage);
        }

        /// <summary>
        /// 把真实服务端异常转发给FlurlDnetServer诊断事件。
        /// </summary>
        /// <param name="context">失败请求上下文。</param>
        /// <param name="errorCode">映射错误码。</param>
        /// <param name="exception">真实异常。</param>
        private void ReportFailure(DnetServerContext context,
            string errorCode,
            Exception exception)
        {
            _requestFailed(new DnetServerRequestFailedEventArgs(context, errorCode, exception));
        }

        #endregion

        #region 拒绝响应泵

        /// <summary>
        /// 把网络回调中产生的过载或停止错误转换为不持有Message的轻量异步响应项。
        /// </summary>
        /// <param name="peer">目标Peer。</param>
        /// <param name="txrId">原请求事务ID。</param>
        /// <param name="eventType">原请求事件类型。</param>
        /// <param name="errorCode">标准错误码。</param>
        /// <param name="message">安全错误说明。</param>
        private void QueueError(Peer peer,
            int txrId,
            int eventType,
            string errorCode,
            string message)
        {
            if (peer == null || txrId == 0) return;

            // 拒绝响应本身也保持有界；极端持续洪泛时宁可让超额请求超时，也不允许错误队列耗尽内存。
            if (Interlocked.Increment(ref _rejectedResponseCount) > _maxPendingRequests) {
                Interlocked.Decrement(ref _rejectedResponseCount);
                return;
            }
            _rejectedResponses.Enqueue(new DnetRejectedResponse(peer,
                txrId,
                eventType,
                errorCode,
                message));
            StartRejectedResponsePump();
        }

        /// <summary>
        /// 确保全服务器最多只有一个拒绝响应泵运行。
        /// </summary>
        private void StartRejectedResponsePump()
        {
            if (Interlocked.CompareExchange(ref _rejectedResponsePumpRunning, 1, 0) != 0) return;
            Task.Run((Action)PumpRejectedResponses);
        }

        /// <summary>
        /// 在普通线程池线程上按队列顺序发送拒绝响应，避免网络回调执行JSON序列化。
        /// </summary>
        private void PumpRejectedResponses()
        {
            while (true) {
                while (_rejectedResponses.TryDequeue(out DnetRejectedResponse response)) {
                    Interlocked.Decrement(ref _rejectedResponseCount);
                    SendErrorNow(response.peer,
                        response.txrId,
                        response.eventType,
                        response.errorCode,
                        response.message);
                }

                Interlocked.Exchange(ref _rejectedResponsePumpRunning, 0);
                if (_rejectedResponses.IsEmpty
                    || Interlocked.CompareExchange(ref _rejectedResponsePumpRunning, 1, 0) != 0) {
                    return;
                }
            }
        }

        /// <summary>
        /// 序列化并发送一条标准远端错误响应。
        /// 错误协议固定使用Newtonsoft默认设置，避免业务JsonSerializerSettings改变字段形状。
        /// </summary>
        /// <param name="peer">目标Peer。</param>
        /// <param name="txrId">原请求事务ID。</param>
        /// <param name="requestEventType">原请求事件类型。</param>
        /// <param name="errorCode">稳定错误码。</param>
        /// <param name="message">安全错误说明。</param>
        private void SendErrorNow(Peer peer,
            int txrId,
            int requestEventType,
            string errorCode,
            string message)
        {
            if (peer == null || txrId == 0) return;
            var error = new DnetRemoteError {
                code = errorCode,
                message = message,
                requestEventType = requestEventType
            };
            string json = JsonConvert.SerializeObject(error);
            _server.TrySend(peer,
                json,
                Format.Json,
                txrId,
                DnetFrameworkEventTypes.Error,
                true);
        }

        #endregion

        #region 停止

        /// <summary>
        /// 原子停止接受新请求、取消全部连接上下文并等待正在执行的Action结束。
        /// </summary>
        /// <returns>全部Peer邮箱停止后的任务。</returns>
        internal Task StopAsync()
        {
            lock (_stopLock) {
                if (_stopTask != null) return _stopTask;

                Interlocked.Exchange(ref _stopping, 1);
                var stopTasks = new List<Task>();
                foreach (KeyValuePair<int, DnetPeerMailbox> pair in _mailboxes) {
                    if (_mailboxes.TryRemove(pair.Key, out DnetPeerMailbox mailbox)) {
                        stopTasks.Add(mailbox.StopAsync());
                    }
                }
                foreach (Task retiredStop in _retiredMailboxStops.Values) {
                    stopTasks.Add(retiredStop);
                }
                _stopTask = stopTasks.Count == 0
                    ? Task.FromResult(true)
                    : Task.WhenAll(stopTasks);
                return _stopTask;
            }
        }

        #endregion

        /// <summary>
        /// 表示一条已经脱离池化Message、等待异步发送的轻量拒绝响应。
        /// </summary>
        private sealed class DnetRejectedResponse
        {
            /// <summary>
            /// 初始化拒绝响应数据。
            /// </summary>
            /// <param name="peerValue">目标Peer。</param>
            /// <param name="txrIdValue">原请求事务ID。</param>
            /// <param name="eventTypeValue">原请求事件类型。</param>
            /// <param name="errorCodeValue">标准错误码。</param>
            /// <param name="messageValue">安全错误说明。</param>
            internal DnetRejectedResponse(Peer peerValue,
                int txrIdValue,
                int eventTypeValue,
                string errorCodeValue,
                string messageValue)
            {
                peer = peerValue;
                txrId = txrIdValue;
                eventType = eventTypeValue;
                errorCode = errorCodeValue;
                message = messageValue;
            }

            /// <summary>
            /// 目标Peer。
            /// </summary>
            internal readonly Peer peer;

            /// <summary>
            /// 原请求事务ID。
            /// </summary>
            internal readonly int txrId;

            /// <summary>
            /// 原请求事件类型。
            /// </summary>
            internal readonly int eventType;

            /// <summary>
            /// 标准错误码。
            /// </summary>
            internal readonly string errorCode;

            /// <summary>
            /// 安全错误说明。
            /// </summary>
            internal readonly string message;
        }
    }
}
