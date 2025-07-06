using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        public void TestMethod_ServerClient50()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugMode = false;
            DNClient.Inst.Close();
            DNServer.Inst.Close();

            EchoServer server = new EchoServer(DNServer.Inst);
            server.Start(21024);
            Assert.That(DNServer.Inst.IsStarted);

            TestClient client = new TestClient(DNClient.Inst);
            client.Connect("127.0.0.1", 21024);

            Random rand = new Random();
            int sendDataLength = rand.Next(1, 256);
            // int sendDataLength = 75;
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++)
                sendData[i] = 0xFF;

            if (!client.SendAndCheckEcho(sendData, 50, 100, true)) {
                LogProxy.Error($"失败,当前ServerReceiveCount={server.ServerReceiveCount}");
            }
            else {
                LogProxy.Info($"成功,当前ServerReceiveCount={server.ServerReceiveCount}");
            }
            client.Close();
            server.Stop();

            Assert.That(client.ReceiveCount, Is.EqualTo(client.SendCount));
        }

        /// <summary>
        /// 启动一个服务器，它会原样回发接收到的消息。
        /// 再启动一个客户端和它发送消息，验证发送接收正常
        /// </summary>
        [Test]
        public void TestMethod_ServerClient()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugMode = false;
            DNClient.Inst.Close();
            DNServer.Inst.Close();

            EchoServer server = new EchoServer(DNServer.Inst);
            server.Start(21024);
            Assert.That(DNServer.Inst.IsStarted);

            TestClient client = new TestClient(DNClient.Inst);
            client.Connect("127.0.0.1", 21024);

            Random rand = new Random();
            // int sendDataLength = rand.Next(1, 256);
            int sendDataLength = 75;
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++)
                sendData[i] = 0xFF;

            if (!client.SendAndCheckEcho(sendData, 500, 100, true)) {
                LogProxy.Error($"失败,当前ServerReceiveCount={server.ServerReceiveCount}");

                // 失败之后重新连接个看看
                client.Close();
                client.Connect("127.0.0.1", 21024);
                client.Client.Send("这是失败之后重新创建一个客户端连接尝试发个消息");
                Thread.Sleep(1);
                var msgs = client.Client.GetReceiveData();
                LogProxy.Info($"Client.GetReceiveData()的Count={msgs?.Count}");
            }
            else {
                LogProxy.Info($"成功,当前ServerReceiveCount={server.ServerReceiveCount}");
            }
            client.Close();
            server.Stop();

            Assert.That(client.ReceiveCount, Is.EqualTo(client.SendCount));
        }

        /// <summary>
        /// 合并简短消息一起发送
        /// </summary>
        [Test]
        public void TestMethod_ServerClientMerge()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugMode = false;
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

            if (!client.SendAndCheckEcho(sendData, 500, 100, false)) {
                LogProxy.Error($"失败,当前ServerReceiveCount={server.ServerReceiveCount}");
            }
            else {
                LogProxy.Info($"成功,当前ServerReceiveCount={server.ServerReceiveCount}");
            }

            Assert.That(client.ReceiveCount, Is.EqualTo(client.SendCount));

            client.Close();
            server.Stop();
        }

        /// <summary>
        /// 64个客户端同时并发发送并阻塞等待回显结果
        /// </summary>
        [Test]
        public void TestMethod_ServerClient64C_Parallel()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugMode = false;
            DNClient.Inst.Close();
            DNServer.Inst.Close();

            EchoServer server = new EchoServer(DNServer.Inst);
            server.Start(21024, true);
            Assert.That(DNServer.Inst.IsStarted);

            int clientCount = 64;
            Random rand = new Random();
            int sendDataLength = rand.Next(1, 256);
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++)
                sendData[i] = 0xFF;

            TestClient[] clients = new TestClient[clientCount];
            bool[] results = new bool[clientCount];

            // 多线程并发执行 SendAndCheckEcho
            Parallel.For(0, clientCount, i => {
                // 创建并连接客户端
                clients[i] = new TestClient(new DNClient() { Name = $"Client{i}" });
                clients[i].Connect("127.0.0.1", 21024);

                results[i] = clients[i].SendAndCheckEcho(sendData, 500, 10, true, timeoutSeconds: 30);
                if (!results[i]) {
                    LogProxy.Error($"客户端 {i} 失败, ServerReceiveCount={server.ServerReceiveCount}");

                    clients[i].Close();
                    clients[i].Connect("127.0.0.1", 21024);
                    clients[i].Client.Send($"客户端{i}失败后重新发送一次");
                    Thread.Sleep(1);
                    var msgs = clients[i].Client.GetReceiveData();
                    LogProxy.Info($"客户端{i} GetReceiveData Count={msgs?.Count}");
                }
                else {
                    LogProxy.Info($"客户端 {i} 成功, ServerReceiveCount={server.ServerReceiveCount}");
                }
            });

            // 关闭所有客户端
            foreach (var client in clients)
                client.Close();

            server.Stop();

            // 断言所有客户端都收到了数据
            for (int i = 0; i < clientCount; i++) {
                Assert.That(clients[i].ReceiveCount, Is.EqualTo(clients[i].SendCount), $"客户端 {i} 的接收数量与发送不一致");
            }
        }

        /// <summary>
        /// 使用Task.Run的写法
        /// </summary>
        [Test]
        public void TestMethod_ServerClient8C_Parallel()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugMode = false;
            DNClient.Inst.Close();
            DNServer.Inst.Close();

            EchoServer server = new EchoServer(DNServer.Inst);
            server.Start(21024, isFastResponse: false);
            Assert.That(DNServer.Inst.IsStarted);

            int clientCount = 8;
            Random rand = new Random();
            int sendDataLength = rand.Next(1, 256);
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++)
                sendData[i] = 0xFF;

            TestClient[] clients = new TestClient[clientCount];
            bool[] results = new bool[clientCount];

            var tasks = new List<Task>();

            for (int i = 0; i < clientCount; i++) {
                int idx = i; // 捕获变量
                var task = Task.Run(() => {
                    var client = new TestClient(new DNClient() { Name = $"Client{idx}" });
                    clients[idx] = client;

                    client.Connect("127.0.0.1", 21024);

                    results[idx] = client.SendAndCheckEcho(sendData, 500, 200, true, timeoutSeconds: 60); // 有的电脑卡,超时搞长点
                    if (!results[idx]) {
                        LogProxy.Error($"客户端 {idx} 失败, ServerReceiveCount={server.ServerReceiveCount}");

                        client.Close();
                        client.Connect("127.0.0.1", 21024);
                        client.Client.Send($"客户端{idx}失败后重新发送一次");
                        Thread.Sleep(1);
                        var msgs = client.Client.GetReceiveData();
                        LogProxy.Info($"客户端{idx} GetReceiveData Count={msgs?.Count}");
                    }
                    else {
                        LogProxy.Info($"客户端 {idx} 成功, ServerReceiveCount={server.ServerReceiveCount}");
                    }
                });

                tasks.Add(task);
            }

            Task.WaitAll(tasks.ToArray());

            foreach (var client in clients)
                client.Close();

            server.Stop();

            for (int i = 0; i < clientCount; i++) {
                Assert.That(clients[i].ReceiveCount, Is.EqualTo(clients[i].SendCount), $"客户端 {i} 的接收数量与发送不一致");
            }
        }

        /// <summary>
        /// 测试服务器端使用isFastResponse=true来做
        /// </summary>
        [Test]
        public void TestMethod_ServerClient8C_Parallel_Fast()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugMode = false;
            DNClient.Inst.Close();
            DNServer.Inst.Close();

            EchoServer server = new EchoServer(DNServer.Inst);
            server.Start(21024, isFastResponse: true);
            Assert.That(DNServer.Inst.IsStarted);

            int clientCount = 8;
            Random rand = new Random();
            int sendDataLength = rand.Next(1, 256);
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++)
                sendData[i] = 0xFF;

            TestClient[] clients = new TestClient[clientCount];
            bool[] results = new bool[clientCount];

            var tasks = new List<Task>();

            for (int i = 0; i < clientCount; i++) {
                int idx = i; // 捕获变量
                var task = Task.Run(() => {
                    var client = new TestClient(new DNClient() { Name = $"Client{idx}" });
                    clients[idx] = client;

                    client.Connect("127.0.0.1", 21024);

                    results[idx] = client.SendAndCheckEcho(sendData, 500, 200, true, timeoutSeconds: 60); // 有的电脑卡,超时搞长点
                    if (!results[idx]) {
                        LogProxy.Error($"客户端 {idx} 失败, ServerReceiveCount={server.ServerReceiveCount}");

                        client.Close();
                        client.Connect("127.0.0.1", 21024);
                        client.Client.Send($"客户端{idx}失败后重新发送一次");
                        Thread.Sleep(1);
                        var msgs = client.Client.GetReceiveData();
                        LogProxy.Info($"客户端{idx} GetReceiveData Count={msgs?.Count}");
                    }
                    else {
                        LogProxy.Info($"客户端 {idx} 成功, ServerReceiveCount={server.ServerReceiveCount}");
                    }
                });

                tasks.Add(task);
            }

            Task.WaitAll(tasks.ToArray());

            foreach (var client in clients)
                client.Close();

            server.Stop();

            for (int i = 0; i < clientCount; i++) {
                Assert.That(clients[i].ReceiveCount, Is.EqualTo(clients[i].SendCount), $"客户端 {i} 的接收数量与发送不一致");
            }
        }
    }
}
