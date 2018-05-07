using DNET;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;

namespace DNETUnitTest
{
    /// <summary>
    /// 包含有创建服务器，创建客户端，然后互发的测试。
    /// </summary>
    [TestClass]
    public partial class UnitTest1
    {
        /// <summary>
        /// 启动一个服务器，它会原样回发接收到的消息。
        /// 再启动一个客户端和它发送消息，验证发送接收正常
        /// </summary>
        //[TestMethod]
        public void TestMethod_SendReceDPacketNoCrc()
        {
            Config.DefaultConfigOnWindows();
            Config.IsAutoHeartbeat = false;
            DNClient.GetInst().isDebugLog = true;
            LogFile.GetInst().isImmediatelyFlush = true;

            DNServer.GetInst().EventTokenReceData += (token) =>
            {
                byte[][] datas = token.GetReceiveData();
                if (datas == null)
                {
                    return;
                }

                for (int i = 0; i < datas.Length; i++)
                {
                    //收到的每一条消息.
                    byte[] data = datas[i];

                    DxDebug.LogConsole("服务端接收到:msgNum=" + BitConverter.ToInt32(data, 0));
                    //直接原样回发
                    DNServer.GetInstance().Send(token, data);

                    //得到消息类型然后处理
                    //int pType = BitConverter.ToInt32(data, 0);
                    //TypeRegister.GetInstance().Dispatch(token, pType, data, sizeof(int), data.Length - sizeof(int));
                }
            };

            DNServer.GetInst().Start(21024);//启动服务器
            while (true)
            {
                if (DNServer.GetInst().IsStarted)
                {
                    DxDebug.LogConsole("TestMethod_Send():服务器启动成功");
                    break;
                }
            }
            Random rand = new Random();
            int sendDataLength = rand.Next(128, 256);
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++)
            {
                sendData[i] = (byte)rand.Next(128, 256);
            }
            DNClient.GetInst().Connect("127.0.0.1", 21024);

            while (true)
            {
                if (DNClient.GetInst().IsConnected)
                {
                    DxDebug.LogConsole("TestMethod_Send():连接成功");
                    break;
                }
            }

            int receCount = 0;//接收的消息总条数
            int sendCount = 0;

            //发送n次
            for (int count = 0; count < 200; count++)
            {
                while (DNClient.GetInst().isSendQueueIsFull)
                {
                    Thread.Sleep(20);
                }
                //一次连发n条
                for (int i = 0; i < 500; i++)
                {
                    //发送sendDataLength字节的sendData
                    DxDebug.LogConsole("客户端发送:msgNum=" + sendCount);
                    Buffer.BlockCopy(BitConverter.GetBytes(sendCount), 0, sendData, 0, 4);
                    DNClient.GetInst().Send(sendData);
                    sendCount++;
                }
                while (receCount != sendCount)
                {
                    Thread.Sleep(20);
                    byte[][] datas = DNClient.GetInstance().GetReceiveData();
                    if (datas != null)
                    {
                        for (int i = 0; i < datas.Length; i++)
                        {
                            byte[] msg = datas[i];
                            //判断接收长度是否一致
                            Assert.IsTrue(msg.Length == sendDataLength);
                            //判断消息序号
                            int msgNum = BitConverter.ToInt32(msg, 0);
                            DxDebug.LogConsole("客户端接收到回发:msgNum=" + msgNum);
                            Assert.IsTrue(msgNum == receCount);

                            for (int j = 4; j < msg.Length; j++)
                            {
                                //判断每个字节是否一致
                                Assert.IsTrue(msg[j] == sendData[j]);
                            }

                            receCount++;
                        }
                    }
                }
            }

            Assert.IsTrue(receCount == sendCount);

            DNClient.GetInst().CloseImmediate();
            DNServer.GetInst().Close();
            LogFile.GetInst().Close();
        }

