using System;
using System.Threading;
using NUnit.Framework;

namespace DNET.Test
{
    [SetUpFixture]
    public class GlobalTestSetup
    {
        [OneTimeSetUp]
        public void GlobalSetup()
        {
            // 在所有测试开始前执行一次
            //DNET.LogProxy.SetupLogToConsole();
            string TimeStamp() => $"[{DateTime.Now:HH:mm:ss.fff}] [Thread:{Thread.CurrentThread.ManagedThreadId}]";

            LogProxy.Info = s => TestContext.Progress.WriteLine($"{TimeStamp()} [INFO] {s}");
            LogProxy.Warning = s => TestContext.Progress.WriteLine($"{TimeStamp()} [WARN] {s}");
            LogProxy.Error = s => TestContext.Progress.WriteLine($"{TimeStamp()} [ERROR] {s}");
            LogProxy.Debug = s => TestContext.Progress.WriteLine($"{TimeStamp()} [DEBUG] {s}");
        }

        [OneTimeTearDown]
        public void GlobalTeardown()
        {
            DNClient.Inst.Close();
            DNServer.Inst.Close();
            // 在所有测试结束后执行一次
            TestContext.Progress.WriteLine("Global teardown running...");
        }
    }
}
