using System;
using System.Collections.Concurrent;

namespace DNET
{
    /// <summary>
    /// 对象池
    /// </summary>
    /// <typeparam name="T">池中对象的类型</typeparam>
    public class Pool<T> where T : class, new()
    {
        /// <summary>
        /// 初始化一个新的 Pool 实例，并指定最大缓存容量。
        /// </summary>
        /// <param name="maxCapacity">池的最大容量，默认为4096。</param>
        public Pool(int maxCapacity = 4096)
        {
            _maxCapacity = maxCapacity;
        }

        /// <summary>
        /// 存放对象的容器
        /// </summary>
        private readonly ConcurrentBag<T> _pool = new ConcurrentBag<T>();

        /// <summary>
        /// 池的最大容量
        /// </summary>
        private readonly int _maxCapacity;

        /// <summary>
        /// 池的大约计数.使用这个性能比_pool.Count性能高
        /// </summary>
        private int _count = 0;

        /// <summary>
        /// 获取一个对象。如果池中没有就创建新对象。
        /// </summary>
        public T Get()
        {
            T item;
            if (_pool.TryTake(out item)) {
                _count--;
                return item;
            }
            return new T();
        }

        /// <summary>
        /// 回收一个对象到池中。
        /// </summary>
        /// <param name="item">要回收的对象</param>
        public void Recycle(T item)
        {
            if (item == null) return;

            // 如果当前池中的对象数量未达到上限，则将该列表推入池中
            // TODO: _count 在多线程下可能存在竞态，必要时可考虑原子操作
            if (_count < _maxCapacity) {
                _pool.Add(item);
                _count++;
            }
            // 超过最大容量就丢弃，避免池无限膨胀
        }

        /// <summary>
        /// 当前池中缓存的对象数量(ConcurrentBag.Count本身是一个大略的计数)
        /// </summary>
        public int Count => _pool.Count;

        /// <summary>
        /// 全局共享池
        /// </summary>
        public static Pool<T> Shared { get; } = new Pool<T>();
    }
}
