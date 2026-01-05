using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 负责管理已连接的客户端的类（线程安全）
    /// </summary>
    public class PeerManager : IDisposable
    {
        /// <summary>
        /// 单例实例懒加载容器
        /// </summary>
        private static readonly Lazy<PeerManager> _instance = new Lazy<PeerManager>(() => new PeerManager());

        /// <summary>
        /// 单例
        /// </summary>
        public static PeerManager Inst => _instance.Value;

        /// <summary>
        /// 所有用户的字典，key是Token的ID，线程安全
        /// </summary>
        private readonly ConcurrentDictionary<int, Peer> _dictPeer = new ConcurrentDictionary<int, Peer>();

        /// <summary>
        /// 一个递增的ID计数，会分配给新的Token
        /// </summary>
        private int _curID;

        /// <summary>
        /// 当前用户的计数
        /// </summary>
        public int PeersCount => _dictPeer.Count;

        #region Event

        /// <summary>
        /// 事件：新连接上了一个客户。参数int: Token的id
        /// </summary>
        public event Action<int> EventAddPeer;

        /// <summary>
        /// 事件：删除/关闭了一个客户。参数int: Token的id，参数PeerErrorType: 删除原因
        /// </summary>
        public event Action<int, PeerErrorType> EventDeletePeer;

        #endregion Event

        /// <summary>
        /// 添加一个peer
        /// </summary>
        /// <param name="peer">要加入的peer对象</param>
        internal Peer AddPeer(Peer peer)
        {
            // TODO: peer 可能为 null，需确认调用方是否保证非空
            peer.ID = Interlocked.Increment(ref _curID) - 1;
            peer.peerSocket.Name = $"Peer[{peer.ID}]";
            if (_dictPeer.TryAdd(peer.ID, peer)) {
                try {
                    // 这个事件,但是注意此时Socket还没有好.
                    EventAddPeer?.Invoke(peer.ID);
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerManager.AddPeer():执行事件EventAddToken异常: {e.Message}");
                }
                if (LogProxy.Debug != null)
                    LogProxy.Debug($"PeerManager.AddPeer():添加了一个客户端,当前服务器上有{_dictPeer.Count}个客户端");
                return peer;
            }
            throw new InvalidOperationException($"添加客户端失败，ID {peer.ID} 已存在");
            // return peer;
        }

        /// <summary>
        /// 关闭一个peer，释放资源，但不从列表删除
        /// </summary>
        /// <param name="id">peer的ID</param>
        /// <param name="type">关闭原因</param>
        private void DisposePeer(int id, PeerErrorType type)
        {
            var peer = GetPeer(id);
            peer?.Dispose();
        }

        /// <summary>
        /// 删除一个peer，先关闭，再从字典移除，触发事件
        /// </summary>
        /// <param name="id">peer的ID</param>
        /// <param name="errorType">关闭的错误原因</param>
        public void DeletePeer(int id, PeerErrorType errorType = PeerErrorType.UserManualDelete)
        {
            DisposePeer(id, errorType);

            // 字典中移除
            if (_dictPeer.TryRemove(id, out _)) {
                try {
                    EventDeletePeer?.Invoke(id, errorType);
                } catch (Exception e) {
                    if (LogProxy.Warning != null)
                        LogProxy.Warning($"PeerManager.DeletePeer(): 执行事件EventDeleteToken异常: {e.Message}");
                }
                if (LogProxy.Debug != null)
                    LogProxy.Debug($"PeerManager.DeletePeer(): 关闭了一个客户端. 还有{_dictPeer.Count}个客户端，原因 {errorType}");
            }
        }

        /// <summary>
        /// 删除所有peer
        /// </summary>
        public void DeleteAllPeer()
        {
            if (_dictPeer.IsEmpty) {
                return;
            }
            if (LogProxy.Info != null)
                LogProxy.Info("PeerManager.DeleteAllPeer(): 删除所有客户端！");
            var peers = GetAllPeer();
            foreach (var peer in peers) {
                DeletePeer(peer.ID, PeerErrorType.ClearAllToken);
            }
            ListPool<Peer>.Shared.Recycle(peers);
        }

        /// <summary>
        /// 根据ID获取peer，没有返回null
        /// </summary>
        /// <param name="id">peer的ID</param>
        /// <returns>未找到则返回null</returns>
        public Peer GetPeer(int id)
        {
            _dictPeer.TryGetValue(id, out var peer);
            if (peer == null)
                if (LogProxy.Error != null)
                    LogProxy.Error($"PeerManager.GetPeer():不存在id为{id}的Peer!");
            return peer;
        }

        /// <summary>
        /// 获取所有peer的列表.这个结果可以归还到ListPool中.
        /// </summary>
        /// <returns>当前所有peer的列表</returns>
        public List<Peer> GetAllPeer()
        {
            var peers = ListPool<Peer>.Shared.Get();
            peers.AddRange(_dictPeer.Values);
            return peers;
        }

        /// <summary>
        /// 向所有peer添加发送数据（仅入队，不立刻启动发送）
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据起始位置</param>
        /// <param name="count">数据长度</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        public void SendToAllPeer(byte[] data, int offset, int count,
            Format format = Format.Raw, int txrId = 0, int eventType = 0)
        {
            foreach (var peer in _dictPeer.Values) {
                // TODO: peer.peerSocket 可能为空，需确认连接是否已完整初始化
                peer.peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
            }
        }

        /// <summary>
        /// 向所有peer发送文本数据（仅入队，不启动发送）
        /// </summary>
        /// <param name="text">要发送的文本</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        public void SendToAllPeer(string text,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0)
        {
            try {
                if (string.IsNullOrEmpty(text)) {
                    // TODO: 确认发送空消息时的下游处理是否安全
                    SendToAllPeer(null, 0, 0, format, txrId, eventType);
                    return;
                }
                byte[] data = Encoding.UTF8.GetBytes(text);
                SendToAllPeer(data, 0, data.Length, format, txrId, eventType);
            } catch (Exception e) {
                if (LogProxy.Error != null)
                    LogProxy.Error($"DNServer.SendToAllPeer():发送文本异常 {e}");
            }
        }

        /// <summary>
        /// 向除了exceptTokenID外所有peer添加发送数据（仅入队，不启动发送）
        /// </summary>
        /// <param name="exceptPeerID">排除的peer ID</param>
        /// <param name="data">要发送的数据</param>
        /// <param name="offset">数据起始位置</param>
        /// <param name="count">数据长度</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        public void SendToAllPeerExcept(int exceptPeerID, byte[] data, int offset, int count,
            Format format = Format.Raw, int txrId = 0, int eventType = 0)
        {
            foreach (var peer in _dictPeer.Values) {
                if (peer.ID != exceptPeerID) {
                    // TODO: peer.peerSocket 可能为空，需确认连接是否已完整初始化
                    peer.peerSocket.AddSendData(data, offset, count, format, txrId, eventType);
                }
            }
        }

        /// <summary>
        /// 向除了exceptTokenID外所有peer发送文本数据（仅入队，不启动发送）
        /// </summary>
        /// <param name="exceptPeerID">排除的peer ID</param>
        /// <param name="text">要发送的文本</param>
        /// <param name="format">数据格式</param>
        /// <param name="txrId">事务id</param>
        /// <param name="eventType">消息类型</param>
        public void SendToAllPeerExcept(int exceptPeerID, string text,
            Format format = Format.Raw,
            int txrId = 0,
            int eventType = 0)
        {
            try {
                if (string.IsNullOrEmpty(text)) {
                    // TODO: 确认发送空消息时的下游处理是否安全
                    SendToAllPeerExcept(exceptPeerID, null, 0, 0, format, txrId, eventType);
                    return;
                }
                byte[] data = Encoding.UTF8.GetBytes(text);
                SendToAllPeerExcept(exceptPeerID, data, 0, data.Length, format, txrId, eventType);
            } catch (Exception e) {
                if (LogProxy.Error != null)
                    LogProxy.Error($"DNServer.SendToAllPeerExcept():发送文本异常 {e}");
            }
        }

        #region IDisposable

        /// <summary>
        /// 标记是否已释放
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            EventAddPeer = null;
            EventDeletePeer = null;
            _dictPeer.Clear();
        }

        #endregion
    }
}
