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

        /// <summary>
        /// 启动服务器
        /// </summary>
        /// <param name="port">监听的端口号</param>
        public void Start(int port, bool isFastResponse = true)
        {
            server.Close();

            // 这个服务器压力很大,用工作线程处理每个消息吧isFastResponse=false
            server.IsFastResponse = isFastResponse;

            // 设置接收数据事件处理
            server.EventPeerReceData += (s, peer) => {
                var msgList = peer.GetReceiveData();
                if (msgList == null || msgList.Count == 0) return;

                foreach (var msg in msgList) {
                    // 这是小线程的回调事件,server不应该sleep
                    // while (peer.IsSendQueueOverflow())
                    //     Thread.Sleep(1);

                    if (msg.Format == Format.Text) {
                        LogProxy.Log($"收到文本数据:{msg.Text}");
                    }
                    // 回发接收到的数据
                    peer.AddSendData(msg.data.buffer, 0, msg.data.Length,
                        format: msg.Format,
                        txrId: msg.TxrId);

                    ServerReceiveCount++;
                }
                s.TryStartSend(peer); // 此时再合并发送.
                msgList.RecycleAllItems();
            };

            // 尝试启动服务器直到成功
            while (true) {
                server.Close(false);
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
