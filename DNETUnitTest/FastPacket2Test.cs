using DNET;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DNETUnitTest
{

    [TestClass]
    public unsafe class FastPacket2Test
    {
        [TestMethod]
        public void TestMethod_Temp1()
        {
            DQueue<IntPtr> queue = new DQueue<IntPtr>(1024);
            int msgCount = 10000;
            Task.Run(() =>
            {
                for (int count = 0; count < msgCount; count++)
                {
                    IntPtr msg = Marshal.AllocHGlobal(256);
                    queue.Enqueue(msg);
                }
            });
            for (int count = 0; count < msgCount; count++)
            {
                IntPtr msg = Marshal.AllocHGlobal(256);
                queue.Enqueue(msg);
            }
            Assert.IsTrue(queue.Count == msgCount * 2);
        }


        [TestMethod]
        public void TestMethod_AddSend()
        {
            LogFile.GetInst().CreatLogFile();
            DxDebug.IsLogFile = true;
            DxDebug.isLog = false;

            Random rand = new Random();
            FastPacket2 fp2 = new FastPacket2();

            int sendLength = 0;
            byte[] sendBuff = new byte[512];

            byte[] data = new byte[256];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)i;
            }

            int msgCount = 10000;

            Task.Run(() =>
            {
                //增加1000条待发送消息
                for (int count = 0; count < msgCount; count++)
                {
                    //DxDebug.LogConsole("1 添加一条消息！");
                    //一边添加
                    fp2.AddSend(data, 0, data.Length);
                }

            });
            Thread.Sleep(10);

            //增加1000条待发送消息
            for (int count = 0; count < msgCount; count++)
            {
                //DxDebug.LogConsole("2 添加一条消息！");
                //一边添加
                fp2.AddSend(data, 0, data.Length);
                //一边发送
                while (true)
                {
                    if (fp2.SendMsgCount == 0)
                    {
                        break;
                    }
                    //DxDebug.LogConsole("3 写入一次消息！");
                    sendLength += fp2.WriteSendDataToBuffer(sendBuff, 0, rand.Next(sendBuff.Length));
                }
            }

            while (true)
            {
                Thread.Sleep(10);
                if (fp2.SendMsgCount == 0)
                {
                    break;
                }
                //DxDebug.LogConsole("4 写入一次消息！");
                sendLength += fp2.WriteSendDataToBuffer(sendBuff, 0, rand.Next(sendBuff.Length));
            }

            Assert.IsTrue(sendLength == (data.Length + sizeof(int)) * msgCount * 2);
            LogFile.GetInst().Close();
        }
    }
}