using System;
using System.Threading.Tasks;

namespace Flurl.DNET
{
    /// <summary>
    /// 为响应任务提供类似Flurl.Http的链式正文读取方法，并在读取结束后自动回收池化响应。
    /// </summary>
    public static class DnetResponseTaskExtensions
    {
        /// <summary>
        /// 等待响应并复制正文，随后自动释放响应。
        /// </summary>
        /// <param name="responseTask">返回DNET响应的任务。</param>
        /// <returns>可以独立于响应生命周期使用的正文数组。</returns>
        public static async Task<byte[]> ReceiveBytes(this Task<DnetResponse> responseTask)
        {
            if (responseTask == null) throw new ArgumentNullException(nameof(responseTask));

            DnetResponse response = await responseTask.ConfigureAwait(false);
            using (response) {
                return response.ToByteArray();
            }
        }

        /// <summary>
        /// 等待Text或Json响应并按UTF-8读取正文，随后自动释放响应。
        /// </summary>
        /// <param name="responseTask">返回DNET响应的任务。</param>
        /// <returns>UTF-8解码后的响应正文。</returns>
        public static async Task<string> ReceiveString(this Task<DnetResponse> responseTask)
        {
            if (responseTask == null) throw new ArgumentNullException(nameof(responseTask));

            DnetResponse response = await responseTask.ConfigureAwait(false);
            using (response) {
                return response.ReadAsString();
            }
        }

        /// <summary>
        /// 等待Text或Json响应并反序列化正文，随后自动释放响应。
        /// </summary>
        /// <typeparam name="T">目标对象类型。</typeparam>
        /// <param name="responseTask">返回DNET响应的任务。</param>
        /// <returns>反序列化得到的对象。</returns>
        public static async Task<T> ReceiveJson<T>(this Task<DnetResponse> responseTask)
        {
            if (responseTask == null) throw new ArgumentNullException(nameof(responseTask));

            DnetResponse response = await responseTask.ConfigureAwait(false);
            using (response) {
                return response.ReadAsJson<T>();
            }
        }
    }
}
