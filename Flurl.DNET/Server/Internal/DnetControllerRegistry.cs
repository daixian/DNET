using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DNET;

namespace Flurl.DNET
{
    /// <summary>
    /// 在服务器启动阶段发现控制器、创建单例实例、校验Action签名并构建冻结路由表。
    /// 默认工厂创建的控制器由注册器拥有；外部工厂返回的实例始终由调用方管理生命周期。
    /// </summary>
    internal sealed class DnetControllerRegistry : IDisposable
    {
        #region 状态与构建

        /// <summary>
        /// 可选的外部控制器工厂；null表示使用Activator创建。
        /// </summary>
        private readonly Func<Type, object> _controllerFactory;

        /// <summary>
        /// 记录由默认工厂创建、需要在服务器最终停止后释放的控制器。
        /// </summary>
        private readonly List<object> _ownedControllers = new List<object>();

        /// <summary>
        /// 标记注册器是否已经释放。
        /// </summary>
        private int _disposed;

        /// <summary>
        /// 使用可选外部控制器工厂初始化注册器。
        /// </summary>
        /// <param name="controllerFactory">外部单例控制器工厂；null使用Activator。</param>
        internal DnetControllerRegistry(Func<Type, object> controllerFactory)
        {
            _controllerFactory = controllerFactory;
        }

        /// <summary>
        /// 扫描指定程序集并构建已经完整校验的路由表。
        /// </summary>
        /// <param name="assemblies">调用方显式注册的程序集集合。</param>
        /// <returns>启动后只读的事件路由表。</returns>
        internal DnetRouteTable Build(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null) throw new ArgumentNullException(nameof(assemblies));
            if (Volatile.Read(ref _disposed) != 0) {
                throw new ObjectDisposedException(nameof(DnetControllerRegistry));
            }

            var routes = new Dictionary<int, DnetActionDescriptor>();
            try {
                foreach (Type controllerType in FindControllerTypes(assemblies)) {
                    object controller = CreateController(controllerType);
                    int routeCount = RegisterControllerRoutes(controllerType, controller, routes);
                    if (routeCount == 0) {
                        throw new InvalidOperationException(
                            $"DNET控制器{controllerType.FullName}没有声明任何DnetRoute Action。");
                    }
                }
                if (routes.Count == 0) {
                    throw new InvalidOperationException("注册的程序集中没有找到任何DNET控制器路由。");
                }
                return new DnetRouteTable(routes);
            } catch {
                Dispose();
                throw;
            }
        }

        #endregion

        #region 控制器发现与创建

        /// <summary>
        /// 从程序集集合中按稳定名称顺序发现公开、非抽象控制器类型。
        /// </summary>
        /// <param name="assemblies">要扫描的程序集。</param>
        /// <returns>稳定排序后的控制器类型序列。</returns>
        private static IEnumerable<Type> FindControllerTypes(IEnumerable<Assembly> assemblies)
        {
            return assemblies
                .Where(assembly => assembly != null)
                .Distinct()
                .SelectMany(GetLoadableTypes)
                .Where(type => type.IsClass
                    && !type.IsAbstract
                    && (type.IsPublic || type.IsNestedPublic)
                    && type.GetCustomAttribute<DnetControllerAttribute>(false) != null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal);
        }

