using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DNET
{
    public static class DNETExt
    {
        /// <summary>
        /// 从 SocketAsyncEventArgs 的 UserToken 中获取 ConnectionContext 类型的对象。
        /// 如果转换失败，返回 null。
        /// </summary>
        public static ConnectionContext GetConnectionContext(this SocketAsyncEventArgs args)
        {
            return args.UserToken as ConnectionContext;
        }

        /// <summary>
        /// 设置 SocketAsyncEventArgs 的 UserToken 为 ConnectionContext 对象。
        /// </summary>
        public static void SetConnectionContext(this SocketAsyncEventArgs args, ConnectionContext context)
        {
            args.UserToken = context;
        }
    }
}
