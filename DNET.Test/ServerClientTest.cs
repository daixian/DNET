using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace DNET.Test
{
    [TestFixture]
    public class ServerClientTest
    {
        /// <summary>
        /// 启动一个服务器，它会原样回发接收到的消息。
        /// 再启动一个客户端和它发送消息，验证发送接收正常
        /// </summary>
        [Test]
        public void TestMethod_ServerClient()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugLog = false;
            DNClient.Inst.Close();
            DNServer.Inst.Close();

            EchoServer server = new EchoServer(DNServer.Inst);
            server.Start(21024);
            Assert.That(DNServer.Inst.IsStarted);

            TestClient client = new TestClient(DNClient.Inst);
            client.Connect("127.0.0.1", 21024);

            Random rand = new Random();
            int sendDataLength = rand.Next(1, 256);
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++)
                sendData[i] = 0xFF;

            client.SendAndCheckEcho(sendData, 500, 100, true);

            Assert.That(client.ReceiveCount == client.SendCount);

            client.Close();
            server.Stop();
        }

        /// <summary>
        /// 合并简短消息一起发送
        /// </summary>
        [Test]
        public void TestMethod_ServerClientMerge()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugLog = false;
            DNClient.Inst.Close();
            DNServer.Inst.Close();

            EchoServer server = new EchoServer(DNServer.Inst);
            server.Start(21024);
            Assert.That(DNServer.Inst.IsStarted);

            TestClient client = new TestClient(DNClient.Inst);
            client.Connect("127.0.0.1", 21024);

            Random rand = new Random();
            int sendDataLength = rand.Next(1, 256);
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++)
                sendData[i] = 0xFF;

            client.SendAndCheckEcho(sendData, 500, 100, false);

            Assert.That(client.ReceiveCount == client.SendCount);

            client.Close();
            server.Stop();
        }

        /// <summary>
        /// 多个客户端一起发送
        /// </summary>
        [Test]
        public void TestMethod_ServerEcho()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugLog = false;
            DNClient.Inst.Close();
            DNServer.Inst.Close();

            int port = 21025;
            // 启动服务端 Echo 逻辑
            DNServer.Inst.EventPeerReceData += peer => {
                var msgs = peer.GetReceiveData();
                if (msgs == null || msgs.Count == 0) return;
                for (int i = 0; i < msgs.Count; i++) {
                    //收到的每一条消息.
                    Message msg = msgs[i];
                    // LogProxy.LogDebug($"服务端接收到:msg.TxrId={msg.TxrId}");
                    //直接原样回发
                    peer.Send(msg.data, 0, msg.data.Length, txrId: msg.TxrId);
                }
            };

            DNServer.Inst.Start(port);
            while (true)
                if (DNServer.Inst.IsStarted) {
                    LogProxy.LogDebug("TestMethod_ServerEcho():服务器启动成功");
                    break;
                }

            int threadCount = 16; //16线程
            int messagesPerThread = 5000; //发送5000条
            int sendDataLength = 222; //发送数据的长度
            byte[] sendData = new byte[sendDataLength]; //每条消息附带的实际数据
            for (int i = 0; i < sendDataLength; i++) sendData[i] = (byte)i;

            var exceptions = new ConcurrentQueue<Exception>();
            var tasks = new List<Thread>();

            for (int t = 0; t < threadCount; t++) {
                int threadIndex = t;
                Thread thread = new Thread(() => {
                    try {
                        DNClient client = new DNClient();
                        client.Name = Thread.CurrentThread.Name;
                        client.Connect("127.0.0.1", port);
                        // 等待连接成功
                        int retry = 0;
                        while (!client.IsConnected && retry++ < 200) Thread.Sleep(20);
                        Assert.IsTrue(client.IsConnected, $"线程{threadIndex}连接失败");

                        int sendCount = 0; // 发送计数
                        int receCount = 0; // 接收计数

                        // 连发 messagesPerThread 条消息
                        for (int i = 0; i < messagesPerThread; i++) {
                            while (client.IsSendQueueOverflow())
                                Thread.Sleep(1);
                            // LogProxy.LogDebug("客户端发送:msgNum=" + sendCount);
                            client.Send(sendData, 0, sendData.Length, Format.Raw, sendCount);
                            sendCount++;
                        }

                        // 等待并检查回显
                        DateTime startTime = DateTime.UtcNow;
                        TimeSpan timeout = TimeSpan.FromSeconds(5);

                        while (receCount != messagesPerThread) {
                            // 超时检查
                            if (DateTime.UtcNow - startTime > timeout) Assert.Fail($"超时未收到全部消息：已收到 {receCount} 条，预期 {messagesPerThread} 条");

                            Thread.Sleep(1);
                            var datas = client.GetReceiveData();
                            if (datas != null)
                                foreach (Message msg in datas) {
                                    // LogProxy.LogDebug($"客户端接收到回发:TxrId={msg.TxrId}");
                                    Assert.That(msg.data.Length == sendDataLength);
                                    Assert.That(msg.TxrId, Is.EqualTo(receCount));
                                    for (int j = 0; j < sendDataLength; j++)
                                        Assert.That(msg.data[j] == sendData[j]);

                                    receCount++;
                                }
                        }
                        LogProxy.LogDebug($"客户端{client.Name}发送接收{sendCount}/{receCount}条消息");
                        client.Close();
                    } catch (Exception ex) {
                        exceptions.Enqueue(ex);
                    }
                }) { IsBackground = true, Name = $"ClientThread-{t}" };

                tasks.Add(thread);
            }

            // 启动所有线程
            foreach (Thread t in tasks) t.Start();
            foreach (Thread t in tasks) t.Join();

            DNServer.Inst.Close();
            // 最后验证没有异常
            Assert.That(exceptions, Is.Empty, $"有异常发生: {string.Join("\n", exceptions)}");
        }
    }
}
