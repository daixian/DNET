using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DNET
{
    /// <summary>
    /// 往返时延（Round-Trip Time，RTT）统计器，用于记录每个消息从发送到接收之间的耗时。
    /// 可用于统计平均往返时延、最大往返时延、最小往返时延以及调用次数。
    /// </summary>
    public class RttStatistics
    {
        /// <summary>
        /// 记录已发送但尚未接收到响应的消息时间戳，键为 TxrId，值为 Stopwatch 时间戳。
        /// </summary>
        private readonly ConcurrentDictionary<int, long> _sentTimestamps = new ConcurrentDictionary<int, long>();

        /// <summary>
        /// 已记录的延迟样本总数。
        /// </summary>
        private long _totalCount = 0;

        /// <summary>
        /// 延迟总和（用于计算平均值）。
        /// </summary>
        private double _totalLatency = 0;

        /// <summary>
        /// 当前记录中的最大延迟（毫秒）。
        /// </summary>
        private double _maxLatency = double.MinValue;

        /// <summary>
        /// 当前记录中的最小延迟（毫秒）。
        /// </summary>
        private double _minLatency = double.MaxValue;

        /// <summary>
        /// 记录发送事件，标记当前时间戳。
        /// </summary>
        /// <param name="txrId">事务 ID，用于关联请求和响应。</param>
        public void RecordSent(int txrId)
        {
            _sentTimestamps[txrId] = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// 记录接收事件，计算并返回延迟（毫秒）。
        /// </summary>
        /// <param name="txrId">事务 ID，对应之前发送的记录。</param>
        /// <returns>若找到对应的发送时间，则返回延迟（毫秒）；否则返回 -1。</returns>
        public double RecordReceived(int txrId)
        {
            if (_sentTimestamps.TryRemove(txrId, out var startTimestamp)) {
                // 计算延迟：当前时间戳 - 起始时间戳，单位换算为毫秒
                double latency = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

                // 累加统计值（非线程安全，轻量高性能）
                // TODO: 如果在多线程高并发下使用，考虑原子累加或加锁保证统计一致性
                _totalCount++;
                _totalLatency += latency;

                if (latency > _maxLatency) _maxLatency = latency;
                if (latency < _minLatency) _minLatency = latency;

                return latency;
            }
            return -1; // 未找到对应发送记录
        }

        /// <summary>
        /// 当前记录的平均往返时延（毫秒）。
        /// </summary>
        public double Average => _totalCount > 0 ? _totalLatency / _totalCount : 0;

        /// <summary>
        /// 当前记录中的最大往返时延（毫秒）。
        /// </summary>
        public double Max => _totalCount > 0 ? _maxLatency : 0;

        /// <summary>
        /// 当前记录中的最小往返时延（毫秒）。
        /// </summary>
        public double Min => _totalCount > 0 ? _minLatency : 0;

        /// <summary>
        /// 当前记录的总次数（样本数量）。
        /// </summary>
        public long Count => _totalCount;

        /// <summary>
        /// 清空所有统计数据与时间戳记录。
        /// </summary>
        public void Reset()
        {
            _sentTimestamps.Clear();
            _totalCount = 0;
            _totalLatency = 0;
            _maxLatency = double.MinValue;
            _minLatency = double.MaxValue;
        }
    }
}
