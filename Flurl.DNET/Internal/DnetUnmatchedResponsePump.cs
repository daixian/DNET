using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Flurl.DNET
{
    /// <summary>
    /// 在独立线程池任务中按到达顺序处理未匹配响应，避免用户事件阻塞DNET的Socket接收回调。
    /// 每个响应在回调返回后由泵统一释放。
    /// </summary>
    internal sealed class DnetUnmatchedResponsePump : IDisposable
    {
        /// <summary>
        /// 等待派发的未匹配响应队列。
        /// </summary>
        private readonly ConcurrentQueue<DnetResponse> _queue =
            new ConcurrentQueue<DnetResponse>();

        /// <summary>
        /// 实际执行用户事件派发的回调。
        /// </summary>
        private readonly Action<DnetResponse> _responseHandler;

        /// <summary>
        /// 标记是否已有线程池工作项获得消费权。
        /// </summary>
        private int _isRunning;

        /// <summary>
        /// 标记当前泵是否已经停止并释放。
        /// </summary>
        private int _disposed;

        /// <summary>
        /// 初始化未匹配响应串行泵。
        /// </summary>
        /// <param name="responseHandler">对单条未匹配响应执行事件派发的回调。</param>
        internal DnetUnmatchedResponsePump(Action<DnetResponse> responseHandler)
        {
            _responseHandler = responseHandler ?? throw new ArgumentNullException(nameof(responseHandler));
        }

        /// <summary>
        /// 将未匹配响应加入有序派发队列。
        /// </summary>
        /// <param name="response">已经取得所有权的响应。</param>
        internal void Enqueue(DnetResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (Volatile.Read(ref _disposed) != 0) {
                response.Dispose();
                return;
            }

            _queue.Enqueue(response);
            TryStartWorker();
        }

        /// <summary>
        /// 停止事件派发，并回收队列中尚未处理的全部响应。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            while (_queue.TryDequeue(out DnetResponse response)) {
                response.Dispose();
            }
        }

        /// <summary>
        /// 在当前没有工作项时获得消费权并投递线程池工作。
        /// </summary>
        private void TryStartWorker()
        {
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(ProcessQueue);
        }

        /// <summary>
        /// 串行消费队列，并处理生产者与消费者交接消费权时的竞态。
        /// </summary>
        /// <param name="state">ThreadPool要求的回调状态，本实现不使用。</param>
        private void ProcessQueue(object state)
        {
            while (true) {
                while (_queue.TryDequeue(out DnetResponse response)) {
                    try {
                        if (Volatile.Read(ref _disposed) == 0) {
                            _responseHandler(response);
                        }
                    } finally {
                        response.Dispose();
                    }
                }

                Interlocked.Exchange(ref _isRunning, 0);
                if (_queue.IsEmpty
                    || Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) {
                    return;
                }
            }
        }
    }
}
