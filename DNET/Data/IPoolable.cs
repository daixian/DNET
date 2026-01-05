using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DNET
{
    /// <summary>
    /// 可用于对象池复用的类型应实现此接口。
    /// </summary>
    public interface IPoolable
    {
        /// <summary>
        /// 重置对象状态，使其可以被池复用。
        /// </summary>
        void Reset();

        /// <summary>
        /// 将对象归还到池中。
        /// </summary>
        void Recycle();
    }
}
