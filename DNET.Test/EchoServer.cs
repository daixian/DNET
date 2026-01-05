using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace DNET.Test
{
    /// <summary>
    /// 回显服务器测试类，用于测试服务器端的基本功能
    /// </summary>
    public class EchoServer
    {
        private readonly DNServer server;

        /// <summary>
        /// 初始化回显服务器实例
        /// </summary>
        /// <param name="server">要测试的DNServer实例</param>
        public EchoServer(DNServer server)
        {
            this.server = server;
        }

        /// <summary>
        /// 获取服务器接收到并处理的消息数量
        /// </summary>
        public int ServerReceiveCount { get; private set; }

        /// <summary>
        /// 是否立即发送
        /// </summary>
        public bool Immediately { get; set; } = true;

        public class PeerUser
        {
            public int ReceiveCount;
        }

        /// <summary>
        /// 启动服务器
        /// </summary>
        /// <param name="port">监听的端口号</param>
        /// <param name="isFastResponse"></param>
        public void Start(int port, bool isFastResponse = true)
        {
            // 设置接收数据事件处理
            server.EventPeerReceData += (s, peer) => {
                if (peer.User == null) {
                    peer.User = new PeerUser();
                }
                // dx: 这个锁是为了保证按顺序发送回去
                // lock (peer) {
                PeerUser user = peer.User as PeerUser;
                List<Message> msgList = peer.GetReceiveData();
                if (msgList == null || msgList.Count == 0) return;

                foreach (var msg in msgList) {
                    // 这是小线程的回调事件,server不应该sleep
                    // while (peer.IsSendQueueOverflow())
                    //     Thread.Sleep(1);

                    if (msg.Format == Format.Text) {
                        LogProxy.Info($"收到文本数据:{msg.Text}");
                    }
                    if (msg.TxrId != user.ReceiveCount) {
                        LogProxy.Error($"[{peer.Name}]收到数据包序号错误,当前 ID/事务/接收:{msg.Id}/{msg.TxrId}/{user.ReceiveCount}");
                    }
                    // 回发接收到的数据
                    peer.AddSendData(msg.data.Bytes, 0, msg.data.Length,
                        format: msg.Format,
                        txrId: msg.TxrId);

                    user.ReceiveCount++;
                    ServerReceiveCount++;
                }
                s.TryStartSend(peer, forceUseWorkThread: true); // 此时再合并发送.
                msgList.RecycleAllItems();
                // }
            };

            // 尝试启动服务器直到成功
            while (true) {
                server.Close(false);

                // 这个服务器压力很大,用工作线程处理每个消息吧isFastResponse=false
                server.IsFastResponse = isFastResponse;

                server.Start(port);
                if (server.IsStarted)
                    break;
            }
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public void Stop()
        {
            server.Close();
        }
    }
}
