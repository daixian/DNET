using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 用于计算一个"工作-等待"模型的工作时间占用
    /// </summary>
    public class DThreadTimeAnalyze
    {
        /// <summary>
        /// 构造函数 
        /// </summary>
        /// <param name="Length">记录的工作循环总次数</param>
        /// <param name="updataCount">每工作循环多少次计算一次</param>
        public DThreadTimeAnalyze(int Length = 32, int updataCount = 1)
        {
            sw = new Stopwatch();
            sw.Start();
            _timesCost = new double[Length];
            _timesWait = new double[Length];

            _timeStart = sw.ElapsedTicks;
            _timeWait = sw.ElapsedTicks;
            this._updataCount = (updataCount != 0) ? updataCount : 8;
        }

        private Stopwatch sw;
        private long _timeStart = 0;
        private long _timeWait = 0;
        private double[] _timesCost;
        private double[] _timesWait;
        private int _curWorkCount = 0;
        private int _updataCount = 1;

        /// <summary>
        /// 默认是不开启的
        /// </summary>
        public bool isWork = false;

        /// <summary>
        /// 工作占时比（百分率）
        /// </summary>
        public double OccupancyRate { get; private set; }

        /// <summary>
        /// 工作开始调用
        /// </summary>
        public long WorkStart()
        {
            if (!isWork) {
                return 0;
            }
            _timeStart = sw.ElapsedTicks;
            long timeCost = _timeStart - _timeWait; //得出该次等待时间
            _timesWait[_curWorkCount] = timeCost;
            return timeCost;
        }

        /// <summary>
        /// 工作结束，进入等待时调用
        /// </summary>
        public long WaitStart()
        {
            if (!isWork) {
                return 0;
            }

            _timeWait = sw.ElapsedTicks;
            long timeCost = _timeWait - _timeStart; //得出消耗时间
            _timesCost[_curWorkCount] = timeCost;

            if (_curWorkCount >= _timesCost.Length - 1) //如果已经是最后一个，归零
            {
                Interlocked.Exchange(ref _curWorkCount, 0);
            }
            else {
                Interlocked.Increment(ref _curWorkCount); //递增
            }


            if (_curWorkCount % _updataCount == 0) //自动计算
            {
                OccupancyRate = CalculateOccupancyRate(_timesWait, _timesCost, _timesWait.Length);
            }
            return timeCost;
        }

        /// <summary>
        /// 计算一次
        /// </summary>
        public void Calculate()
        {
            OccupancyRate = CalculateOccupancyRate(_timesWait, _timesCost, _timesWait.Length);
        }

        /// <summary>
        /// 计算平均占用率
        /// </summary>
        /// <param name="waitTime">记录等待时间的数组</param>
        /// <param name="costTime">记录消耗时间的数组</param>
        /// <param name="length">要计算的长度</param>
        /// <returns>百分率</returns>
        private double CalculateOccupancyRate(double[] waitTime, double[] costTime, int length)
        {
            double sum_wait = 0;
            double sum_cost = 0;
            length = (waitTime.Length < length) ? waitTime.Length : length;
            for (int i = 0; i < length; i++) {
                sum_wait += waitTime[i];
                sum_cost += costTime[i];
            }
            return sum_cost / (sum_wait + sum_cost) * 100d;
        }
    }
}
