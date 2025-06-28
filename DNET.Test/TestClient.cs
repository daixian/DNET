using System;
using System.Threading;
using NUnit.Framework;

namespace DNET.Test
{
    /// <summary>
    /// 测试客户端封装类，用于测试DNClient的功能
    /// </summary>
    public class TestClient
    {
        private readonly DNClient client;

        public TestClient(DNClient client)
        {
            this.client = client;
        }

        /// <summary>
        /// 获取接收到的消息数量
        /// </summary>
        public int ReceiveCount { get; private set; }

        /// <summary>
        /// 获取已发送的消息数量
        /// </summary>
        public int SendCount { get; private set; }

        /// <summary>
        /// 连接到指定的服务器
        /// </summary>
        /// <param name="ip">服务器IP地址</param>
        /// <param name="port">服务器端口</param>
        public void Connect(string ip, int port)
        {
            client.Close();
            client.Connect(ip, port);
            // 等待连接成功
            int retry = 0;
            while (!client.IsConnected && retry++ < 200) Thread.Sleep(20);
            Assert.IsTrue(client.IsConnected, $"{client.Name} 连接失败");
        }

        /// <summary>
        /// 发送数据并验证结果
        /// </summary>
        /// <param name="sendData">要发送的数据</param>
        /// <param name="batchCount">每个批次发送的消息数量</param>
        /// <param name="repeatCount">重复发送的次数</param>
        /// <param name="immediately">是否立即发送</param>
        public void SendAndCheckEcho(byte[] sendData, int batchCount, int repeatCount, bool immediately)
        {
            LogProxy.Log($"{client.Name} 发送数据并验证结果,数据长度:{sendData.Length}, 一批发送消息数:{batchCount}, 重复次数={repeatCount}, 立刻发送:{immediately}");
            for (int c = 0; c < repeatCount; c++) {
                for (int i = 0; i < batchCount; i++) {
                    while (client.IsSendQueueOverflow())
                        Thread.Sleep(1);

                    client.Send(sendData, 0, sendData.Length, Format.Raw, SendCount, 0, immediately);
                    SendCount++;
                }

                // 5秒超时
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

                            // 验证事务id号是不是按照自己发送的顺序递增的
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
