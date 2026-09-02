using System;
using System.Collections.Concurrent;
using System.Threading;
using DNET;

namespace Flurl.DNET
{
    /// <summary>
    /// 表示一个DNET客户端连接在控制器框架中的稳定运行时上下文。
    /// 上下文由服务器按Peer创建一次，允许单例控制器通过Items保存线程安全的连接级会话数据。
    /// </summary>
    public sealed class DnetConnectionContext
    {
        /// <summary>
        /// 当前连接对应的底层Peer，仅供服务端调度和回复使用。
        /// </summary>
        private readonly Peer _peer;

        /// <summary>
        /// 连接关闭时触发的取消源。
        /// </summary>
        private readonly CancellationTokenSource _connectionClosedSource =
            new CancellationTokenSource();

        /// <summary>
        /// 标记当前上下文是否已经释放。
        /// </summary>
        private int _disposed;

        /// <summary>
        /// 使用已经建立的Peer初始化连接上下文。
        /// </summary>
        /// <param name="peer">当前连接对应的Peer。</param>
        internal DnetConnectionContext(Peer peer)
        {
            _peer = peer ?? throw new ArgumentNullException(nameof(peer));
            PeerId = peer.ID;
            RemoteIP = peer.peerSocket == null ? null : peer.RemoteIP;
            Items = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        }

        /// <summary>
        /// 获取当前连接的Peer ID。
        /// </summary>
        public int PeerId { get; }

        /// <summary>
        /// 获取连接建立时记录的远端地址。
        /// </summary>
        public string RemoteIP { get; }

        /// <summary>
        /// 获取用于保存认证信息等连接级状态的线程安全字典。
        /// </summary>
        public ConcurrentDictionary<string, object> Items { get; }

        /// <summary>
        /// 获取Peer断开或服务器停止时触发的取消标记。
        /// </summary>
        public CancellationToken ConnectionClosed => _connectionClosedSource.Token;

        /// <summary>
        /// 获取仍由DNET管理的底层Peer，不向控制器公开以保持接收事件独占。
        /// </summary>
        internal Peer Peer => _peer;

        /// <summary>
        /// 通知当前连接已经关闭。重复调用不会重复取消。
        /// </summary>
        internal void Cancel()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            try {
                _connectionClosedSource.Cancel();
            } catch (ObjectDisposedException) {
                // Dispose与断线通知并发时，已释放取消源等同于已经完成取消。
            } catch (Exception exception) {
                // CancellationToken回调属于控制器代码，隔离其异常以保证连接清理能够继续。
                if (LogProxy.Warning != null)
                    LogProxy.Warning($"DnetConnectionContext.Cancel():取消回调异常 {exception}");
            }
        }

        /// <summary>
        /// 取消并释放连接取消源。重复调用不会产生副作用。
        /// </summary>
        internal void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try {
                try {
                    _connectionClosedSource.Cancel();
                } catch (Exception exception) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"DnetConnectionContext.Dispose():取消回调异常 {exception}");
                }
            } finally {
                _connectionClosedSource.Dispose();
                Items.Clear();
            }
        }
    }
}
