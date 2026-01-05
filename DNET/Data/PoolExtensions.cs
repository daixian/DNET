using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DNET
{
    /// <summary>
    /// 做一些扩展方法
    /// </summary>
    public static class PoolExtensions
    {
        /// <summary>
        /// 回收到全局共享池List
        /// </summary>
        /// <typeparam name="T">列表元素类型</typeparam>
        /// <param name="list">要回收的列表</param>
        public static void Recycle<T>(this List<T> list)
        {
            if (list == null) return;
            ListPool<T>.Shared.Recycle(list);
        }

        /// <summary>
        /// 回收到全局共享池List
        /// </summary>
        /// <param name="list">要回收的消息列表</param>
        public static void RecycleAllItems(this List<Message> list)
        {
            if (list == null)
                return;
            foreach (var item in list) {
                item.Recycle();
            }
            ListPool<Message>.Shared.Recycle(list);
        }
    }
}
