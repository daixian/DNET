using System;
using System.Threading;
using System.Threading.Tasks;

namespace Flurl.DNET
{
    /// <summary>
    /// 保存单个等待响应请求的完成源、超时计时器和外部取消注册。
    /// 所有完成路径通过原子状态竞争，保证响应、超时、取消和断线中只有一个成为最终结果。
    /// </summary>
    internal sealed class DnetPendingRequest
    {
        #region 状态与资源

        /// <summary>
        /// 尚未完成状态。
        /// </summary>
        private const int _pendingState = 0;

        /// <summary>
        /// 已经完成状态。
        /// </summary>
        private const int _completedState = 1;

        /// <summary>
        /// 以异步方式执行continuation的响应完成源。
        /// </summary>
        private readonly TaskCompletionSource<DnetResponse> _completionSource =
            new TaskCompletionSource<DnetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 超时发生时通知请求调度器的回调。
        /// </summary>
        private readonly Action<DnetPendingRequest> _timeoutCallback;

        /// <summary>
        /// 外部取消发生时通知请求调度器的回调。
        /// </summary>
        private readonly Action<DnetPendingRequest> _cancellationCallback;

        /// <summary>
        /// 当前请求的单次超时计时器。
        /// </summary>
        private Timer _timeoutTimer;

        /// <summary>
        /// 当前请求在调用方CancellationToken上的注册。
        /// </summary>
        private CancellationTokenRegistration _cancellationRegistration;

        /// <summary>
        /// 请求完成状态，使用Interlocked保证只有一个完成路径获胜。
        /// </summary>
        private int _completionState;

        #endregion

        #region 构造与状态

        /// <summary>
        /// 初始化一个等待响应的请求。
        /// </summary>
        /// <param name="txrId">请求事务ID。</param>
        /// <param name="eventType">请求事件类型。</param>
        /// <param name="timeout">请求超时时间。</param>
        /// <param name="timeoutCallback">超时回调。</param>
        /// <param name="cancellationCallback">外部取消回调。</param>
        internal DnetPendingRequest(int txrId,
            int eventType,
            TimeSpan timeout,
            Action<DnetPendingRequest> timeoutCallback,
            Action<DnetPendingRequest> cancellationCallback)
        {
            TxrId = txrId;
            EventType = eventType;
            Timeout = timeout;
            _timeoutCallback = timeoutCallback ?? throw new ArgumentNullException(nameof(timeoutCallback));
            _cancellationCallback = cancellationCallback ?? throw new ArgumentNullException(nameof(cancellationCallback));
        }

        /// <summary>
        /// 请求事务ID。
        /// </summary>
        internal int TxrId { get; }

        /// <summary>
        /// 请求事件类型。
        /// </summary>
        internal int EventType { get; }

        /// <summary>
        /// 请求超时时间。
        /// </summary>
        internal TimeSpan Timeout { get; }

        /// <summary>
        /// 调用方最终等待的响应任务。
        /// </summary>
        internal Task<DnetResponse> Task => _completionSource.Task;

        /// <summary>
        /// 请求是否已经由任意完成路径结束。
        /// </summary>
        internal bool IsCompleted => Volatile.Read(ref _completionState) == _completedState;

        #endregion

        #region 等待与完成

        /// <summary>
        /// 在请求进入并发等待表后启动超时和外部取消监听。
        /// </summary>
        /// <param name="cancellationToken">调用方取消标记。</param>
        internal void Start(CancellationToken cancellationToken)
        {
            var timeoutTimer = new Timer(OnTimeout,
                null,
                Timeout,
                System.Threading.Timeout.InfiniteTimeSpan);
            _timeoutTimer = timeoutTimer;
            if (IsCompleted) {
                timeoutTimer.Dispose();
            }

            if (!cancellationToken.CanBeCanceled) return;

            CancellationTokenRegistration cancellationRegistration =
                cancellationToken.Register(OnCancellation);
            _cancellationRegistration = cancellationRegistration;
            if (IsCompleted) {
                cancellationRegistration.Dispose();
            }
        }

        /// <summary>
        /// 尝试用收到的匹配响应完成请求。
        /// </summary>
        /// <param name="response">匹配到的响应。</param>
        /// <returns>当前调用赢得完成竞争时返回true。</returns>
        internal bool TrySetResponse(DnetResponse response)
        {
            if (!TryBeginCompletion()) return false;

            DisposeWaitResources();
            return _completionSource.TrySetResult(response);
        }

        /// <summary>
        /// 尝试用异常完成请求。
        /// </summary>
        /// <param name="exception">导致请求失败的异常。</param>
        /// <returns>当前调用赢得完成竞争时返回true。</returns>
        internal bool TrySetException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (!TryBeginCompletion()) return false;

            DisposeWaitResources();
            return _completionSource.TrySetException(exception);
        }

        /// <summary>
        /// 尝试使用标准任务取消语义完成请求。
        /// </summary>
        /// <returns>当前调用赢得完成竞争时返回true。</returns>
        internal bool TrySetCanceled()
        {
            if (!TryBeginCompletion()) return false;

            DisposeWaitResources();
            return _completionSource.TrySetCanceled();
        }

        /// <summary>
        /// 原子竞争请求完成权。
        /// </summary>
        /// <returns>当前调用首次把状态改为完成时返回true。</returns>
        private bool TryBeginCompletion()
        {
            return Interlocked.CompareExchange(ref _completionState,
                _completedState,
                _pendingState) == _pendingState;
        }

        /// <summary>
        /// 释放超时计时器和外部取消注册。
        /// </summary>
        private void DisposeWaitResources()
        {
            Interlocked.Exchange(ref _timeoutTimer, null)?.Dispose();
            _cancellationRegistration.Dispose();
        }

        /// <summary>
        /// 响应超时计时器并通知请求调度器。
        /// </summary>
        /// <param name="state">Timer要求的回调状态，本实现不使用。</param>
        private void OnTimeout(object state)
        {
            _timeoutCallback(this);
        }

        /// <summary>
        /// 响应调用方CancellationToken并通知请求调度器。
        /// </summary>
        private void OnCancellation()
        {
            _cancellationCallback(this);
        }

        #endregion
    }
}
