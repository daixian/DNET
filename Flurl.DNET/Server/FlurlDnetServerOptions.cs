using System;
using Newtonsoft.Json;

namespace Flurl.DNET
{
    /// <summary>
    /// 配置FlurlDnetServer的JSON行为、全局过载上限和优雅关闭等待时间。
    /// 每个选项在赋值时完成边界校验，服务器启动后不再读取可变的外部配置引用。
    /// </summary>
    public sealed class FlurlDnetServerOptions
    {
        /// <summary>
        /// 默认允许排队和执行的请求总数。
        /// </summary>
        private const int _defaultMaxPendingRequests = 4096;

        /// <summary>
        /// Dispose等待活动控制器结束的默认最长时间。
        /// </summary>
        private static readonly TimeSpan _defaultShutdownTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 当前允许的最大待处理请求数。
        /// </summary>
        private int _maxPendingRequests = _defaultMaxPendingRequests;

        /// <summary>
        /// 当前优雅关闭等待时间。
        /// </summary>
        private TimeSpan _shutdownTimeout = _defaultShutdownTimeout;

        /// <summary>
        /// 当前服务器使用的Newtonsoft.Json设置。
        /// </summary>
        private JsonSerializerSettings _jsonSettings = new JsonSerializerSettings();

        /// <summary>
        /// 获取或设置全服务器允许排队和执行的请求总数，必须大于零。
        /// </summary>
        public int MaxPendingRequests {
            get => _maxPendingRequests;
            set {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                _maxPendingRequests = value;
            }
        }

        /// <summary>
        /// 获取或设置Dispose等待活动Action结束的最长时间，必须大于等于零。
        /// 零表示不等待，但后台清理仍会在活动Action结束后完成。
        /// </summary>
        public TimeSpan ShutdownTimeout {
            get => _shutdownTimeout;
            set {
                if (value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value));
                _shutdownTimeout = value;
            }
        }

        /// <summary>
        /// 获取或设置服务端请求与响应使用的Newtonsoft.Json配置。
        /// 传入null时自动恢复为默认设置。
        /// </summary>
        public JsonSerializerSettings JsonSettings {
            get => _jsonSettings;
            set => _jsonSettings = value ?? new JsonSerializerSettings();
        }

        /// <summary>
        /// 获取或设置是否把未处理Action异常消息返回客户端。
        /// 默认关闭，避免泄露服务端实现细节；真实异常始终通过RequestFailed报告。
        /// </summary>
        public bool IncludeExceptionDetails { get; set; }

        /// <summary>
        /// 创建一份独立快照，防止服务器启动后外部继续修改配置影响运行中不变量。
        /// JsonSerializerSettings按现有客户端约定共享，调用方应在启动前完成其配置。
        /// </summary>
        /// <returns>当前选项的独立快照。</returns>
        internal FlurlDnetServerOptions Clone()
        {
            return new FlurlDnetServerOptions {
                MaxPendingRequests = MaxPendingRequests,
                ShutdownTimeout = ShutdownTimeout,
                JsonSettings = JsonSettings,
                IncludeExceptionDetails = IncludeExceptionDetails
            };
        }
    }
}
