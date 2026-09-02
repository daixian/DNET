using System;
using System.Threading;

namespace Flurl.DNET
{
    /// <summary>
    /// 表示一次控制器Action调用的请求上下文。
    /// 它只暴露稳定的协议元数据和连接上下文，不暴露池化Message，因而可以安全跨越异步等待。
    /// </summary>
    public sealed class DnetServerContext
    {
        /// <summary>
        /// 初始化单次请求上下文。
        /// </summary>
        /// <param name="connection">请求所属连接。</param>
        /// <param name="txrId">请求事务ID。</param>
        /// <param name="eventType">请求事件类型。</param>
        internal DnetServerContext(DnetConnectionContext connection, int txrId, int eventType)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            TxrId = txrId;
            EventType = eventType;
        }

        /// <summary>
        /// 获取请求事务ID；零表示不要求自动回复的单向通知。
        /// </summary>
        public int TxrId { get; }

        /// <summary>
        /// 获取用于路由当前Action的事件类型。
        /// </summary>
        public int EventType { get; }

        /// <summary>
        /// 获取请求所属的连接上下文。
        /// </summary>
        public DnetConnectionContext Connection { get; }

        /// <summary>
        /// 获取Peer断开或服务器停止时触发的取消标记。
        /// DNET首版没有客户端主动取消协议，因此客户端本地取消不会触发该标记。
        /// </summary>
        public CancellationToken CancellationToken => Connection.ConnectionClosed;
    }
}
