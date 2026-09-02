using System;

namespace Flurl.DNET
{
    /// <summary>
    /// 把控制器中的公开实例方法绑定到一个唯一DNET事件类型。
    /// 路由在服务器启动时完成校验和冻结，运行期间通过常数时间字典查找。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class DnetRouteAttribute : Attribute
    {
        /// <summary>
        /// 使用要匹配的事件类型创建路由特性。
        /// </summary>
        /// <param name="eventType">业务请求事件类型。</param>
        public DnetRouteAttribute(int eventType)
        {
            EventType = eventType;
        }

        /// <summary>
        /// 获取当前Action匹配的业务事件类型。
        /// </summary>
        public int EventType { get; }
    }
}
