using System;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 工作线程模型,一组线程等待一个信号量队列
    /// <example>创建的时候会自动开启:
    /// <code>
    /// _workThread = new WorkThread(1024 * 512, 2, "LogicManager", 8192);
    /// </code>
    /// </example>
    /// </summary>
    public class WorkThread : IDisposable
    {
        /// <summary>
        /// 构造函数：输入参数是最大消息队列长度和线程个数。 会自动开始工作.
        /// </summary>
        /// <param name="MaxMsgQueue"> 最大消息队列长度. </param>
        /// <param name="threadCount"> 线程个数. </param>
        /// <param name="name">        这一组工作线程的名字. </param>
        /// <param name="initMsgQueueSize">初始队列长度. </param>
        public WorkThread(int MaxMsgQueue, int threadCount, string name = "WorkThread", int initMsgQueueSize = 32)
        {
            if (MaxMsgQueue > 0)
            {
                MSG_QUEUE_CAPACITY = MaxMsgQueue;
                if (MaxMsgQueue < initMsgQueueSize)//如果输入的最大长度还小于初始长度
                {
                    _initMsgQueueSize = MaxMsgQueue;//那么就等于最大长度
                }
                _initMsgQueueSize = initMsgQueueSize;
            }
            if (threadCount > 0)
            {
                _threadCount = threadCount;
            }

            //自动开启
            Start();
        }

        /// <summary>
        /// 构造函数，会自动开始工作。
        /// </summary>
        public WorkThread()
        {
            //自动开启
            Start();
        }

        /// <summary>
        /// 这一组工作线程的名字
        /// </summary>
        private string name = "WorkThread";

        /// <summary>
        /// 消息队列最大数
        /// </summary>
        private int MSG_QUEUE_CAPACITY = 32;

        /// <summary>
        /// 初始消息队列长度
        /// </summary>
        private int _initMsgQueueSize = 32;

        /// <summary>
        /// 总共的工作线程的个数
        /// </summary>
        private int _threadCount = 2;

        /// <summary>
        /// 工作线程
        /// </summary>
        private Thread[] _workThread = null;

        /// <summary>
        /// 线程的ID
        /// </summary>
        private int[] _workThreadID = null;

        /// <summary>
        /// 用来强行停止线程
        /// </summary>
        private bool _isRun = false;

        /// <summary>
        /// 对应一条消息的信号量
        /// </summary>
        private Semaphore _msgSemaphore;

        /// <summary>
        /// 这个线程待处理的消息队列
        /// </summary>
        private DQueue<IWorkMsg> _msgQueue;

        /// <summary>
        /// 当前的信号量计数
        /// </summary>
        private int _curSemCount = 0;

        /// <summary>
        /// 队列的峰值长度
        /// </summary>
        private int _msgQueuePeakLength = 0;

        /// <summary>
        /// 这个类是否已经被释放掉
        /// </summary>
        private bool _disposed = true;

        #region 统计工作状态

        /// <summary>
        /// 已经处理过的消息的计数
        /// </summary>
        public int _procMsgCount;

        /// <summary>
        /// 工作线程的工作状态
        /// </summary>
        private bool[] _isWorking = null;

        /// <summary>
        /// 工作线程的上一次的工作时间
        /// </summary>
        private long[] _lastWorkTime = null;

        #endregion 统计工作状态

        #region Property

        /// <summary>
        /// 服务器队列的最大长度
        /// </summary>
        public int msgQueueCapacity
        {
            get { return MSG_QUEUE_CAPACITY; }
        }

        /// <summary>
        /// 队列的峰值长度
        /// </summary>
        public int msgQueuePeakLength
        {
            get { return _msgQueuePeakLength; }
        }

        #endregion Property

        /// <summary>
        /// 工作线程启动
        /// </summary>
        public void Start()
        {
            try
            {
                //标记线程可以运行
                _isRun = true;

                if (_disposed)
                {
                    //标记自己已经有了资源申请
                    _disposed = false;
                    try
                    {
                        DxDebug.LogConsole("WorkThread.Start():这个类对象经被释放或刚刚构造，重新初始化");
                        _msgQueue = new DQueue<IWorkMsg>(MSG_QUEUE_CAPACITY, _initMsgQueueSize);
                        _msgSemaphore = new Semaphore(0, 64);//由于AddMessage的改动，这里只需要是随便一个数既可

                        _isWorking = new bool[_threadCount];
                        _lastWorkTime = new long[_threadCount];
                        _workThreadID = new int[_threadCount];

                        _workThread = new Thread[_threadCount];
                        for (int i = 0; i < _threadCount; i++)
                        {
                            _workThread[i] = new Thread(DoWork);
                            _workThread[i].IsBackground = true;
                            //工作线程的优先级(影响不大)
                            _workThread[i].Priority = ThreadPriority.Highest;
                            _workThread[i].Name = name + i;
                            //记录线程ID
                            _workThreadID[i] = _workThread[i].ManagedThreadId;
                            _workThread[i].Start(); //启动线程
                        }
                    }
                    catch (Exception e)
                    {
                        DxDebug.LogError("WorkThread.Start():构造失败！异常:" + e.Message);
                        Dispose();
                    }
                }
                else
                {
                    Dispose();
                    Start();
                }
            }
            catch (Exception e)
            {
                DxDebug.LogError("WorkThread.Start():异常：" + e.Message);
            }
        }

        /// <summary>
        /// 添加一个工作任务到消息队列，提供给这些线程来处理
        /// </summary>
        /// <param name="msg"></param>
        public void AddMessage(IWorkMsg msg)
        {
            try
            {
                if (_msgQueue.EnqueueMaxLimit(msg))
                {
                    if (_curSemCount < 1)//如果当前的信号量剩余不多的时候
                    {
                        Interlocked.Increment(ref _curSemCount);
                        _msgSemaphore.Release();// 释放信号量
                    }
                }
                else
                {
                    DxDebug.LogWarning("WorkThread.AddMessage():大于工作线程的能力了，丢弃了一条消息！");
                }

                if (_msgQueuePeakLength < _msgQueue.Count)
                {
                    _msgQueuePeakLength = _msgQueue.Count;//记录当前的峰值长度
                }
            }
            //catch (SemaphoreFullException)
            //{
            //    DxDebug.LogError("WorkThread.AddMessage():大于工作线程的能力了：");

            //    throw;
            //}
            catch (Exception e)
            {
                DxDebug.LogError("WorkThread.AddMessage():异常：" + e.Message);
                throw;
            }
        }

        /// <summary>
        /// 检查线程工作超时。超时时间的单位为ms
        /// </summary>
        /// <param name="index">线程的index</param>
        /// <param name="time_ms">超时时间，单位为毫秒</param>
        /// <returns></returns>
        public bool CheckThreadWorkOverTime(int index, double time_ms)
        {
            if (index >= _threadCount || index < 0)
            {
                return false;
            }

            if (_isWorking[index] == true)
            {
                double costTime = (DateTime.Now.Ticks - _lastWorkTime[index]) / 10000;//毫秒
                if (costTime >= time_ms)
                {
                    return true;//返回这个线程工作超时
                }
            }
            return false;
        }

        /// <summary>
        /// 得到一个线程当前这次工作的耗时，如果当前线程是空闲，那么返回0单位毫秒
        /// </summary>
        /// <param name="index">线程index</param>
        /// <returns>某个线程的当前工作耗时</returns>
        public double GetCostTime(int index)
        {
            if (index >= _threadCount || index < 0)
            {
                return 0;
            }

            if (_isWorking[index] == true)
            {
                double costTime = (DateTime.Now.Ticks - _lastWorkTime[index]) / 10000;//毫秒
                return costTime;
            }
            return 0;
        }

        /// <summary>
        /// 清除这个类对象的里的状态记录，如消息队列达到的最大长度msgQueuePeakLength
        /// </summary>
        public void ClearStatus()
        {
            _msgQueuePeakLength = 0;
        }

        /// <summary>
        /// 线程的工作函数
        /// </summary>
        private void DoWork()
        {
            DxDebug.LogConsole("WorkThread.DoWork():工作线程启动！");

            while (_isRun)
            {
                IWorkMsg msg = null;
                try
                {
                    //记录线程空闲
                    RecThreadStatus(Thread.CurrentThread.ManagedThreadId, false);

                    _msgSemaphore.WaitOne();
                    Interlocked.Decrement(ref _curSemCount);

                    //记录线程开始工作
                    RecThreadStatus(Thread.CurrentThread.ManagedThreadId, true);

                    while (true)
                    {
                        msg = _msgQueue.Dequeue();
                        if (msg != null)
                        {
                            //递增计数
                            Interlocked.Increment(ref _procMsgCount);
                            try
                            {
                                //取一条消息进行执行
                                msg.DoWork();
                            }
                            catch (Exception e)
                            {
                                DxDebug.LogWarning("WorkThread.DoWork():执行msg异常：" + msg.Name + "异常信息：" + e.Message);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    if (msg != null)
                        DxDebug.LogError("WorkThread.DoWork():异常：" + msg.Name + "异常信息：" + e.Message);
                    else
                        DxDebug.LogError("WorkThread.DoWork():异常：目前IWorkMsg为null(可能空闲),异常信息：" + e.Message);
                }
            }
        }

        /// <summary>
        /// 记录线程的状态
        /// </summary>
        /// <param name="managedThreadId">线程标识符id</param>
        /// <param name="isWorking">要记录线程当前的工作状态是多少</param>
        private void RecThreadStatus(int managedThreadId, bool isWorking)
        {
            int index = 0;
            for (int i = 0; i < _workThreadID.Length; i++)
            {
                if (managedThreadId == _workThreadID[i])//去id列表里找到index
                {
                    index = i;
                    break;
                }
            }

            _isWorking[index] = isWorking;//标记工作状态
            if (isWorking == true)
            {
                _lastWorkTime[index] = DateTime.Now.Ticks;//标记当前时间
            }
        }

        #region IDisposable implementation

        /// <summary>
        /// Dispose函数
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            try
            {
                DxDebug.LogConsole("WorkThread.DoWork():工作线程关闭！");
                _isRun = false;

                //最先去把线程关了
                for (int i = 0; i < _threadCount; i++)
                {
                    //把线程关了
                    if (_workThread[i] != null && _workThread[i].IsAlive)
                    {
                        try
                        {
                            _workThread[i].Abort();
                        }
                        catch (Exception e)
                        {
                            DxDebug.LogWarning("WorkThread.Dispose():异常 _workThread[" + i + "].Abort();" + e.Message);
                        }
                    }
                }

                if (disposing)
                {
                    // 清理托管资源
                    if (_msgQueue != null)
                        _msgQueue.Clear();
                }
                // 清理非托管资源
                if (_msgSemaphore != null)
                    _msgSemaphore.Close();
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("WorkThread.Dispose():释放异常" + e.Message);
            }
            //让类型知道自己已经被释放
            _disposed = true;
        }

        #endregion IDisposable implementation
    }
}