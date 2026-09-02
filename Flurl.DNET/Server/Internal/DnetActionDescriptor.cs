using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Flurl.DNET
{
    /// <summary>
    /// 保存一个已经完成签名验证的控制器Action及其预编译调用委托。
    /// 反射只在启动阶段用于生成表达式，运行时不会通过MethodInfo.Invoke调用用户代码。
    /// </summary>
    internal sealed class DnetActionDescriptor
    {
        /// <summary>
        /// BoxTaskAsync泛型辅助方法的开放MethodInfo，仅在构建调用委托时使用。
        /// </summary>
        private static readonly MethodInfo _boxTaskMethod = typeof(DnetActionDescriptor)
            .GetMethod(nameof(BoxTaskAsync), BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>
        /// 已预编译的Action调用委托。
        /// </summary>
        private readonly Func<object, object, DnetServerContext, CancellationToken, Task<object>>
            _actionInvoker;

        /// <summary>
        /// 使用已验证的控制器实例和方法构建Action描述符。
        /// </summary>
        /// <param name="eventType">Action匹配的事件类型。</param>
        /// <param name="controller">单例控制器实例。</param>
        /// <param name="method">公开实例Action方法。</param>
        /// <param name="requestType">请求DTO类型。</param>
        internal DnetActionDescriptor(int eventType,
            object controller,
            MethodInfo method,
            Type requestType)
        {
            EventType = eventType;
            Controller = controller ?? throw new ArgumentNullException(nameof(controller));
            Method = method ?? throw new ArgumentNullException(nameof(method));
            RequestType = requestType ?? throw new ArgumentNullException(nameof(requestType));
            _actionInvoker = CompileInvoker(method);
        }

        /// <summary>
        /// 获取当前Action匹配的事件类型。
        /// </summary>
        internal int EventType { get; }

        /// <summary>
        /// 获取Action所属的单例控制器实例。
        /// </summary>
        internal object Controller { get; }

        /// <summary>
        /// 获取Action方法的启动期元数据，用于错误诊断。
        /// </summary>
        internal MethodInfo Method { get; }

        /// <summary>
        /// 获取请求正文需要反序列化到的DTO类型。
        /// </summary>
        internal Type RequestType { get; }

        /// <summary>
        /// 使用指定JSON设置把正文反序列化为Action请求参数。
        /// </summary>
        /// <param name="json">UTF-8解码后的JSON正文。</param>
        /// <param name="jsonSettings">当前服务器JSON设置。</param>
        /// <returns>Action需要的请求对象。</returns>
        internal object DeserializeRequest(string json, JsonSerializerSettings jsonSettings)
        {
            return JsonConvert.DeserializeObject(json, RequestType, jsonSettings);
        }

        /// <summary>
        /// 调用预编译Action并把Task泛型结果统一装箱为object。
        /// </summary>
        /// <param name="request">已经反序列化的请求DTO。</param>
        /// <param name="context">请求上下文。</param>
        /// <param name="cancellationToken">连接关闭取消标记。</param>
        /// <returns>异步Action的装箱结果。</returns>
        internal Task<object> InvokeAsync(object request,
            DnetServerContext context,
            CancellationToken cancellationToken)
        {
            return _actionInvoker(Controller, request, context, cancellationToken);
        }

        /// <summary>
        /// 根据Action的可选上下文参数生成强类型表达式并编译为统一调用委托。
        /// </summary>
        /// <param name="method">已经通过注册器验证的Action方法。</param>
        /// <returns>运行时不依赖反射调用的Action委托。</returns>
        private static Func<object, object, DnetServerContext, CancellationToken, Task<object>>
            CompileInvoker(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var controllerParameter = Expression.Parameter(typeof(object), "controller");
            var requestParameter = Expression.Parameter(typeof(object), "request");
            var contextParameter = Expression.Parameter(typeof(DnetServerContext), "context");
            var cancellationParameter = Expression.Parameter(typeof(CancellationToken),
                "cancellationToken");

            var arguments = new List<Expression>(parameters.Length) {
                Expression.Convert(requestParameter, parameters[0].ParameterType)
            };
            for (int i = 1; i < parameters.Length; i++) {
                Type parameterType = parameters[i].ParameterType;
                arguments.Add(parameterType == typeof(DnetServerContext)
                    ? (Expression)contextParameter
                    : cancellationParameter);
            }

            MethodCallExpression actionCall = Expression.Call(
                Expression.Convert(controllerParameter, method.DeclaringType),
                method,
                arguments);
            Type replyType = method.ReturnType.GetGenericArguments()[0];
            MethodCallExpression boxedTask = Expression.Call(
                _boxTaskMethod.MakeGenericMethod(replyType),
                actionCall);

            return Expression.Lambda<Func<object,
                    object,
                    DnetServerContext,
                    CancellationToken,
                    Task<object>>>(boxedTask,
                    controllerParameter,
                    requestParameter,
                    contextParameter,
                    cancellationParameter)
                .Compile();
        }

        /// <summary>
        /// 等待一个Task&lt;T&gt;并把结果装箱，以统一不同控制器回复类型的调度接口。
        /// </summary>
        /// <typeparam name="T">Action回复类型。</typeparam>
        /// <param name="task">控制器返回的任务。</param>
        /// <returns>装箱后的异步结果。</returns>
        private static async Task<object> BoxTaskAsync<T>(Task<T> task)
        {
            if (task == null) throw new InvalidOperationException("控制器Action返回了null任务。");
            return await task.ConfigureAwait(false);
        }
    }
}
