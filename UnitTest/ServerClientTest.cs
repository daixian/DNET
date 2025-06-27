using NUnit.Framework;
using System;
using System.Collections.Generic;
using DNET.Protocol;
using DNET;

namespace UnitTest;

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
        Config.isDebugLog = false;

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

                while (token.SendQueueOverflow) {
                    Thread.Sleep(2);
                }
                //直接原样回发
                DNServer.Inst.Send(token, msg.data, txrId: msg.TxrId);

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
        int sendDataLength = 100;
        byte[] sendData = new byte[sendDataLength];
        for (int i = 0; i < sendData.Length; i++) {
            sendData[i] = 0xFF;
        }
        DNClient.Inst.Connect("127.0.0.1", 21024);

        while (true) {
            if (DNClient.Inst.IsConnected) {
                LogProxy.LogDebug("TestMethod_Send():连接成功");
                Thread.Sleep(1000);
                break;
            }
        }

        int receCount = 0; //接收的消息总条数
        int sendCount = 0;

        //发送n次
        for (int count = 0; count < 100; count++) {
            //一次连发n条
            for (int i = 0; i < 500; i++) {
                while (DNClient.Inst.SendQueueOverflow) {
                    Thread.Sleep(2);
                }
                LogProxy.LogDebug("客户端发送:msgNum=" + sendCount);
                DNClient.Inst.Send(sendData, 0, sendData.Length, DNET.Protocol.Format.Raw, sendCount, 0);
                sendCount++;
            }
            while (receCount != sendCount) {
                Thread.Sleep(1);
                var datas = DNClient.Inst.GetReceiveData();
                if (datas != null) {
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
        Config.isDebugLog = false;

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

                while (token.SendQueueOverflow) {
                    Thread.Sleep(2);
                }
                //直接原样回发
                DNServer.Inst.Send(token, msg.data, txrId: msg.TxrId);

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
                Thread.Sleep(1000);
                break;
            }
        }

        int receCount = 0; //接收的消息总条数
        int sendCount = 0;

        //发送n次
        for (int count = 0; count < 100; count++) {
            //一次连发n条
            for (int i = 0; i < 500; i++) {
                while (DNClient.Inst.SendQueueOverflow) {
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
}
