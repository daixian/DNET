using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 负责管理已连接的客户端的类（线程安全）
    /// </summary>
    public class PeerManager : IDisposable
    {
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
        /// <param name="peer"></param>
        internal Peer AddPeer(Peer peer)
        {
            peer.ID = Interlocked.Increment(ref _curID) - 1;
            peer.peerSocket.Name = $"Peer[{peer.ID}]";
            if (_dictPeer.TryAdd(peer.ID, peer)) {
                try {
                    EventAddPeer?.Invoke(peer.ID);
                } catch (Exception e) {
                    LogProxy.LogWarning($"PeerManager.AddPeer(): 执行事件EventAddToken异常: {e.Message}");
                }

                LogProxy.LogDebug($"PeerManager.AddPeer(): 添加了一个客户端. 当前服务器上有 {_dictPeer.Count} 个客户端; ip: {peer.peerSocket.RemoteIP}");
                return peer;
            }
            throw new InvalidOperationException($"添加客户端失败，ID {peer.ID} 已存在");
            return peer;
        }

        /// <summary>
        /// 关闭一个peer，释放资源，但不从列表删除
        /// </summary>
        /// <param name="id"></param>
        /// <param name="type"></param>
        private void DisposePeer(int id, PeerErrorType type)
        {
            var peer = GetPeer(id);
            peer?.Dispose();
        }

        /// <summary>
        /// 删除一个peer，先关闭，再从字典移除，触发事件
        /// </summary>
        /// <param name="id"></param>
        /// <param name="errorType">关闭的错误原因</param>
        public void DeletePeer(int id, PeerErrorType errorType = PeerErrorType.UserManualDelete)
        {
            DisposePeer(id, errorType);

            // 字典中移除
            if (_dictPeer.TryRemove(id, out _)) {
                try {
                    EventDeletePeer?.Invoke(id, errorType);
                } catch (Exception e) {
                    LogProxy.LogWarning($"PeerManager.DeletePeer(): 执行事件EventDeleteToken异常: {e.Message}");
                }

                LogProxy.LogDebug($"PeerManager.DeletePeer(): 关闭了一个客户端. 还有 {_dictPeer.Count} 个客户端，原因 {errorType}");
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

            LogProxy.Log("PeerManager.DeleteAllPeer(): 删除所有客户端！");
            var peers = GetAllPeer();
            foreach (var peer in peers) {
                DeletePeer(peer.ID, PeerErrorType.ClearAllToken);
            }
        }

        /// <summary>
        /// 根据ID获取peer，没有返回null
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Peer GetPeer(int id)
        {
            _dictPeer.TryGetValue(id, out var peer);
            return peer;
        }

        /// <summary>
        /// 获取所有peer数组
        /// </summary>
        /// <returns></returns>
        public Peer[] GetAllPeer()
        {
            return _dictPeer.Values.ToArray();
        }

        /// <summary>
        /// 向所有peer添加发送数据（仅入队，不启动发送）
        /// </summary>
        public void SendToAllPeer(byte[] data, int index, int length)
        {
            foreach (var peer in _dictPeer.Values) {
                peer.peerSocket.AddSendData(data, index, length);
            }
        }

        /// <summary>
        /// 向除了exceptTokenID外所有peer添加发送数据（仅入队，不启动发送）
        /// </summary>
        public void SendToAllPeerExcept(int exceptTokenID, byte[] data, int index, int length)
        {
            foreach (var peer in _dictPeer.Values) {
                if (peer.ID != exceptTokenID) {
                    peer.peerSocket.AddSendData(data, index, length);
                }
            }
        }

        #region IDisposable

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
