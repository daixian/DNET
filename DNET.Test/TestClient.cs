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
        private readonly DNClient _client;

        public TestClient(DNClient client)
        {
            _client = client;
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
        /// 实际客户端
        /// </summary>
        public DNClient Client => _client;

        /// <summary>
        /// 连接到指定的服务器
        /// </summary>
        /// <param name="ip">服务器IP地址</param>
        /// <param name="port">服务器端口</param>
        public void Connect(string ip, int port)
        {
            _client.Close();
            _client.Connect(ip, port);
            // 一直等待连接成功
            int retry = 0;
            while (!_client.IsConnected && retry++ < 1000) {
                Thread.Sleep(40);
                if (retry % 100 == 0) {
                    LogProxy.Info($"{_client.Name} 尝试重连...");
                    _client.Disconnect();
                    _client.Connect(ip, port); //重连一次
                }
            }
            Assert.IsTrue(_client.IsConnected, $"{_client.Name} 连接失败");
        }

        /// <summary>
        /// 发送数据并验证结果
        /// </summary>
        /// <param name="sendData">要发送的数据</param>
        /// <param name="batchCount">每个批次发送的消息数量</param>
        /// <param name="repeatCount">重复发送的次数</param>
        /// <param name="immediately">是否立即发送</param>
        public bool SendAndCheckEcho(byte[] sendData, int batchCount, int repeatCount, bool immediately, float timeoutSeconds = 5f)
        {
            LogProxy.Info($"{_client.Name} 发送数据并验证结果,数据长度:{sendData.Length}, 一批发送消息数:{batchCount}, 重复次数={repeatCount}, 立刻发送:{immediately}");
            // _client.Send($"客户端{_client.Name}准备开始发送数据...", Format.Text, SendCount, 0, true);

            for (int c = 0; c < repeatCount; c++) {
                for (int i = 0; i < batchCount; i++) {
                    // while (_client.IsSendQueueOverflow())
                    // Thread.Sleep(1);

                    _client.Send(sendData, 0, sendData.Length, Format.Raw, SendCount, 0, immediately);
                    SendCount++;
                }
                _client.TryStartSendOnWorkThread(); // 这里再驱动一下发送

                // 5秒超时
                DateTime startTime = DateTime.UtcNow;
                TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
                int errorCount = 0;
                while (ReceiveCount != SendCount) {
                    if (DateTime.UtcNow - startTime > timeout) {
                        LogProxy.Error($"{_client.Name} 超时未收到全部消息：已收到 {ReceiveCount} 条,预期 {SendCount} 条,上次接收到现在:{_client.Status.TimeSinceLastReceived} ms");
                        LogProxy.Debug($"{_client.Name} 待发送队列{_client.WaitSendMsgCount}");
                        _client.Send($"客户端{_client.Name}发生错误", Format.Text, SendCount, 0, true);
                        Thread.Sleep(1000); //这里等待一下看看现在服务器能否收到数据
                        var texts = _client.GetReceiveData();
                        if (texts != null && texts.Count > 0)
                            foreach (Message msg in texts) {
                                LogProxy.Debug($"收到回复文本:{msg.Text}");
                            }
                        return false;
                        //Assert.Fail($"超时未收到全部消息：已收到 {ReceiveCount} 条，预期 {SendCount} 条");
                    }

                    var msgList = _client.GetReceiveData();
                    if (msgList != null && msgList.Count > 0) {
                        foreach (Message msg in msgList) {
                            if (msg.Format == Format.Text)
                                continue;
                            Assert.That(msg.data.Length == sendData.Length);

                            if (msg.TxrId != ReceiveCount) {
                                errorCount++;
                                LogProxy.Error($"{_client.Name}错误 {msg.TxrId}/{ReceiveCount}  发送/接收:{_client.Status.SendMessageCount}/{_client.Status.ReceiveMessageCount}");
                            }
                            // 验证事务id号是不是按照自己发送的顺序递增的
                            // Assert.That(msg.TxrId, Is.EqualTo(ReceiveCount),
                            //     $"[ASSERT FAILED] 客户端{_client.Name}检查TxrId错误: actual = {msg.TxrId}, expected = {ReceiveCount},总的SendCount={SendCount},msgList.Count={msgList.Count},待发送队列{_client.WaitSendMsgCount}");

                            for (int j = 0; j < msg.data.Length; j++)
                                Assert.That(msg.data.buffer[j] == sendData[j]);

                            ReceiveCount++;
                        }
                        msgList.RecycleAllItems();
                    }
                    else {
                        Thread.Sleep(1);
                    }
                }
                Assert.IsTrue(errorCount == 0, $"{_client.Name}接收错误: {errorCount}");
            }

            LogProxy.Info($"{_client.Name}测试结束,平均延迟{_client.RttStatis.Average:F3}ms,最大{_client.RttStatis.Max:F3}ms,最小{_client.RttStatis.Min:F3}ms");
            return true;
        }

        public void Close()
        {
            _client.Close();
        }
    }
}
