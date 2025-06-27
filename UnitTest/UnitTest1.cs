using DNET;
using NUnit.Framework;
using System;
using System.Threading;

namespace UnitTest
{
    /// <summary>
    /// 包含有创建服务器，创建客户端，然后互发的测试。
    /// </summary>
    [TestFixture]
    public class UnitTest1
    {
        [Test]
        public void TestMethod_Log()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugLog = true;
            LogProxy.LogWarning("123");
            LogProxy.LogError("123");

            DNClient.Inst.Close();
            DNServer.Inst.Close();
        }


        /// <summary>
        /// 启动一个服务器，它会原样回发接收到的消息。
        /// 再启动一个客户端和它发送消息，验证发送接收正常
        /// </summary>
        [Test]
        public void TestMethod_SendReceDPacketNoCrc()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugLog = true;

            DNServer.Inst.EventPeerReceData += (token) => {
                var msgs = token.GetReceiveData();
                if (msgs == null) {
                    return;
                }

                for (int i = 0; i < msgs.Count; i++) {
                    //收到的每一条消息.
                    var msg = msgs[i];

                    LogProxy.LogDebug($"服务端接收到:txrId={msg.header.txrId}");
                    //直接原样回发
                    DNServer.Inst.Send(token, msg.data, 0, msg.data.Length);

                    //得到消息类型然后处理
                    //int pType = BitConverter.ToInt32(data, 0);
                    //TypeRegister.GetInstance().Dispatch(token, pType, data, sizeof(int), data.Length - sizeof(int));
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
            int sendDataLength = rand.Next(128, 256);
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++) {
                sendData[i] = (byte)rand.Next(128, 256);
            }
            DNClient.Inst.Connect("127.0.0.1", 21024);

            while (true) {
                Thread.Sleep(1);
                if (DNClient.Inst.IsConnected) {
                    LogProxy.LogDebug("TestMethod_Send():连接成功");
                    break;
                }
            }

            int receCount = 0; //接收的消息总条数
            int sendCount = 0;

            //发送n次
            //for (int count = 0; count < 200; count++) {
            while (DNClient.Inst.IsSendQueueOverflow()) {
                Thread.Sleep(1);
            }
            //一次连发n条
            for (int i = 0; i < 16; i++) {
                //发送sendDataLength字节的sendData
                LogProxy.LogDebug("客户端发送:msgNum=" + sendCount);
                Buffer.BlockCopy(BitConverter.GetBytes(sendCount), 0, sendData, 0, 4);
                DNClient.Inst.Send(sendData, 0, sendData.Length, Format.Raw, i, 0);
                sendCount++;
            }
            while (receCount != sendCount) {
                Thread.Sleep(1);
                var msgs = DNClient.Inst.GetReceiveData();
                if (msgs != null) {
                    for (int i = 0; i < msgs.Count; i++) {
                        var msg = msgs[i];
                        //判断接收长度是否一致
                        Assert.That(msg.data.Length == sendDataLength);
                        //判断消息序号
                        int msgNum = BitConverter.ToInt32(msg.data, 0);
                        LogProxy.LogDebug("客户端接收到回发:msgNum=" + msgNum);
                        Assert.That(msgNum == receCount);

                        for (int j = 4; j < msg.data.Length; j++) {
                            //判断每个字节是否一致
                            Assert.That(msg.data[j] == sendData[j]);
                        }

                        receCount++;
                    }
                }
            }
            //}

            Assert.That(receCount == sendCount);

            DNClient.Inst.Close();
            DNServer.Inst.Close();
        }


        ///// <summary>
        ///// 启动一个服务器，它会原样回发接收到的消息。
        ///// 再启动一个客户端和它发送消息，验证发送接收正常
        ///// </summary>
        //[TestMethod]
        //public void TestMethod_SendReceFastPacketBF()
        //{
        //    Config.IsAutoHeartbeat = false;
        //    DNClient.Inst.isDebugLog = true;
        //    DNServer.Inst.EventPeerReceData += (token) => {
        //        var datas = token.GetReceiveData();
        //        if (datas == null) {
        //            return;
        //        }

