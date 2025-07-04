﻿using System;
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
            while (!_client.IsConnected && retry++ < 1000)
                Thread.Sleep(20);
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
            LogProxy.Log($"{_client.Name} 发送数据并验证结果,数据长度:{sendData.Length}, 一批发送消息数:{batchCount}, 重复次数={repeatCount}, 立刻发送:{immediately}");
            _client.Send($"客户端{_client.Name}准备开始发送数据...", Format.Text, SendCount, 0, true);

            for (int c = 0; c < repeatCount; c++) {
                for (int i = 0; i < batchCount; i++) {
                    while (_client.IsSendQueueOverflow())
                        Thread.Sleep(1);

                    _client.Send(sendData, 0, sendData.Length, Format.Raw, SendCount, 0, immediately);
                    SendCount++;
                }

                // 5秒超时
                DateTime startTime = DateTime.UtcNow;
                TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);

                while (ReceiveCount != SendCount) {
                    if (DateTime.UtcNow - startTime > timeout) {
                        LogProxy.LogError($"{_client.Name} 超时未收到全部消息：已收到 {ReceiveCount} 条,预期 {SendCount} 条,上次接收到现在:{_client.Status.TimeSinceLastReceived} ms");
                        LogProxy.LogDebug($"{_client.Name} 待发送队列{_client.WaitSendMsgCount}");
                        _client.Send($"客户端{_client.Name}发生错误", Format.Text, SendCount, 0, true);
                        Thread.Sleep(1000); //这里等待一下看看现在服务器能否收到数据
                        var texts = _client.GetReceiveData();
                        if (texts != null && texts.Count > 0)
                            foreach (Message msg in texts) {
                                LogProxy.LogDebug($"收到回复文本:{msg.Text}");
                            }
                        return false;
                        //Assert.Fail($"超时未收到全部消息：已收到 {ReceiveCount} 条，预期 {SendCount} 条");
                    }

                    var datas = _client.GetReceiveData();
                    if (datas != null && datas.Count > 0) {
                        foreach (Message msg in datas) {
                            if (msg.Format == Format.Text)
                                continue;
                            Assert.That(msg.data.Length == sendData.Length);

                            // 验证事务id号是不是按照自己发送的顺序递增的
                            Assert.That(msg.TxrId, Is.EqualTo(ReceiveCount),
                                 $"[ASSERT FAILED] 客户端{_client.Name}检查TxrId错误: actual = {msg.TxrId}, expected = {ReceiveCount}");

                            for (int j = 0; j < msg.data.Length; j++)
                                Assert.That(msg.data.buffer[j] == sendData[j]);


                            ReceiveCount++;
                            msg.Recycle();
                        }
                        datas.Recycle();
                    }
                    else {
                        Thread.Sleep(1);
                    }
                }
            }
            return true;
        }

        public void Close()
        {
            _client.Close();
        }
    }
}
