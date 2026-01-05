using System.Net.Sockets;

namespace DNET
{
    /// <summary>
    /// DNET静态扩展方法
    /// </summary>
    internal static class DNETExtension
    {
        /// <summary>
        /// 从 SocketAsyncEventArgs 的 UserToken 中获取 ConnectionContext 类型的对象。
        /// 如果转换失败，返回 null。
        /// </summary>
        /// <param name="args">异步事件参数</param>
        /// <returns>ConnectionContext 实例或 null</returns>
        internal static ConnectionContext GetConnectionContext(this SocketAsyncEventArgs args)
        {
            // TODO: 如需更严格的参数校验，可对 args 进行空检查并抛出异常
            return args.UserToken as ConnectionContext;
        }

        /// <summary>
        /// 设置 SocketAsyncEventArgs 的 UserToken 为 ConnectionContext 对象。
        /// </summary>
        /// <param name="args">异步事件参数</param>
        /// <param name="context">要设置的上下文对象</param>
        internal static void SetConnectionContext(this SocketAsyncEventArgs args, ConnectionContext context)
        {
            args.UserToken = context;
        }
    }
}
