using System;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 这个类主要就是一个timer，定时器函数中包含了1s一次的事件和心跳包相关函数。
    /// </summary>
    public class ClientTimer
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public ClientTimer()
        {
        }

        /// <summary>
        /// 静态构造函数
        /// </summary>
        private static ClientTimer _instance = new ClientTimer();

        /// <summary>
        /// 获得实例
        /// </summary>
        /// <returns></returns>
        public static ClientTimer GetInstance()
        {
            return _instance;
        }

        /// <summary>
        /// 获得实例
        /// </summary>
        /// <returns></returns>
        public static ClientTimer GetInst()
        {
            return _instance;
        }

        /// <summary>
        /// 定时器间隔
        /// </summary>
        public const int KICK_TIME = 1000;

        /// <summary>
        /// 一个定时器
        /// </summary>
        private Timer _timer;

        private int _count3S = 0;

        private bool disposed;

        /// <summary>
        /// 定时器函数的事件，目前定时器的默认时间是1秒一次。
        /// </summary>
        public event Action EventOnTimer = null;

        /// <summary>
        /// 定时器函数的事件，3秒1次
        /// </summary>
        public event Action EventOnTimer3S = null;

        /// <summary>
        /// 初始化并且开始，如果调用了果了Dispose，那么可以重新调用这个函数再次开始。
        /// </summary>
        public void Start()
        {
            if (disposed == false) {
                Dispose();
            }
            _timer = new Timer(new TimerCallback(OnTimerTick));
            _timer.Change(250, KICK_TIME);

            _count3S = 0;
            DxDebug.LogConsole("ClientTimer.Init()：ClientTimer启动!");
        }

        /// <summary>
        /// 定时器函数
        /// </summary>
        /// <param name="state"></param>
        private void OnTimerTick(object state)
        {
            DNClient client = DNClient.GetInstance();

            if (Config.IsAutoHeartbeat && client.IsConnected) {
                float time = (DateTime.Now.Ticks - client.LastMsgSendTickTime) / 10000;
                if (time > Config.HeartBeatSendTime) //如果时间已经超过了那么就发送心跳包
                {
                    //发送一次心跳包
                    SendHeartBeat();
                }
            }

            if (Config.IsAutoHeartbeat && client.IsConnected) {
                //如果15s没有收到心跳包
                float time = (DateTime.Now.Ticks - client.LastMsgReceTickTime) / 10000;
                if (time > Config.HeartBeatCheckTime) {
                    DxDebug.LogWarning("ClientTimer.OnTimerTick()：长时间没有收到心跳包，判断可能已经掉线！");
                    client.Disconnect(); //关闭连接
                }
            }

            //执行事件
            if (EventOnTimer != null) {
                try {
                    EventOnTimer();
                } catch (Exception e) {
                    DxDebug.LogWarning("ClientTimer.OnTimerTick()：执行EventOnTimer事件异常：" + e.Message);
                }
            }

            //执行3秒事件
            if (EventOnTimer3S != null) {
                _count3S += KICK_TIME;
                if (_count3S >= 3 * 1000) //20秒
                {
                    _count3S = 0;
                    try {
                        EventOnTimer3S();
                    } catch (Exception e) {
                        DxDebug.LogWarning("ClientTimer.OnTimerTick()：执行EventOnTimer3S事件异常：" + e.Message);
                        ;
                    }
                }
            }
        }

        /// <summary>
        /// 发送心跳包函数,调用DNClient单例，然后发送。
        /// </summary>
        public void SendHeartBeat()
        {
            DNClient client = DNClient.GetInstance();
            if (client.IsConnected) {
                client.Send(Config.HeartBeatData); //发个心跳包
                DxDebug.Log("ClientTimer：发送 HeartBeatData ~❤");
            }
            else {
                // DxDebug.Log("ClientTimer：发送心跳包 - 但是当前还未连接");
            }
        }

        #region IDisposable implementation

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposed) {
                return;
            }
            if (disposing) {
                // 清理托管资源
            }
            // 清理非托管资源
            try {
                if (_timer != null)
                    _timer.Dispose();
            } catch (Exception) {
            }
            //让类型知道自己已经被释放
            disposed = true;
        }

        #endregion IDisposable implementation
    }
}
