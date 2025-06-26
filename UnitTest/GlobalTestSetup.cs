using NUnit.Framework;

[SetUpFixture]
public class GlobalTestSetup
{
    [OneTimeSetUp]
    public void GlobalSetup()
    {
        // 在所有测试开始前执行一次
        //DNET.LogProxy.SetupLogToConsole();

        DNET.LogProxy.actionLog = (s) => TestContext.Progress.WriteLine("[LOG] " + s);
        DNET.LogProxy.actionLogWarning = (s) => TestContext.Progress.WriteLine("[WARN] " + s);
        DNET.LogProxy.actionLogError = (s) => TestContext.Progress.WriteLine("[ERROR] " + s);
        DNET.LogProxy.actionLogDebug = (s) => TestContext.Progress.WriteLine("[DEBUG] " + s);
    }

    [OneTimeTearDown]
    public void GlobalTeardown()
    {
        // 在所有测试结束后执行一次
        TestContext.Progress.WriteLine("Global teardown running...");
    }
}
