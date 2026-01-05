using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 工作处理器接口，用于定义处理消息的方法。
    /// </summary>
    /// <typeparam name="T">消息类型。</typeparam>
    public interface IWorkHandler<T>
    {
        /// <summary>
        /// 处理指定的消息。
        /// </summary>
        /// <param name="msg">要处理的消息。</param>
        /// <param name="waitTimeMs">这条消息等待了多长时间(ms)。</param>
        void Handle(ref T msg, double waitTimeMs);
    }

    /// <summary>
    /// 表示一个工作项，包含数据、处理方式及发布时间。
    /// 使用 Work 避免与 Task 冲突，同时保持“任务”语义。
    /// </summary>
    /// <typeparam name="T">工作项的数据类型。</typeparam>
    public struct WorkItem<T>
    {
        /// <summary>
        /// 消息参数数据。
        /// </summary>
        public T data;

        /// <summary>
        /// 处理该工作项的处理器。
        /// </summary>
        public IWorkHandler<T> handler;

        /// <summary>
        /// 发布该工作项的时间戳（使用 Stopwatch.GetTimestamp() 记录）。
        /// </summary>
        public long postTimestamp;

        /// <summary>
        /// 获取当前等待时间（毫秒）。
        /// </summary>
        public double WaitTimeMs => (Stopwatch.GetTimestamp() - postTimestamp) * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>
    /// 工作线程类，负责处理队列中的工作项。
    /// </summary>
    /// <typeparam name="T">工作项的数据类型。</typeparam>
    public class WorkThread<T>
    {
        /// <summary>
        /// 线程实例
        /// </summary>
        private readonly Thread _thread;

        /// <summary>
        /// 工作队列，存储待处理的工作项
        /// </summary>
        private readonly ConcurrentQueue<WorkItem<T>> _queue = new ConcurrentQueue<WorkItem<T>>();

        /// <summary>
        /// 信号量，用于唤醒线程处理任务
        /// </summary>
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);

        /// <summary>
        /// 控制线程是否继续运行
        /// </summary>
        private volatile bool _running = true;

        /// <summary>
        /// 构造函数，初始化并启动工作线程。
        /// </summary>
        /// <param name="name">线程名称，默认为 MessageLoopThread。</param>
        public WorkThread(string name = "MessageLoopThread")
        {
            _thread = new Thread(Loop) {
                IsBackground = true, // 设置为后台线程
                Name = name
            };
            _thread.Start(); // 启动线程
        }

        /// <summary>
        /// 线程主循环方法，持续监听队列并处理任务。
        /// </summary>
        private void Loop()
        {
            if (LogProxy.Debug != null)
                LogProxy.Debug($"WorkThread.Loop():线程[{Thread.CurrentThread.Name}]开始工作");

            while (_running) {
                _signal.WaitOne(); // 等待信号唤醒
                ThreadAnalyzer.Inst.BeginWork(); // 开始工作统计

                // 处理队列中所有任务
                while (_queue.TryDequeue(out var msg)) {
                    try {
                        // 执行处理逻辑
                        msg.handler.Handle(ref msg.data, msg.WaitTimeMs);
                    } catch (Exception e) {
                        if (LogProxy.Warning != null)
                            LogProxy.Warning($"WorkThread.Loop():[{Thread.CurrentThread.Name}] 工作异常: {e}");
                    }
                }

                ThreadAnalyzer.Inst.EndWork(); // 结束工作统计
            }
        }

        /// <summary>
        /// 向队列中投递一个新的工作项。
        /// </summary>
        /// <param name="data">要处理的数据。</param>
        /// <param name="handler">处理该数据的处理器。</param>
        public void Post(in T data, IWorkHandler<T> handler)
        {
            // TODO: handler 为空时会导致工作线程抛异常，必要时可加参数校验
            // 创建工作项并设置发布时间
            _queue.Enqueue(new WorkItem<T> {
                data = data,
                handler = handler,
                postTimestamp = Stopwatch.GetTimestamp()
            });
            _signal.Set(); // 唤醒线程处理
        }

        /// <summary>
        /// 清空当前工作队列中的所有任务。
        /// </summary>
        public void ClearQueue()
        {
            // 清空队列
            while (_queue.TryDequeue(out var _)) {
            }
        }

        /// <summary>
        /// 强制停止工作线程。
        /// </summary>
        public void Stop()
        {
            ClearQueue(); // 清空队列
            _running = false; // 停止运行标志
            _signal.Set(); // 唤醒线程以退出循环
            _thread.Join(); // 等待线程结束
        }
    }
}
