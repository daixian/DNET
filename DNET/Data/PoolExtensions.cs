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
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        public static void Recycle<T>(this List<T> list)
        {
            if (list == null) return;
            ListPool<T>.Shared.Recycle(list);
        }
    }
}
