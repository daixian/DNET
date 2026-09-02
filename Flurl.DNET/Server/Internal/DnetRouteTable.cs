using System;
using System.Collections.Generic;

namespace Flurl.DNET
{
    /// <summary>
    /// 保存服务器启动阶段构建并冻结的事件类型路由表。
    /// 底层字典冻结后只执行并发读取，因此无需在每次请求查找时加锁。
    /// </summary>
    internal sealed class DnetRouteTable
    {
        /// <summary>
        /// 以业务事件类型为键的Action描述符字典。
        /// </summary>
        private readonly Dictionary<int, DnetActionDescriptor> _actions;

        /// <summary>
        /// 使用已经完整校验的路由集合初始化只读路由表。
        /// </summary>
        /// <param name="actions">不会再被修改的路由字典。</param>
        internal DnetRouteTable(Dictionary<int, DnetActionDescriptor> actions)
        {
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        /// <summary>
        /// 尝试查找指定事件类型对应的Action。
        /// </summary>
        /// <param name="eventType">请求事件类型。</param>
        /// <param name="action">找到的Action描述符。</param>
        /// <returns>找到路由时返回true。</returns>
        internal bool TryGetAction(int eventType, out DnetActionDescriptor action)
        {
            return _actions.TryGetValue(eventType, out action);
        }
    }
}
