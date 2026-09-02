using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DNET;

namespace Flurl.DNET
{
    /// <summary>
    /// 为单个Peer维护有序、可取消的异步请求邮箱。
    /// 每个邮箱同时最多执行一个Action，不同邮箱各自在.NET线程池上推进，从而实现连接内有序、连接间并行。
    /// </summary>
    internal sealed class DnetPeerMailbox
    {
        /// <summary>
        /// 保护请求队列、运行状态和停止状态的同步对象。
        /// </summary>
        private readonly object _syncRoot = new object();

        /// <summary>
        /// 尚未开始执行的连接内有序请求队列。
        /// </summary>
        private readonly Queue<DnetInboundRequest> _requests =
            new Queue<DnetInboundRequest>();

        /// <summary>
        /// 执行单条控制器请求的异步回调。
        /// </summary>
        private readonly Func<DnetInboundRequest, DnetConnectionContext, Task> _requestProcessor;

        /// <summary>
        /// 请求完成或被丢弃后释放全局待处理计数的回调。
        /// </summary>
        private readonly Action<DnetInboundRequest> _requestCompleted;

        /// <summary>
        /// 邮箱完全停止时完成的异步完成源。
        /// </summary>
        private readonly TaskCompletionSource<bool> _stoppedCompletionSource =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 标记当前是否已有一个排队或运行中的DrainAsync。
        /// </summary>
        private bool _isDraining;

        /// <summary>
        /// 标记当前邮箱是否已经停止接受新请求。
        /// </summary>
        private bool _isStopping;

        /// <summary>
        /// 标记停止资源清理是否已经由一个线程取得完成权。
        /// </summary>
        private bool _isStopCompleted;

        /// <summary>
        /// 使用连接上下文和调度回调初始化Peer邮箱。
        /// </summary>
        /// <param name="connection">当前Peer的连接上下文。</param>
        /// <param name="requestProcessor">单请求异步处理器。</param>
        /// <param name="requestCompleted">单请求资源完成回调。</param>
        internal DnetPeerMailbox(DnetConnectionContext connection,
            Func<DnetInboundRequest, DnetConnectionContext, Task> requestProcessor,
            Action<DnetInboundRequest> requestCompleted)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _requestProcessor = requestProcessor ?? throw new ArgumentNullException(nameof(requestProcessor));
            _requestCompleted = requestCompleted ?? throw new ArgumentNullException(nameof(requestCompleted));
        }

        /// <summary>
        /// 获取当前邮箱对应的稳定连接上下文。
        /// </summary>
        internal DnetConnectionContext Connection { get; }

        /// <summary>
        /// 尝试按到达顺序加入请求，并在首次入队时把邮箱泵投递到线程池。
        /// </summary>
        /// <param name="request">已经取得池化Message所有权的请求。</param>
        /// <returns>邮箱仍接受请求时返回true。</returns>
        internal bool TryEnqueue(DnetInboundRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            bool shouldStartDrain = false;
            lock (_syncRoot) {
                if (_isStopping) return false;
                _requests.Enqueue(request);
                if (!_isDraining) {
                    _isDraining = true;
                    shouldStartDrain = true;
                }
            }

            if (shouldStartDrain) {
                Task.Run((Func<Task>)DrainAsync);
            }
            return true;
        }

        /// <summary>
        /// 停止邮箱、取消当前连接Action并立即回收所有尚未开始的排队请求。
        /// </summary>
        /// <returns>当前正在执行的Action结束且连接上下文释放后的任务。</returns>
        internal Task StopAsync()
        {
            List<DnetInboundRequest> discardedRequests = null;
            bool completeImmediately = false;
            lock (_syncRoot) {
                if (!_isStopping) {
                    _isStopping = true;
                    if (_requests.Count > 0) {
                        discardedRequests = new List<DnetInboundRequest>(_requests);
                        _requests.Clear();
                    }
                }
                if (!_isDraining) completeImmediately = true;
            }

            Connection.Cancel();
            if (discardedRequests != null) {
                foreach (DnetInboundRequest request in discardedRequests) {
                    _requestCompleted(request);
                }
            }
            if (completeImmediately) CompleteStop();
            return _stoppedCompletionSource.Task;
        }

        /// <summary>
        /// 按连接内顺序逐条等待Action完成，且确保任意异常路径都会归还当前请求资源。
        /// </summary>
        private async Task DrainAsync()
        {
            while (true) {
                DnetInboundRequest request;
                bool shouldComplete;
                lock (_syncRoot) {
                    if (_requests.Count == 0) {
                        _isDraining = false;
                        shouldComplete = _isStopping;
                        request = null;
                    } else {
                        shouldComplete = false;
                        request = _requests.Dequeue();
                    }
                }

                if (request == null) {
                    if (shouldComplete) CompleteStop();
                    return;
                }

                try {
                    await _requestProcessor(request, Connection).ConfigureAwait(false);
                } catch (Exception exception) {
                    if (LogProxy.Error != null)
                        LogProxy.Error($"DnetPeerMailbox.DrainAsync():未隔离的调度异常 {exception}");
                } finally {
                    _requestCompleted(request);
                }
            }
        }

        /// <summary>
        /// 释放连接上下文并以幂等方式完成邮箱停止任务。
        /// </summary>
        private void CompleteStop()
        {
            lock (_syncRoot) {
                if (_isStopCompleted) return;
                _isStopCompleted = true;
            }
            Connection.Dispose();
            _stoppedCompletionSource.TrySetResult(true);
        }
    }
}
