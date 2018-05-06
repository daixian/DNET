using DNET;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DNETUnitTest
{

    [TestClass]
    public  class FastPacket2Test
    {
        //[TestMethod]
        public void TestMethod_Temp1()
        {
            unsafe
            {
                int[] buffer = new int[1024 * 128];
                IntPtr addr = Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0);
                int* p = (int*)addr.ToPointer();
                for (int count = 0; count < 1024; count++)
                {
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        p[i] = i;
                    }
                }

            }
        }


        [TestMethod]
        public void TestMethod_AddSend()
        {
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

            //增加1000条待发送消息
            for (int count = 0; count < msgCount; count++)
            {
                //一边添加
                fp2.AddSend(data, 0, data.Length);
                //一边发送
                while (true)
                {
                    if (fp2.SendMsgCount == 0)
                    {
                        break;
                    }
                    sendLength += fp2.WriteSendDataToBuffer(sendBuff, 0, rand.Next(sendBuff.Length));
                }
            }
            
            Assert.IsTrue(sendLength == (data.Length + sizeof(int)) * msgCount);
        }
    }
}