using DNET;
using NUnit.Framework;

[SetUpFixture]
public class GlobalTestSetup
{
    [OneTimeSetUp]
    public void GlobalSetup()
    {
        // 在所有测试开始前执行一次
        //DNET.LogProxy.SetupLogToConsole();

        LogProxy.actionLog = s => TestContext.Progress.WriteLine($"[INFO] {s}");
        LogProxy.actionLogWarning = s => TestContext.Progress.WriteLine($"[WARN] {s}");
        LogProxy.actionLogError = s => TestContext.Progress.WriteLine($"[ERROR] {s}");
        LogProxy.actionLogDebug = s => TestContext.Progress.WriteLine($"[DEBUG] {s}");
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
