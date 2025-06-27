using System.Threading;
using NUnit.Framework;

namespace DNET.Test
{
    public class EchoServer
    {
        private readonly DNServer server;

        public EchoServer(DNServer server)
        {
            this.server = server;
        }

        public int ServerReceiveCount { get; private set; }

        public void Start(int port)
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugLog = false;
            server.Close();

            server.EventPeerReceData += peer => {
                var msgs = peer.GetReceiveData();
                if (msgs == null || msgs.Count == 0) return;

                foreach (Message msg in msgs) {
                    while (peer.IsSendQueueOverflow())
                        Thread.Sleep(1);

                    server.Send(peer, msg.data, 0, msg.data.Length, txrId: msg.TxrId);
                    Assert.That(msg.TxrId, Is.EqualTo(ServerReceiveCount));
                    ServerReceiveCount++;
                }
            };

            while (true) {
                server.Close(false);
                server.Start(port);
                if (server.IsStarted)
                    break;
            }
        }

        public void Stop()
        {
            server.Close();
        }
    }
}
