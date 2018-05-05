using System;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 一个默认的服务器Timer单例类，在 DNServer()的构造函数中会自动启动它。
    /// </summary>
    public class ServerTimer : IDisposable
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public ServerTimer()
        {
        }

        /// <summary>
        /// 静态构造函数
        /// </summary>
        private static ServerTimer _instance = new ServerTimer();

        /// <summary>
        /// 获得实例
        /// </summary>
        /// <returns></returns>
        public static ServerTimer GetInstance()
        {
            return _instance;
        }

        /// <summary>
        /// 获得实例
        /// </summary>
        /// <returns></returns>
        public static ServerTimer GetInst()
        {
            return _instance;
        }

        /// <summary>
        /// 定时器函数的事件，目前定时器的默认时间是1秒一次。
        /// </summary>
        public event Action EventOnTimer = null;

        /// <summary>
        /// 定时器间隔，目前是1s，最好不要修改这个时间
        /// </summary>
        public const int KICK_TIME = 1000;

        /// <summary>
        /// 一个定时器
        /// </summary>
        private Timer _timer;

        /// <summary>
        /// 心跳包检测计时器
        /// </summary>
        private int _countHeartBeatCheckTime = 0;

        /// <summary>
        /// 心跳包发送计时器
        /// </summary>
        private int _countHeartBeatSendTime = 0;

        /// <summary>
        /// 上一次进心跳包检查的时间。
        /// 如果从这个时间开始token都一直没有收到消息，那么就是心跳包超时。
        /// </summary>
        private long _checkTickTime = 0;

        /// <summary>
        /// disposed标志
        /// </summary>
        private bool disposed = true;

        /// <summary>
        /// 初始化并且开始，如果调用了果了Dispose，那么可以重新调用这个函数再次开始。
        /// 如果已经初始化，那么不会执行。
        /// </summary>
        public void Start()
        {
            //这里不要强制释放了
            //if (disposed == false)
            //{
            //    Dispose();
            //}
            if (disposed)
            {
                _timer = new Timer(new TimerCallback(OnTimerTick));
                _timer.Change(250, KICK_TIME);
                _checkTickTime = DateTime.Now.Ticks;

                DxDebug.LogConsole("ServerTimer.Init()：ServerTimer启动!");
            }
        }

        /// <summary>
        /// 定时器函数
        /// </summary>
        /// <param name="state"></param>
        private void OnTimerTick(object state)
        {
            try
            {
                //如果自动心跳包功能打开了
                if (Config.IsAutoHeartbeat == true)
                {
                    CheckOffLineAndSend();
                }

                //执行事件
                if (EventOnTimer != null)
                {
                    try
                    {
                        EventOnTimer();
                    }
                    catch (Exception e)
                    {
                        DxDebug.LogWarning("ServerTimer.OnTimerTick()：执行EventOnTimer事件异常：" + e.Message); ;
                    }
                }
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("ServerTimer.OnTimerTick()：异常：" + e.Message);
            }
        }

        #region function module

        /// <summary>
        /// 检查用户是否其实已经离线，在OnTimerTick函数里调用
        /// </summary>
        private void CheckOffLineAndSend()
        {
            _countHeartBeatCheckTime += KICK_TIME;
            if (_countHeartBeatCheckTime >= Config.HeartBeatCheckTime) //15秒*1
            {
                Interlocked.Exchange(ref _countHeartBeatCheckTime, 0);//这里要立马置零，防止后面的代码执行的过久，再次进入kick

                Token[] tokens = TokenManager.GetInstance().GetAllToken();
                if (tokens != null)
                {
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        Token token = tokens[i];
                        if (token.LastMsgReceTickTime < _checkTickTime) //如果从上次的进入这里的时间之后一直都没有收到消息
                        {
                            DxDebug.LogConsole("ServerTimer.CheckOffLine()：一个用户长时间没有收到心跳包，被删除!");

                            TokenManager.GetInstance().DeleteToken(token.ID, TokenErrorType.HeartBeatTimeout);//删除这个用户
                        }
                    }
                }

                _checkTickTime = DateTime.Now.Ticks;
            }

            _countHeartBeatSendTime += KICK_TIME;
            if (_countHeartBeatSendTime >= Config.HeartBeatSendTime) //5秒进一次
            {
                Interlocked.Exchange(ref _countHeartBeatSendTime, 0);//这里要立马置零，防止后面的代码执行的过久，再次进入kick

                long subTimeTick = DateTime.Now.Ticks - 10000 * Config.HeartBeatSendTime;//计算得到的门限时间

                Token[] tokens = TokenManager.GetInstance().GetAllToken();
                if (tokens != null)
                {
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        Token token = tokens[i];
                        if (token.disposed == false && token.LastMsgSendTickTime < subTimeTick) //如果从上次的进入这里的时间之后一直都没有发消息
                        {
                            //应该发送一条心跳包
                            token.AddSendData(Config.HeartBeatData, 0, Config.HeartBeatData.Length);
                        }
                    }
                    DNServer.GetInstance().SendAll();//整体发送
                }
            }
        }

        #endregion function module

        #region IDisposable implementation

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            DxDebug.LogConsole("ServerTimer.Dispose()：ServerTimer进入了Dispose()!");
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
            }
            // 清理非托管资源
            try
            {
                if (_timer != null)
                    _timer.Dispose();
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("ServerTimer.Dispose()：异常_timer.Dispose()" + e.Message);
            }
            //让类型知道自己已经被释放
            disposed = true;
        }

        #endregion IDisposable implementation
    }
}