using NUnit.Framework;
using DNET;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

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

            // 服务端接收到的次数
            int serverReceCount = 0;
            int serverEventPeerReceDataCount = 0;
            Peer lastPeer = null;
            DNServer.Inst.EventPeerReceData += (token) => {
                lastPeer = token;
                var msgs = token.GetReceiveData();
                if (msgs == null || msgs.Count == 0) {
                    return;
                }
                serverEventPeerReceDataCount++; // 进入回调的次数
                if (msgs.Count > 1)
                    LogProxy.LogDebug($"服务端接收到一批消息,个数{msgs.Count}");

                for (int i = 0; i < msgs.Count; i++) {
                    //收到的每一条消息.
                    var msg = msgs[i];

                    LogProxy.LogDebug($"服务端接收到:msg.TxrId={msg.TxrId}");

                    while (token.IsSendQueueOverflow()) {
                        Thread.Sleep(1);
                    }
                    //直接原样回发
                    DNServer.Inst.Send(token, msg.data, 0, msg.data.Length, txrId: msg.TxrId);

                    // 这个异常会被捕获啊
                    Assert.That(msg.TxrId, Is.EqualTo(serverReceCount)); // 这个事务id应该也是顺着来的
                    serverReceCount++;
                }
            };

            DNServer.Inst.Start(21024); //启动服务器
            while (true) {
                if (DNServer.Inst.IsStarted) {
                    LogProxy.LogDebug("TestMethod_Send():服务器启动成功");
                    break;
                }
            }
            Random rand = new Random();
            int sendDataLength = rand.Next(1, 256); //随机一个长度,不停的测
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++) {
                sendData[i] = 0xFF;
            }
            DNClient.Inst.Connect("127.0.0.1", 21024);

            while (true) {
                if (DNClient.Inst.IsConnected) {
                    LogProxy.LogDebug("TestMethod_Send():连接成功");
                    break;
                }
            }

            int receCount = 0; //接收的消息总条数
            int sendCount = 0;

            //发送n次
            for (int count = 0; count < 100; count++) {
                //一次连发n条
                for (int i = 0; i < 500; i++) {
                    while (DNClient.Inst.IsSendQueueOverflow()) {
                        Thread.Sleep(1);
                    }
                    LogProxy.LogDebug("客户端发送:msgNum=" + sendCount);
                    DNClient.Inst.Send(sendData, 0, sendData.Length, Format.Raw, sendCount, 0);
                    sendCount++;
                }
                while (receCount != sendCount) {
                    Thread.Sleep(1);
                    var datas = DNClient.Inst.GetReceiveData();
                    if (datas != null) {
                        if (datas.Count > 1)
                            LogProxy.LogDebug($"客户端收到一批消息,个数{datas.Count}");
                        for (int i = 0; i < datas.Count; i++) {
                            var msg = datas[i];
                            //判断接收长度是否一致
                            Assert.That(msg.data.Length == sendDataLength);
                            //判断消息序号
                            //int msgNum = BitConverter.ToInt32(msg.data, 0);

                            LogProxy.LogDebug($"客户端接收到回发:TxrId={msg.TxrId}");
                            Assert.That(msg.TxrId, Is.EqualTo(receCount));

                            for (int j = 4; j < msg.data.Length; j++) {
                                //判断每个字节是否一致
                                Assert.That(msg.data[j] == sendData[j]);
                            }

                            receCount++;
                        }
                    }
                }
            }

            Assert.That(receCount == sendCount);

            DNClient.Inst.Close();
            DNServer.Inst.Close();
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

            // 服务端接收到的次数
            int serverReceCount = 0;
            int serverEventPeerReceDataCount = 0;
            Peer lastPeer = null;
            DNServer.Inst.EventPeerReceData += (token) => {
                lastPeer = token;
                var msgs = token.GetReceiveData();
                if (msgs == null || msgs.Count == 0) {
                    return;
                }
                serverEventPeerReceDataCount++; // 进入回调的次数
                if (msgs.Count > 1)
                    LogProxy.LogDebug($"服务端接收到一批消息,个数{msgs.Count}");
                for (int i = 0; i < msgs.Count; i++) {
                    //收到的每一条消息.
                    var msg = msgs[i];

                    LogProxy.LogDebug($"服务端接收到:msg.TxrId={msg.TxrId}");

                    while (token.IsSendQueueOverflow()) {
                        Thread.Sleep(2);
                    }
                    //直接原样回发
                    DNServer.Inst.Send(token, msg.data, 0, msg.data.Length, txrId: msg.TxrId);

                    // 这个异常会被捕获啊
                    Assert.That(msg.TxrId, Is.EqualTo(serverReceCount)); // 这个事务id应该也是顺着来的
                    serverReceCount++;
                }
            };

            DNServer.Inst.Start(21024); //启动服务器
            while (true) {
                if (DNServer.Inst.IsStarted) {
                    LogProxy.LogDebug("TestMethod_Send():服务器启动成功");
                    break;
                }
            }
            Random rand = new Random();
            int sendDataLength = 32;
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++) {
                sendData[i] = 0xFF;
            }
            DNClient.Inst.Connect("127.0.0.1", 21024);

            while (true) {
                if (DNClient.Inst.IsConnected) {
                    LogProxy.LogDebug("TestMethod_Send():连接成功");
                    break;
                }
            }

            int receCount = 0; //接收的消息总条数
            int sendCount = 0;

            //发送n次
            for (int count = 0; count < 100; count++) {
                //一次连发n条
                for (int i = 0; i < 500; i++) {
                    while (DNClient.Inst.IsSendQueueOverflow()) {
                        Thread.Sleep(2);
                    }
                    LogProxy.LogDebug("客户端发送:msgNum=" + sendCount);
                    DNClient.Inst.Send(sendData, 0, sendData.Length,
                        txrId: sendCount,
                        eventType: 0,
                        immediately: false);
                    sendCount++;
                }
                while (receCount != sendCount) {
                    Thread.Sleep(1);
                    var datas = DNClient.Inst.GetReceiveData();
                    if (datas != null) {
                        if (datas.Count > 1)
                            LogProxy.LogDebug($"客户端收到一批消息,个数{datas.Count}");
                        for (int i = 0; i < datas.Count; i++) {
                            var msg = datas[i];
                            //判断接收长度是否一致
                            Assert.That(msg.data.Length == sendDataLength);
                            //判断消息序号
                            //int msgNum = BitConverter.ToInt32(msg.data, 0);

                            LogProxy.LogDebug($"客户端接收到回发:TxrId={msg.TxrId}");
                            Assert.That(msg.TxrId, Is.EqualTo(receCount));

                            for (int j = 4; j < msg.data.Length; j++) {
                                //判断每个字节是否一致
                                Assert.That(msg.data[j] == sendData[j]);
                            }

                            receCount++;
                        }
                    }
                }
            }

            Assert.That(receCount == sendCount);

            DNClient.Inst.Close();
            DNServer.Inst.Close();
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

            // 启动服务端 Echo 逻辑
            DNServer.Inst.EventPeerReceData += (peer) => {
                var msgs = peer.GetReceiveData();
                if (msgs == null || msgs.Count == 0) {
                    return;
                }
                for (int i = 0; i < msgs.Count; i++) {
                    //收到的每一条消息.
                    var msg = msgs[i];
                    // LogProxy.LogDebug($"服务端接收到:msg.TxrId={msg.TxrId}");
                    //直接原样回发
                    peer.Send(msg.data, 0, msg.data.Length, txrId: msg.TxrId);
                }
            };

            DNServer.Inst.Start(21024);
            while (true) {
                if (DNServer.Inst.IsStarted) {
                    LogProxy.LogDebug("TestMethod_ServerEcho():服务器启动成功");
                    break;
                }
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
                var thread = new Thread(() => {
                    try {
                        var client = new DNClient();
                        client.Name = Thread.CurrentThread.Name;
                        client.Connect("127.0.0.1", 21024);
                        // 等待连接成功
                        int retry = 0;
                        while (!client.IsConnected && retry++ < 200) {
                            Thread.Sleep(20);
                        }
                        Assert.IsTrue(client.IsConnected, $"线程{threadIndex}连接失败");

                        int sendCount = 0; // 发送计数
                        int receCount = 0; // 接收计数

                        // 连发 messagesPerThread 条消息
                        for (int i = 0; i < messagesPerThread; i++) {
                            while (client.IsSendQueueOverflow())
                                Thread.Sleep(1);
                            // LogProxy.LogDebug("客户端发送:msgNum=" + sendCount);
                            client.Send(sendData, 0, sendData.Length, Format.Raw, txrId: sendCount, eventType: 0);
                            sendCount++;
                        }

                        // 等待并检查回显
                        while (receCount != messagesPerThread) {
                            Thread.Sleep(1);
                            var datas = client.GetReceiveData();
                            if (datas != null) {
                                foreach (var msg in datas) {
                                    Assert.That(msg.data.Length == sendDataLength);
                                    Assert.That(msg.TxrId, Is.EqualTo(receCount));
                                    for (int j = 0; j < sendDataLength; j++)
                                        Assert.That(msg.data[j] == sendData[j]);

                                    receCount++;
                                }
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
            foreach (var t in tasks) t.Start();
            foreach (var t in tasks) t.Join();

            DNServer.Inst.Close();
            // 最后验证没有异常
            Assert.That(exceptions, Is.Empty, $"有异常发生: {string.Join("\n", exceptions)}");
        }
    }
}