        /// <summary>
        /// 启动一个服务器，它会原样回发接收到的消息。
        /// 再启动一个客户端和它发送消息，验证发送接收正常
        /// </summary>
        [TestMethod]
        public void TestMethod_SendReceFastPacket()
        {
            Config.DefaultConfigOnWindows();
            Config.IsAutoHeartbeat = false;
            DNClient.GetInst().isDebugLog = true;
            LogFile.GetInst().isImmediatelyFlush = true; 
            DNServer.GetInst().EventTokenReceData += (token) =>
            {
                byte[][] datas = token.GetReceiveData();
                if (datas == null)
                {
                    return;
                }

                for (int i = 0; i < datas.Length; i++)
                {
                    //收到的每一条消息.
                    byte[] data = datas[i];

                    DxDebug.LogConsole("服务端接收到:msgNum=" + BitConverter.ToInt32(data, 0));
                    //直接原样回发
                    DNServer.GetInstance().Send(token, data);

                    //得到消息类型然后处理
                    //int pType = BitConverter.ToInt32(data, 0);
                    //TypeRegister.GetInstance().Dispatch(token, pType, data, sizeof(int), data.Length - sizeof(int));
                }
            };

            DNServer.GetInstance().Start(21024);//启动服务器
            while (true)
            {
                if (DNServer.GetInstance().IsStarted)
                {
                    DxDebug.LogConsole("TestMethod_Send():服务器启动成功");
                    break;
                }
            }
            Random rand = new Random();
            int sendDataLength = rand.Next(128, 256);
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++)
            {
                sendData[i] = (byte)rand.Next(128, 256);
            }
            DNClient.GetInstance().Connect("127.0.0.1", 21024);

            while (true)
            {
                if (DNClient.GetInstance().IsConnected)
                {
                    DxDebug.LogConsole("TestMethod_Send():连接成功");
                    Thread.Sleep(1000);
                    break;
                }
            }

            int receCount = 0;//接收的消息总条数
            int sendCount = 0;

            //发送n次
            for (int count = 0; count < 200; count++)
            {
                while (DNClient.GetInstance().isSendQueueIsFull)
                {
                    Thread.Sleep(20);
                }
                //一次连发n条
                for (int i = 0; i < 500; i++)
                {
                    //发送sendDataLength字节的sendData
                    DxDebug.LogConsole("客户端发送:msgNum=" + sendCount);
                    Buffer.BlockCopy(BitConverter.GetBytes(sendCount), 0, sendData, 0, 4);
                    DNClient.GetInstance().Send(sendData);
                    sendCount++;
                }
                while (receCount != sendCount)
                {
                    Thread.Sleep(20);
                    byte[][] datas = DNClient.GetInstance().GetReceiveData();
                    if (datas != null)
                    {
                        for (int i = 0; i < datas.Length; i++)
                        {
                            byte[] msg = datas[i];
                            //判断接收长度是否一致
                            Assert.IsTrue(msg.Length == sendDataLength);
                            //判断消息序号
                            int msgNum = BitConverter.ToInt32(msg, 0);
                            DxDebug.LogConsole("客户端接收到回发:msgNum=" + msgNum);
                            Assert.IsTrue(msgNum == receCount);

                            for (int j = 4; j < msg.Length; j++)
                            {
                                //判断每个字节是否一致
                                Assert.IsTrue(msg[j] == sendData[j]);
                            }

                            receCount++;
                        }
                    }
                }
            }

            Assert.IsTrue(receCount == sendCount);

