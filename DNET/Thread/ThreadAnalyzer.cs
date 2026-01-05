using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 线程性能分析工具包，支持工作/等待时间采样和统计，适合多线程并发场景
    /// </summary>
    public class ThreadAnalyzer : IDisposable
    {
        /// <summary>
        /// 内部静态只读实例，CLR保证线程安全初始化
        /// </summary>
        private static readonly Lazy<ThreadAnalyzer> _instance =
            new Lazy<ThreadAnalyzer>(() => new ThreadAnalyzer());

        /// <summary>
        /// 单例
        /// </summary>
        public static ThreadAnalyzer Inst => _instance.Value;

        /// <summary>
        /// 构造函数，默认窗口64
        /// </summary>
        /// <param name="windowSize">采样窗口大小</param>
        public ThreadAnalyzer(int windowSize = 64)
        {
            // TODO: windowSize <= 0 时可能导致统计异常，必要时可加入参数校验
            _windowSize = windowSize;
            _timer = new Timer(TimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// 采样器集合，按线程ID索引
        /// </summary>
        private readonly ConcurrentDictionary<int, ThreadSampler> _samplers = new ConcurrentDictionary<int, ThreadSampler>();

        /// <summary>
        /// 采样窗口大小
        /// </summary>
        private readonly int _windowSize;

        /// <summary>
        /// 自动采样计时器
        /// </summary>
        private readonly Timer _timer;

        /// <summary>
        /// 统计回调，参数是线程名和当前工作占比（百分比）
        /// </summary>
        public event Action<string, double> OnSample;

        /// <summary>
        /// 是否启用自动采样回调（默认关闭）
        /// </summary>
        public bool AutoSampleEnabled { get; set; }

        /// <summary>
        /// 自动采样周期，毫秒（默认1000ms）
        /// </summary>
        public int AutoSampleInterval { get; set; } = 1000;

        /// <summary>
        /// 获取或创建当前线程对应的采样器
        /// </summary>
        /// <returns>线程对应的采样器</returns>
        private ThreadSampler GetOrCreateSampler()
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            return _samplers.GetOrAdd(threadId, id =>
                new ThreadSampler(Thread.CurrentThread.Name ?? $"Thread-{id}", _windowSize));
        }

        /// <summary>
        /// 标记当前线程工作开始
        /// </summary>
        public void BeginWork()
        {
            var sampler = GetOrCreateSampler();
            sampler.BeginWork();
        }

        /// <summary>
        /// 标记当前线程工作结束（进入等待）
        /// </summary>
        public void EndWork()
        {
            var sampler = GetOrCreateSampler();
            sampler.EndWork();
        }

        /// <summary>
        /// 获取某线程的工作占比，线程ID为 key
        /// </summary>
        /// <param name="threadId">线程ID</param>
        /// <param name="occupancy">输出的占用率</param>
        /// <returns>是否获取成功</returns>
        public bool TryGetOccupancy(int threadId, out double occupancy)
        {
            if (_samplers.TryGetValue(threadId, out var sampler)) {
                occupancy = sampler.GetOccupancyPercent();
                return true;
            }
            occupancy = 0;
            return false;
        }

        /// <summary>
        /// 获取所有线程的采样数据快照（线程名 -> 占用率）
        /// </summary>
        /// <returns>线程名到占用率的字典</returns>
        public Dictionary<string, double> GetAllOccupancies()
        {
            var dict = new Dictionary<string, double>();
            foreach (var kv in _samplers) {
                dict[kv.Value.ThreadName] = kv.Value.GetOccupancyPercent();
            }
            return dict;
        }

        /// <summary>
        /// 开启自动采样回调
        /// </summary>
        public void StartAutoSampling()
        {
            if (AutoSampleEnabled) return;
            AutoSampleEnabled = true;
            _timer?.Change(0, AutoSampleInterval);
        }

        /// <summary>
        /// 停止自动采样
        /// </summary>
        public void StopAutoSampling()
        {
            AutoSampleEnabled = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// 自动采样的定时器回调
        /// </summary>
        /// <param name="state">计时器状态</param>
        private void TimerCallback(object state)
        {
            if (!AutoSampleEnabled) return;
            var snapshot = GetAllOccupancies();
            foreach (var kv in snapshot) {
                try {
                    OnSample?.Invoke(kv.Key, kv.Value);
                } catch {
                    /* 忽略回调异常 */
                }
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            StopAutoSampling();
            _timer?.Dispose();
            _samplers.Clear();
        }
    }
}