        /// <summary>
        /// 读取程序集全部类型，并把类型加载失败转换为包含程序集名称的启动异常。
        /// </summary>
        /// <param name="assembly">要读取的程序集。</param>
        /// <returns>程序集中的全部类型。</returns>
        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try {
                return assembly.GetTypes();
            } catch (ReflectionTypeLoadException exception) {
                string loaderMessages = string.Join(" | ",
                    exception.LoaderExceptions
                        .Where(item => item != null)
                        .Select(item => item.Message));
                throw new InvalidOperationException(
                    $"扫描程序集{assembly.FullName}中的DNET控制器失败：{loaderMessages}",
                    exception);
            }
        }

        /// <summary>
        /// 使用外部工厂或Activator创建并校验单例控制器。
        /// </summary>
        /// <param name="controllerType">控制器类型。</param>
        /// <returns>可供路由表长期调用的控制器实例。</returns>
        private object CreateController(Type controllerType)
        {
            object controller;
            if (_controllerFactory == null) {
                controller = Activator.CreateInstance(controllerType);
                _ownedControllers.Add(controller);
            } else {
                controller = _controllerFactory(controllerType);
            }

            if (controller == null || !controllerType.IsInstanceOfType(controller)) {
                throw new InvalidOperationException(
                    $"控制器工厂没有为{controllerType.FullName}返回兼容实例。");
            }
            return controller;
        }

        #endregion

        #region 路由注册与校验

        /// <summary>
        /// 校验并注册一个控制器上的全部DnetRoute方法。
        /// </summary>
        /// <param name="controllerType">控制器类型。</param>
        /// <param name="controller">单例控制器实例。</param>
        /// <param name="routes">正在构建的全局路由字典。</param>
        /// <returns>当前控制器成功注册的路由数量。</returns>
        private static int RegisterControllerRoutes(Type controllerType,
            object controller,
            Dictionary<int, DnetActionDescriptor> routes)
        {
            int routeCount = 0;
            MethodInfo[] methods = controllerType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(method => method.MetadataToken)
                .ToArray();
            foreach (MethodInfo method in methods) {
                DnetRouteAttribute route = method.GetCustomAttribute<DnetRouteAttribute>(false);
                if (route == null) continue;

                ValidateRoute(method, route.EventType);
                var descriptor = new DnetActionDescriptor(route.EventType,
                    controller,
                    method,
                    method.GetParameters()[0].ParameterType);
                if (routes.TryGetValue(route.EventType, out DnetActionDescriptor existing)) {
                    throw new InvalidOperationException(
                        $"DNET事件类型{route.EventType}存在重复路由："
                        + $"{existing.Method.DeclaringType.FullName}.{existing.Method.Name} 与 "
                        + $"{controllerType.FullName}.{method.Name}。");
                }
                routes.Add(route.EventType, descriptor);
                routeCount++;
            }
            return routeCount;
        }

        /// <summary>
        /// 验证路由事件类型、参数顺序和Task泛型返回类型是否符合首版控制器约定。
        /// </summary>
        /// <param name="method">待验证Action。</param>
        /// <param name="eventType">Action声明的事件类型。</param>
        private static void ValidateRoute(MethodInfo method, int eventType)
        {
            string actionName = $"{method.DeclaringType.FullName}.{method.Name}";
            if (eventType == DnetFrameworkEventTypes.Error) {
                throw new InvalidOperationException(
                    $"Action {actionName}不能使用Flurl.DNET保留事件类型{eventType}。");
            }
            if (method.IsGenericMethodDefinition || method.ContainsGenericParameters) {
                throw new InvalidOperationException($"Action {actionName}不能是泛型方法。");
            }
            if (!method.ReturnType.IsGenericType
                || method.ReturnType.GetGenericTypeDefinition() != typeof(Task<>)) {
                throw new InvalidOperationException(
                    $"Action {actionName}必须返回Task<TReply>。");
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length < 1 || parameters.Length > 3) {
                throw new InvalidOperationException(
                    $"Action {actionName}必须包含一个请求DTO，并且最多追加上下文和取消标记。");
            }
            Type requestType = parameters[0].ParameterType;
            if (requestType == typeof(DnetServerContext)
                || requestType == typeof(CancellationToken)
                || requestType.IsByRef) {
                throw new InvalidOperationException($"Action {actionName}的第一个参数必须是请求DTO。");
            }

            bool foundContext = false;
            bool foundCancellation = false;
            for (int i = 1; i < parameters.Length; i++) {
                Type parameterType = parameters[i].ParameterType;
                if (parameterType == typeof(DnetServerContext) && !foundContext) {
                    if (foundCancellation) {
                        throw new InvalidOperationException(
                            $"Action {actionName}的DnetServerContext必须位于CancellationToken之前。");
                    }
                    foundContext = true;
                    continue;
                }
                if (parameterType == typeof(CancellationToken) && !foundCancellation) {
                    foundCancellation = true;
                    continue;
                }
                throw new InvalidOperationException(
                    $"Action {actionName}只允许在请求DTO后追加DnetServerContext和CancellationToken。");
            }
        }

        #endregion

        #region 生命周期释放

        /// <summary>
        /// 逆序释放所有由默认Activator创建且实现IDisposable的控制器。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            for (int i = _ownedControllers.Count - 1; i >= 0; i--) {
                if (_ownedControllers[i] is IDisposable disposable) {
                    try {
                        disposable.Dispose();
                    } catch (Exception exception) {
                        if (LogProxy.Warning != null)
                            LogProxy.Warning($"DnetControllerRegistry.Dispose():释放控制器异常 {exception}");
                    }
                }
            }
            _ownedControllers.Clear();
        }

        #endregion
    }
}