        //        for (int i = 0; i < datas.Length; i++) {
        //            //收到的每一条消息.
        //            var msg = datas[i];

        //            LogProxy.LogDebug("服务端接收到:msgNum=" + BitConverter.ToInt32(msg.data, 0));
        //            //直接原样回发
        //            DNServer.Inst.Send(token, msg.data);

        //            //得到消息类型然后处理
        //            //int pType = BitConverter.ToInt32(data, 0);
        //            //TypeRegister.GetInstance().Dispatch(token, pType, data, sizeof(int), data.Length - sizeof(int));
        //        }
        //    };

        //    DNServer.Inst.Start(21024); //启动服务器
        //    while (true) {
        //        if (DNServer.Inst.IsStarted) {
        //            LogProxy.LogDebug("TestMethod_Send():服务器启动成功");
        //            break;
        //        }
        //    }
        //    Random rand = new Random();
        //    int sendDataLength = rand.Next(128, 256);
        //    byte[] sendData = new byte[sendDataLength];
        //    for (int i = 0; i < sendData.Length; i++) {
        //        sendData[i] = (byte)rand.Next(128, 256);
        //    }
        //    DNClient.Inst.Connect("127.0.0.1", 21024);

        //    while (true) {
        //        if (DNClient.Inst.IsConnected) {
        //            LogProxy.LogDebug("TestMethod_Send():连接成功");
        //            Thread.Sleep(1000);
        //            break;
        //        }
        //    }

        //    int receCount = 0; //接收的消息总条数
        //    int sendCount = 0;
        //    ByteBuffer[] dataBuffers = new ByteBuffer[128];

        //    //发送n次
        //    for (int count = 0; count < 200; count++) {
        //        //while (DNClient.Inst.isSendQueueIsFull) {
        //        //    Thread.Sleep(20);
        //        //}
        //        //一次连发n条
        //        for (int i = 0; i < 500; i++) {
        //            //发送sendDataLength字节的sendData
        //            LogProxy.LogDebug("客户端发送:msgNum=" + sendCount);
        //            Buffer.BlockCopy(BitConverter.GetBytes(sendCount), 0, sendData, 0, 4);
        //            DNClient.Inst.Send(sendData);
        //            sendCount++;
        //        }
        //        while (receCount != sendCount) {
        //            Thread.Sleep(1);
        //            int msgCount = DNClient.Inst.GetReceiveData(dataBuffers, 0, dataBuffers.Length);
        //            if (msgCount > 0) {
        //                for (int i = 0; i < msgCount; i++) {
        //                    ByteBuffer msg = dataBuffers[i];
        //                    //判断接收长度是否一致
        //                    Assert.That(msg.Length == sendDataLength);
        //                    //判断消息序号
        //                    int msgNum = BitConverter.ToInt32(msg.buffer, 0);
        //                    LogProxy.LogDebug("客户端接收到回发:msgNum=" + msgNum);
        //                    Assert.That(msgNum == receCount);

        //                    for (int j = 4; j < msg.Length; j++) {
        //                        //判断每个字节是否一致
        //                        Assert.That(msg.buffer[j] == sendData[j]);
        //                    }

        //                    msg.Recycle();
        //                    receCount++;
        //                }
        //            }
        //        }
        //    }

        //    Assert.That(receCount == sendCount);

        //    DNClient.Inst.CloseImmediate();
        //    DNServer.Inst.Close();
        //}

