using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DNET.Test
{

    [TestFixture]
    public class ClientSimpleTest
    {
        [Test]
        public void Test_NULL()
        {
            DNClient.Inst.Close();
            for (int i = 0; i < 1024; i++) {
                DNClient.Inst.GetReceiveData();
                DNClient.Inst.AddSendData(null, 1, 2);
                DNClient.Inst.Send(null);
                _ = DNClient.Inst.IsConnected;
                _ = DNClient.Inst.IsConnecting;
                _ = DNClient.Inst.WaitReceMsgCount;
                _ = DNClient.Inst.WaitSendMsgCount;
            }

        }

        [Test]
        public void Test_Connect()
        {
            DNClient.Inst.Close();
            for (int i = 0; i < 5; i++) {
                if (!DNClient.Inst.IsConnected && !DNClient.Inst.IsConnecting)
                    DNClient.Inst.Connect("127.0.0.1", 40000);
                Thread.Sleep(500);
            }

        }

    }

}
