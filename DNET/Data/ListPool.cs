﻿using System.Collections.Generic;
using System.Collections.Concurrent;

namespace DNET
{
    /// <summary>
    /// 表示一个可复用的 List 对象池，用于减少频繁创建和销毁 List 所带来的性能开销。
    /// </summary>
    /// <typeparam name="T">列表中元素的类型。</typeparam>
    public class ListPool<T>
    {
        /// <summary>
        /// 内部使用的对象池，使用 ConcurrentStack 保证线程安全。
        /// </summary>
        private readonly ConcurrentStack<List<T>> _pool = new ConcurrentStack<List<T>>();

        /// <summary>
        /// 池的最大容量，控制最多缓存多少个 List 实例。
        /// </summary>
        private readonly int _maxCapacity;

        /// <summary>
        /// 初始化一个新的 ListPool 实例，并指定最大缓存容量。
        /// </summary>
        /// <param name="maxCapacity">池的最大容量，默认为256。</param>
        public ListPool(int maxCapacity = 256)
        {
            _maxCapacity = maxCapacity;
        }

        /// <summary>
        /// 从池中获取一个 List 实例。如果池中没有可用实例，则会新建一个。
        /// </summary>
        /// <returns>一个 List 实例。</returns>
        public List<T> Get()
        {
            if (_pool.TryPop(out var list)) {
                return list;
            }
            return new List<T>();
        }

        /// <summary>
        /// 将使用完的 List 实例归还到池中，以便后续复用。
        /// </summary>
        /// <param name="list">要回收的 List 实例。</param>
        public void Recycle(List<T> list)
        {
            // 如果当前 List 的容量过大（超过4KB），则不再回收，防止内存膨胀
            if (list == null || list.Capacity > 4 * 1024) return;

            list.Clear(); // 清空列表内容，确保下次使用时是干净的

            // 如果当前池中的对象数量未达到上限，则将该列表推入池中
            if (_pool.Count < _maxCapacity) {
                _pool.Push(list);
            }
            // 超过最大容量就丢弃，避免池无限膨胀
        }

        /// <summary>
        /// 获取一个默认共享的 ListPool 实例，适用于大多数场景下的 List 复用。
        /// </summary>
        public static readonly ListPool<T> Shared = new ListPool<T>();
    }
}