        /// <summary>
        /// 较大压力的测法。
        /// </summary>
        [Test]
        public void TestMethod_SendRecePressure()
        {
            Config.IsAutoHeartbeat = false;
            Config.IsDebugLog = false;

            DNServer.Inst.EventPeerReceData += (token) => {
                var msgs = token.GetReceiveData();
                if (msgs == null) {
                    return;
                }

                for (int i = 0; i < msgs.Count; i++) {
                    //收到的每一条消息.
                    var msg = msgs[i];

                    LogProxy.LogDebug("服务端接收到:msgNum=" + BitConverter.ToInt32(msg.data, 0));
                    //直接原样回发
                    DNServer.Inst.Send(token, msg.data, 0, msg.data.Length);

                    //得到消息类型然后处理
                    //int pType = BitConverter.ToInt32(data, 0);
                    //TypeRegister.GetInstance().Dispatch(token, pType, data, sizeof(int), data.Length - sizeof(int));
                }
            };

            DNServer.Inst.Start(21024); //启动服务器
            while (true) {
                Thread.Sleep(20);
                if (DNServer.Inst.IsStarted) {
                    LogProxy.LogDebug("TestMethod_Send():服务器启动成功");
                    break;
                }
            }
            Random rand = new Random();
            int sendDataLength = rand.Next(128, 256);
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++) {
                sendData[i] = (byte)rand.Next(128, 256);
            }
            DNClient.Inst.Connect("127.0.0.1", 21024);

            while (true) {
                Thread.Sleep(20);
                if (DNClient.Inst.IsConnected) {
                    LogProxy.LogDebug("TestMethod_Send():连接成功");
                    break;
                }
            }

            int receCount = 0; //接收的消息总条数
            int sendCount = 0;

            //发送n次
            for (int count = 0; count < 500; count++) {
                //如果已经队列太满，那就等一下再发
                while (DNClient.Inst.IsSendQueueOverflow()) {
                    Thread.Sleep(1);
                }
                //一次连发n条
                for (int i = 0; i < 1500; i++) {
                    //发送sendDataLength字节的sendData
                    LogProxy.LogDebug("客户端发送:msgNum=" + sendCount);
                    Buffer.BlockCopy(BitConverter.GetBytes(sendCount), 0, sendData, 0, 4);
                    while (DNClient.Inst.IsSendQueueOverflow()) {
                        Thread.Sleep(1);
                    }
                    DNClient.Inst.Send(sendData);
                    sendCount++;
                }
                Thread.Sleep(1);
                //边发边收
                var msgs = DNClient.Inst.GetReceiveData();
                if (msgs != null) {
                    for (int i = 0; i < msgs.Count; i++) {
                        var msg = msgs[i];
                        //判断接收长度是否一致
                        Assert.That(msg.data.Length == sendDataLength);
                        //判断消息序号
                        int msgNum = BitConverter.ToInt32(msg.data, 0);
                        LogProxy.LogDebug("客户端接收到回发:msgNum=" + msgNum);
                        Assert.That(msgNum == receCount);

                        for (int j = 4; j < msg.data.Length; j++) {
                            //判断每个字节是否一致
                            Assert.That(msg.data[j] == sendData[j]);
                        }

                        receCount++;
                    }
                }
            }

            int tryCount = 0;
            while (receCount != sendCount) {
                Thread.Sleep(20);
                var datas = DNClient.Inst.GetReceiveData();
                if (datas != null) {
                    for (int i = 0; i < datas.Count; i++) {
                        var msg = datas[i];
                        //判断接收长度是否一致
                        Assert.That(msg.data.Length == sendDataLength);
                        //判断消息序号
                        int msgNum = BitConverter.ToInt32(msg.data, 0);
                        LogProxy.LogDebug("客户端接收到回发:msgNum=" + msgNum);
                        Assert.That(msgNum == receCount);

                        for (int j = 4; j < msg.data.Length; j++) {
                            //判断每个字节是否一致
                            Assert.That(msg.data[j] == sendData[j]);
                        }

                        receCount++;
                    }
                }
                else {
                    if (tryCount >= 20) {
                        LogProxy.LogDebug("重试超过次数！失败退出！");
                        break;
                    }
                    tryCount++;
                    Thread.Sleep(1);
                }
            }

            Assert.That(receCount == sendCount);

            DNClient.Inst.Close();
            DNServer.Inst.Close();
        }
    }
}
