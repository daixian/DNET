using System;

namespace DNET
{
    /// <summary>
    /// 服务器的工作状态类，依赖于ServerTimer来工作，每秒一次的统计频率
    /// </summary>
    public class ServerStatus
    {
        /// <summary>
        /// 构造函数要记录它的归属服务器
        /// </summary>
        /// <param name="server"></param>
        internal ServerStatus(DNServer server)
        {
            _dnServer = server;
        }

        /// <summary>
        /// 它所属的服务器
        /// </summary>
        private DNServer _dnServer;

        /// <summary>
        /// 接收到的消息条数统计,由DNServer对象直接修改
        /// </summary>
        public int CountReceive = 0;

        /// <summary>
        /// 发送出的消息条数统计,由DNServer对象直接修改
        /// </summary>
        public int CountSend = 0;

        /// <summary>
        /// 接收到的消息字节数统计,由DNServer对象直接修改
        /// </summary>
        public int CountReceiveBytes = 0;

        /// <summary>
        /// 发送出的消息字节数统计,由DNServer对象直接修改
        /// </summary>
        public int CountSendBytes = 0;

        /// <summary>
        /// 最近10秒的接收消息计数
        /// </summary>
        public DQueue<int> CountReceive10s = new DQueue<int>(10);

        /// <summary>
        /// 最近10秒的发送消息计数
        /// </summary>
        public DQueue<int> CountSend10s = new DQueue<int>(10);

        /// <summary>
        /// 最近10秒的接收消息计数
        /// </summary>
        public DQueue<int> CountReceiveBytes10s = new DQueue<int>(10);

        /// <summary>
        /// 最近10秒的发送消息计数
        /// </summary>
        public DQueue<int> CountSendBytes10s = new DQueue<int>(10);

        /// <summary>
        /// 是否打印当前一秒
        /// </summary>
        public bool isPrintCur1s = true;

        /// <summary>
        /// 绑定一个服务器定时器ServerTimer，它是一个1秒一工作的定时器
        /// </summary>
        /// <param name="timer"></param>
        public void BindTimer(ServerTimer timer)
        {
            timer.EventOnTimer += OnUpdate;
        }

        /// <summary>
        /// 统计数据清空
        /// </summary>
        public void Clear()
        {
            CountReceive = 0;
            CountSend = 0;
            CountReceive10s.Clear();
            CountSend10s.Clear();
        }

        /// <summary>
        /// 保存的上一秒的计数,用来计算最近一秒内的消息次数
        /// </summary>
        private int _lastCountReceive = 0;

        /// <summary>
        /// 保存的上一秒的计数,用来计算最近一秒内的消息次数
        /// </summary>
        private int _lastCountSend = 0;

        /// <summary>
        /// 保存的上一秒的计数,用来计算最近一秒内的数据流量长度
        /// </summary>
        private int _lastCountReceiveBytes = 0;

        /// <summary>
        /// 保存的上一秒的计数,用来计算最近一秒内的数据流量长度
        /// </summary>
        private int _lastCountSendBytes = 0;

        /// <summary>
        /// 在定时器中调用的函数
        /// </summary>
        private void OnUpdate()
        {
            int cur1sReceive = CountReceive - _lastCountReceive;
            int cur1sSend = CountSend - _lastCountSend;
            int cur1sReceiveBytes = CountReceiveBytes - _lastCountReceiveBytes;
            int cur1sSendBytes = CountSendBytes - _lastCountSendBytes;

            _lastCountReceive = CountReceive;
            _lastCountSend = CountSend;
            _lastCountReceiveBytes = CountReceiveBytes;
            _lastCountSendBytes = CountSendBytes;

            CountReceive10s.EnqueueMaxLimit(cur1sReceive); //添加这一秒的结果到末尾
            CountSend10s.EnqueueMaxLimit(cur1sSend); //添加这一秒的结果到末尾
            CountReceiveBytes10s.EnqueueMaxLimit(cur1sReceiveBytes); //添加这一秒的结果到末尾
            CountSendBytes10s.EnqueueMaxLimit(cur1sSendBytes); //添加这一秒的结果到末尾

            if (isPrintCur1s && _dnServer.IsStarted) //如果设置了打印这一秒,并且服务器在工作
            {
                DxDebug.LogConsole(String.Format("ServerStatus.OnUpdate()：一秒内接收/发送:{0}/{2}条,总{4}/{5}条,消息队列长{6} ,{1:F1}/{3:F1}kB.",
                    cur1sReceive, cur1sReceiveBytes / 1000.0f, cur1sSend, cur1sSendBytes / 1000.0f, CountReceive, CountSend, _dnServer.msgQueueLength));
            }
        }
    }
}
