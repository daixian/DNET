using System.Diagnostics;

namespace DNET
{
    /// <summary>
    /// 单线程采样器，用于统计一个线程的工作与等待时间占比。
    /// </summary>
    public class ThreadSampler
    {
        /// <summary>
        /// 窗口大小，表示记录时间片段的数量。
        /// </summary>
        private readonly int _windowSize;

        /// <summary>
        /// 存储工作时间段的数组。
        /// </summary>
        private readonly double[] _workDurations;

        /// <summary>
        /// 存储等待时间段的数组。
        /// </summary>
        private readonly double[] _waitDurations;

        /// <summary>
        /// 当前索引位置，用于循环写入数组。
        /// </summary>
        private int _index;

        /// <summary>
        /// 所有工作时间段的总和。
        /// </summary>
        private double _totalWork;

        /// <summary>
        /// 所有等待时间段的总和。
        /// </summary>
        private double _totalWait;

        /// <summary>
        /// 最近一次开始工作的时间戳。
        /// </summary>
        private long _lastWorkStart;

        /// <summary>
        /// 最近一次开始等待的时间戳。
        /// </summary>
        private long _lastWaitStart;

        /// <summary>
        /// 用于计时的 Stopwatch 实例。
        /// </summary>
        private readonly Stopwatch _sw;

        /// <summary>
        /// 获取线程名称。
        /// </summary>
        public string ThreadName { get; }

        /// <summary>
        /// 获取线程工作时间占用的百分比。
        /// </summary>
        public double WorkOccupancyPercent { get; private set; }

        /// <summary>
        /// 构造函数，初始化线程采样器。
        /// </summary>
        /// <param name="threadName">线程名称。</param>
        /// <param name="windowSize">时间窗口大小。</param>
        public ThreadSampler(string threadName, int windowSize)
        {
            // TODO: windowSize <= 0 时可能导致除零或数组异常，必要时可加入参数校验
            ThreadName = threadName;
            _windowSize = windowSize;
            _workDurations = new double[_windowSize];
            _waitDurations = new double[_windowSize];
            _sw = Stopwatch.StartNew();
        }

        /// <summary>
        /// 开始工作阶段，记录当前时间。
        /// </summary>
        public void BeginWork()
        {
            long now = _sw.ElapsedTicks;
            if (_lastWaitStart != 0) {
                double waitMs = TicksToMs(now - _lastWaitStart);
                UpdateRingBuffer(_waitDurations, waitMs, ref _totalWait);
            }
            _lastWorkStart = now;
        }

        /// <summary>
        /// 结束工作阶段，记录工作时间并更新状态。
        /// </summary>
        public void EndWork()
        {
            long now = _sw.ElapsedTicks;
            if (_lastWorkStart != 0) {
                double workMs = TicksToMs(now - _lastWorkStart);
                UpdateRingBuffer(_workDurations, workMs, ref _totalWork);
            }

            _lastWaitStart = now;

            UpdateOccupancy(); // 更新工作时间占比
            _index = (_index + 1) % _windowSize; // 移动到下一个索引
        }

        /// <summary>
        /// 获取当前线程的工作时间占用百分比。
        /// </summary>
        /// <returns>工作时间占用百分比。</returns>
        public double GetOccupancyPercent() => WorkOccupancyPercent;

        /// <summary>
        /// 将计时器的 ticks 转换为毫秒（ms）。
        /// </summary>
        /// <param name="ticks">计时器的 ticks 数量。</param>
        /// <returns>对应的毫秒数。</returns>
        private static double TicksToMs(long ticks) =>
            ticks * 1000.0 / Stopwatch.Frequency;

        /// <summary>
        /// 更新环形缓冲区的数据。
        /// </summary>
        /// <param name="buffer">目标缓冲区数组。</param>
        /// <param name="newValue">新值。</param>
        /// <param name="total">当前总和。</param>
        private void UpdateRingBuffer(double[] buffer, double newValue, ref double total)
        {
            double old = buffer[_index]; // 取出旧值
            buffer[_index] = newValue; // 替换为新值
            total += newValue - old; // 更新总和
        }

        /// <summary>
        /// 计算工作时间的占用百分比。
        /// </summary>
        private void UpdateOccupancy()
        {
            double total = _totalWork + _totalWait;
            WorkOccupancyPercent = total > 0 ? (_totalWork / total) * 100.0 : 0;
        }
    }
}
