using System;
using System.Threading;
using NUnit.Framework;

namespace DNET.Test
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
            Config.IsDebugMode = true;
            LogProxy.Warning("123");
            LogProxy.Error("123");

            DNClient.Inst.Close();
            DNServer.Inst.Close();
        }
    }
}