            DNClient.GetInst().CloseImmediate();
            DNServer.GetInst().Close();
            LogFile.GetInst().Close();
        }

        /// <summary>
        /// 较大压力的测法。
        /// </summary>
        [TestMethod]
        public void TestMethod_SendRecePressure()
        {
            Config.DefaultConfigOnWindows();
            Config.IsAutoHeartbeat = false;
            DNClient.GetInstance().isDebugLog = false;
            LogFile.GetInst().isImmediatelyFlush = false;
            DxDebug.isLog = false;

            DNServer.GetInstance().EventTokenReceData += (token) =>
            {
                byte[][] datas = token.GetReceiveData();
                if (datas == null)
                {
                    return;
                }

                for (int i = 0; i < datas.Length; i++)
                {
                    //收到的每一条消息.
                    byte[] data = datas[i];

                    DxDebug.LogConsole("服务端接收到:msgNum=" + BitConverter.ToInt32(data, 0));
                    //直接原样回发
                    DNServer.GetInstance().Send(token, data);

                    //得到消息类型然后处理
                    //int pType = BitConverter.ToInt32(data, 0);
                    //TypeRegister.GetInstance().Dispatch(token, pType, data, sizeof(int), data.Length - sizeof(int));
                }
            };

            DNServer.GetInstance().Start(21024);//启动服务器
            while (true)
            {
                if (DNServer.GetInstance().IsStarted)
                {
                    DxDebug.LogConsole("TestMethod_Send():服务器启动成功");
                    break;
                }
            }
            Random rand = new Random();
            int sendDataLength = rand.Next(128, 256);
            byte[] sendData = new byte[sendDataLength];
            for (int i = 0; i < sendData.Length; i++)
            {
                sendData[i] = (byte)rand.Next(128, 256);
            }
            DNClient.GetInstance().Connect("127.0.0.1", 21024);

            while (true)
            {
                if (DNClient.GetInstance().IsConnected)
                {
                    DxDebug.LogConsole("TestMethod_Send():连接成功");
                    break;
                }
            }

            int receCount = 0;//接收的消息总条数
            int sendCount = 0;

            //发送n次
            for (int count = 0; count < 500; count++)
            {
                //如果已经队列太满，那就等一下再发
                while (DNClient.GetInstance().isSendQueueIsFull)
                {
                    Thread.Sleep(20);
                }
                //一次连发n条
                for (int i = 0; i < 1500; i++)
                {
                    //发送sendDataLength字节的sendData
                    DxDebug.LogConsole("客户端发送:msgNum=" + sendCount);
                    Buffer.BlockCopy(BitConverter.GetBytes(sendCount), 0, sendData, 0, 4);
                    DNClient.GetInstance().Send(sendData);
                    sendCount++;
                }
                Thread.Sleep(20);
                //边发边收
                byte[][] datas = DNClient.GetInstance().GetReceiveData();
                if (datas != null)
                {
                    for (int i = 0; i < datas.Length; i++)
                    {
                        byte[] msg = datas[i];
                        //判断接收长度是否一致
                        Assert.IsTrue(msg.Length == sendDataLength);
                        //判断消息序号
                        int msgNum = BitConverter.ToInt32(msg, 0);
                        DxDebug.LogConsole("客户端接收到回发:msgNum=" + msgNum);
                        Assert.IsTrue(msgNum == receCount);

                        for (int j = 4; j < msg.Length; j++)
                        {
                            //判断每个字节是否一致
                            Assert.IsTrue(msg[j] == sendData[j]);
                        }

                        receCount++;
                    }
                }
            }

            int tryCount = 0;
            while (receCount != sendCount)
            {
                Thread.Sleep(20);
                byte[][] datas = DNClient.GetInstance().GetReceiveData();
                if (datas != null)
                {
                    for (int i = 0; i < datas.Length; i++)
                    {
                        byte[] msg = datas[i];
                        //判断接收长度是否一致
                        Assert.IsTrue(msg.Length == sendDataLength);
                        //判断消息序号
                        int msgNum = BitConverter.ToInt32(msg, 0);
                        DxDebug.LogConsole("客户端接收到回发:msgNum=" + msgNum);
                        Assert.IsTrue(msgNum == receCount);

                        for (int j = 4; j < msg.Length; j++)
                        {
                            //判断每个字节是否一致
                            Assert.IsTrue(msg[j] == sendData[j]);
                        }

                        receCount++;
                    }
                }
                else
                {
                    if (tryCount >= 20)
                    {
                        DxDebug.LogConsole("重试超过次数！失败退出！");
                        break;
                    }
                    tryCount++;
                    Thread.Sleep(100);
                }
            }

            Assert.IsTrue(receCount == sendCount);

          
            DNClient.GetInst().CloseImmediate();
            DNServer.GetInst().Close();
            LogFile.GetInst().Close();
        }
    }
}