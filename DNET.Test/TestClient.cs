using System;
using System.Threading;
using NUnit.Framework;

namespace DNET.Test
{
    public class TestClient
    {
        private readonly DNClient client;

        public TestClient(DNClient client)
        {
            this.client = client;
        }

        public int ReceiveCount { get; private set; }
        public int SendCount { get; private set; }

        public void Connect(string ip, int port)
        {
            client.Close();
            client.Connect(ip, port);
            // 等待连接成功
            int retry = 0;
            while (!client.IsConnected && retry++ < 200) Thread.Sleep(20);
            Assert.IsTrue(client.IsConnected, $"{client.Name} 连接失败");
        }

        public void SendAndCheckEcho(byte[] sendData, int batchCount, int repeatCount, bool immediately)
        {
            for (int c = 0; c < repeatCount; c++) {
                for (int i = 0; i < batchCount; i++) {
                    while (client.IsSendQueueOverflow())
                        Thread.Sleep(1);

                    client.Send(sendData, 0, sendData.Length, Format.Raw, SendCount, 0, immediately);
                    SendCount++;
                }

                DateTime startTime = DateTime.UtcNow;
                TimeSpan timeout = TimeSpan.FromSeconds(5);

                while (ReceiveCount != SendCount) {
                    if (DateTime.UtcNow - startTime > timeout)
                        Assert.Fail($"超时未收到全部消息：已收到 {ReceiveCount} 条，预期 {SendCount} 条");

                    Thread.Sleep(1);
                    var datas = client.GetReceiveData();
                    if (datas != null)
                        foreach (Message msg in datas) {
                            Assert.That(msg.data.Length == sendData.Length);
                            Assert.That(msg.TxrId, Is.EqualTo(ReceiveCount));

                            for (int j = 4; j < msg.data.Length; j++)
                                Assert.That(msg.data[j] == sendData[j]);

                            ReceiveCount++;
                        }
                }
            }
        }

        public void Close()
        {
            client.Close();
        }
    }
}
