# Flurl.DNET

Flurl.DNET 是建立在 DNET 之上的链式异步请求/响应与强类型控制器库。客户端内部独占一个 `DNClient`，以协议头中的非零 `txrId` 匹配并发请求和乱序回复；服务端内部独占一个 `DNServer`，按 `eventType` 把 JSON 请求分发到控制器 Action。

## 客户端用法

```csharp
using Flurl.DNET;

using (var client = new FlurlDnetClient()) {
    await client.ConnectAsync("127.0.0.1", 9900, cancellationToken);

    LoginReply reply = await client
        .Request(LoginEvent)
        .WithTimeout(TimeSpan.FromSeconds(5))
        .SendJsonAsync(new LoginRequest { account = "dx" }, cancellationToken)
        .ReceiveJson<LoginReply>();
}
```

`SendAsync`、`SendStringAsync`、`SendJsonAsync` 返回 `Task<DnetResponse>`。直接取得 `DnetResponse` 时必须使用 `using` 释放；`ReceiveBytes()`、`ReceiveString()` 和 `ReceiveJson<T>()` 会在读取后自动释放响应。

```csharp
using (DnetResponse response = await client.Request(eventType).SendAsync(data)) {
    ArraySegment<byte> body = response.Body; // 仅在 response 释放前有效
}
```

## 未匹配消息

`txrId=0`、未知事务 ID、以及请求超时或取消后迟到的响应会进入 `UnmatchedReceived`。事件在独立的串行线程池泵中按到达顺序执行，不阻塞 DNET Socket 接收回调。

事件中的 `Response` 仅在本轮回调期间有效。不要保存或主动释放它；如果需要异步处理，请在回调中复制正文。

```csharp
client.UnmatchedReceived += (sender, args) => {
    byte[] body = args.Response.ToByteArray();
};
```

## 控制器服务端

控制器在 `Start` 前扫描一次并创建为单例。Action 必须返回 `Task<TReply>`，第一个参数是请求 DTO，之后可以依次追加 `DnetServerContext` 和 `CancellationToken`。框架自动解析 JSON、复制请求 `txrId` 与 `eventType` 并回复。

```csharp
[DnetController]
public sealed class LoginController
{
    [DnetRoute(LoginEvent)]
    public async Task<LoginReply> Login(
        LoginRequest request,
        DnetServerContext context,
        CancellationToken cancellationToken)
    {
        return await LoginService.LoginAsync(request, cancellationToken);
    }
}

using (var server = new FlurlDnetServer()) {
    server.RequestFailed += (sender, args) => Log(args.Exception);
    server.AddControllers(typeof(LoginController).Assembly);
    server.Start(9900);
    await server.StopAsync();
}
```

同一 Peer 的 Action 严格按消息到达顺序执行，不同 Peer 可以并行。网络接收回调只转移池化消息所有权，JSON 和用户代码始终在线程池执行。请求正文完成反序列化后会立即回收，不会在异步业务等待期间占用 DNET 缓冲区。

默认使用 `Activator.CreateInstance` 创建控制器，框架负责释放这些实例。接入现有依赖注入容器时使用 `UseControllerFactory(type => container.Resolve(type))`；外部工厂返回的控制器由调用方管理生命周期。单例控制器必须是线程安全的，连接级认证或会话数据可以存放在 `context.Connection.Items`。

## 远端错误

找不到路由、非 JSON 请求、JSON 解析失败、Action 异常、服务端过载和停止状态会沿用请求 `txrId` 返回保留事件类型 `DnetFrameworkEventTypes.Error`。客户端会直接抛出 `FlurlDnetRemoteException`，可通过 `ErrorCode` 判断 `RouteNotFound`、`InvalidPayload`、`ServerBusy` 等错误，不必等待请求超时。

`DnetActionException` 可用于返回业务自定义错误码和安全消息。未处理异常默认只向客户端返回通用说明，真实异常通过服务端 `RequestFailed` 事件报告。`txrId=0` 被视为单向通知，会执行控制器但不自动回复。

## 底层服务端回复

不需要控制器框架时，仍可直接使用 `DNServer.PeerReceived` 和 `DnetReplyExtensions`。Reply 扩展会自动沿用请求的 `TxrId`，未指定 `eventType` 时也会沿用请求值。调用方仍负责回收请求 `Message` 和接收列表。

不要让 `FlurlDnetServer` 与其他代码同时消费同一个底层服务器的 `PeerReceived` 或接收队列；`FlurlDnetServer` 从结构上不公开其内部 `DNServer`。

DNET 当前的 `PeerManager` 是进程级单例，因此同一进程只允许一个活动的 `FlurlDnetServer`，也不要同时启动另一套原始 `DNServer`。关闭完成后可以创建并启动新的服务器实例。

## 行为约束

- 调用请求前必须先等待 `ConnectAsync` 成功。
- 默认请求超时为 30 秒，可通过 `DefaultTimeout` 或单请求 `WithTimeout` 修改。
- 客户端不自动重连或重试，避免服务器已经执行请求时产生重复业务操作。
- `TrySend` 入队失败、发送背压、连接中断、主动断开和释放都会及时结束对应等待任务。
- `ReceiveString` 和 `ReceiveJson<T>` 只接受 `Format.Text` 或 `Format.Json`。首版不处理 GZIP 格式。
- `DnetFrameworkEventTypes.Error` 是框架保留事件类型，客户端请求和服务端业务路由都禁止使用。
- 服务端全局待处理请求上限默认为 4096；超过上限立即返回 `ServerBusy`，可通过 `FlurlDnetServerOptions.MaxPendingRequests` 调整。
- `Format.Heart` 不进入业务控制器；`StopAsync` 会取消连接令牌、回收排队消息并等待活动 Action 到达关闭超时。
