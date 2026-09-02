using System;

namespace Flurl.DNET
{
    /// <summary>
    /// 标记一个由FlurlDnetServer在启动阶段发现并创建为单例的控制器类型。
    /// 控制器可能被不同Peer并发调用，因此不得在实例字段中保存非线程安全的请求状态。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class DnetControllerAttribute : Attribute
    {
    }
}
