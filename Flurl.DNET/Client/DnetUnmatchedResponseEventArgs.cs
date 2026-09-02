using System;

namespace Flurl.DNET
{
    /// <summary>
    /// 为无法匹配当前等待请求的DNET消息提供事件数据。
    /// Response只在事件回调期间有效，全部订阅者返回后由Flurl.DNET自动释放。
    /// </summary>
    public sealed class DnetUnmatchedResponseEventArgs : EventArgs
    {
        /// <summary>
        /// 初始化未匹配响应事件数据。
        /// </summary>
        /// <param name="response">本次无法匹配的响应。</param>
        internal DnetUnmatchedResponseEventArgs(DnetResponse response)
        {
            Response = response ?? throw new ArgumentNullException(nameof(response));
        }

        /// <summary>
        /// 本次无法匹配的响应，仅在事件回调期间有效。
        /// </summary>
        public DnetResponse Response { get; }
    }
}
