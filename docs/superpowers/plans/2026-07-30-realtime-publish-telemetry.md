# Realtime Publish Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `IRealtimePublisher` 的取消令牌真实进入 SignalR 发送边界，并记录可告警的低基数发布结果与耗时。

**Architecture:** `SignalRRealtimePublisher` 改用未类型化 `IHubContext<FullNetNotificationHub>` 的 `SendCoreAsync`，客户端方法名和单一 `RealtimeMessage` 载荷保持不变，同时传入调用方 `CancellationToken`。独立发布 Telemetry 使用现有 `fullnet.realtime` Meter，仅记录目标类别与结果枚举；观测失败不得改变发送结果。

**Tech Stack:** .NET 10、ASP.NET Core SignalR、System.Diagnostics.Metrics、OpenTelemetry、MSTest、NSubstitute

## Global Constraints

- 只修改 Realtime Abstractions/SignalR BuildingBlock、Realtime Unit 和 Realtime 运维/验证文档。
- 不修改 Jobs、CodeGeneration、Notifications、Host Program、数据库、迁移、路线图或 `eng/testing/test-matrix.json`。
- `ReceiveMessageAsync` 客户端方法名、MessagePack/JSON 载荷与至少一次 Outbox 语义保持不变。
- 标签键只使用 `target`、`outcome`；值只允许 `user|group` 与 `success|failure|canceled`。
- 不记录用户、组名、租户、消息机器码、异常类型或异常文本。
- Docker 与其它窗口串行避让；运行 affected 前必须重新确认占用并在 teardown 后明确释放。
- 共享脏工作区不暂存、不提交。

---

### Task 1: SignalR 发送取消与结果指标

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Realtime.SignalR/RealtimePublishTelemetry.cs`
- Modify: `src/BuildingBlocks/Full.NET.Realtime.SignalR/SignalRRealtimePublisher.cs`
- Modify: `src/BuildingBlocks/Full.NET.Realtime.Abstractions/IRealtimePublisher.cs`
- Test: `tests/Full.NET.UnitTests/Realtime/SignalRRealtimePublisherTests.cs`

**Interfaces:**
- Consumes: `IClientProxy.SendCoreAsync(string, object?[], CancellationToken)`。
- Produces: `RealtimePublishTelemetry.Record(long, string, string)`。
- Preserves: 客户端调用名 `ReceiveMessageAsync` 与单参数 `RealtimeMessage` 载荷。

- [x] **Step 1: 写入取消传播 RED**

```csharp
var cancellation = new CancellationTokenSource();
cancellation.Cancel();
var publisher = CreatePublisher(out var clientProxy);
clientProxy
    .SendCoreAsync(
        nameof(IFullNetNotificationClient.ReceiveMessageAsync),
        Arg.Any<object?[]>(),
        cancellation.Token)
    .Returns(Task.FromCanceled(cancellation.Token));

await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
    publisher.PublishToUserAsync(
        Guid.CreateVersion7(),
        CreateMessage(),
        cancellation.Token));
```

- [x] **Step 2: 写入发布指标 RED**

```csharp
await publisher.PublishToGroupAsync(
    RealtimeGroups.HostBroadcast,
    CreateMessage());

AssertMeasurement(
    "fullnet.realtime.publish.attempts",
    target: "group",
    outcome: "success");
AssertMeasurement(
    "fullnet.realtime.publish.duration",
    target: "group",
    outcome: "success");
```

- [x] **Step 3: 运行测试确认 RED**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
& 'tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.exe' --filter 'FullyQualifiedName~SignalRRealtimePublisherTests'
```

Expected: 旧构造函数要求强类型 Hub Context，且不会调用带取消令牌的 `SendCoreAsync`；指标断言没有测量值。

- [x] **Step 4: 实现最小发送适配与 Telemetry**

```csharp
internal sealed class SignalRRealtimePublisher(
    IHubContext<FullNetNotificationHub> hubContext)
    : IRealtimePublisher
{
    private Task PublishAsync(
        string target,
        string groupName,
        RealtimeMessage message,
        CancellationToken cancellationToken) =>
        hubContext.Clients
            .Group(groupName)
            .SendCoreAsync(
                nameof(IFullNetNotificationClient.ReceiveMessageAsync),
                [message],
                cancellationToken);
}
```

`PublishAsync` 必须等待发送任务，按 `success`、调用方取消 `canceled` 和其它异常
`failure` 记录结果后原样返回或抛出。

- [x] **Step 5: 运行聚焦测试确认 GREEN**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
& 'tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.exe' --filter 'FullyQualifiedName~Full.NET.UnitTests.Realtime'
```

Expected: Realtime 聚焦测试全部通过；取消令牌精确传入 SignalR，异常对象保持不变，Telemetry 只含封闭标签。

### Task 2: 单节点发布 Meter 注册

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Realtime.SignalR/ServiceCollectionExtensions.cs`
- Modify: `tests/Full.NET.UnitTests/Realtime/RealtimeBackplaneRegistrationTests.cs`

**Interfaces:**
- Consumes: `RealtimeBackplaneTelemetry.MeterName`，仍为 `fullnet.realtime`。
- Produces: Realtime 启用但未配置 Redis 时也注册发布指标 Meter。

- [x] **Step 1: 写入单节点注册 RED**

```csharp
services.AddFullNetRealtimeSignalR(
    new ConfigurationBuilder().Build(),
    "Testing");
using var provider = services.BuildServiceProvider();

Assert.IsNotNull(provider.GetRequiredService<MeterProvider>());
```

- [x] **Step 2: 运行注册测试确认 RED**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
& 'tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.exe' --filter 'FullyQualifiedName~RealtimeBackplaneRegistrationTests'
```

Expected: 未配置 Redis 的启用模式尚未注册 `MeterProvider`。

- [x] **Step 3: 将 Meter 注册提升到所有启用模式**

```csharp
services
    .AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(
        RealtimeBackplaneTelemetry.MeterName));
```

该注册放在 `Realtime:Enabled` 检查之后、Redis 条件分支之前；禁用模式继续不注册
SignalR 发布器和 Realtime Meter。

- [x] **Step 4: 运行注册与 Release build**

Run:

```powershell
dotnet build src/BuildingBlocks/Full.NET.Realtime.SignalR/Full.NET.Realtime.SignalR.csproj -c Release --no-restore --nologo
& 'tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.exe' --filter 'FullyQualifiedName~Full.NET.UnitTests.Realtime'
```

Expected: BuildingBlock 0 warning / 0 error，Realtime 聚焦测试全部通过。

### Task 3: 运维与切片验证

**Files:**
- Modify: `docs/operations/realtime-redis-backplane.md`
- Modify: `docs/verification/realtime-signalr-foundation-2026-07-26.md`

**Interfaces:**
- Consumes: `fullnet.realtime.publish.attempts` 与 `fullnet.realtime.publish.duration`。
- Produces: 发布失败率、取消率和 P95/P99 的平台无关告警基线。

- [x] **Step 1: 登记发布指标语义**

```text
fullnet.realtime.publish.attempts{target,outcome}
fullnet.realtime.publish.duration{target,outcome}
```

文档必须说明成功只表示 SignalR 服务端发送任务完成，不表示浏览器已处理；可靠业务状态
仍由数据库与事务 Outbox 提供。

- [x] **Step 2: 运行影响集与完成门禁**

Run:

```powershell
pnpm test:naming
pnpm test:integration:affected:plan -- --snapshot realtime-publish-telemetry-20260730 --phase slice
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --nologo
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll `
  --no-ansi --progress off `
  --filter 'FullyQualifiedName~Realtime' `
  --minimum-expected-tests 7 --timeout 30m
docker ps --format "{{.ID}} {{.Names}} {{.Status}}"
git diff --check
git status --short --branch
```

Actual: 快照计划同时命中 Realtime 与其它窗口后写入的 CodeGeneration 文件。为避免越界执行，
本窗口按矩阵同一过滤器仅执行 Realtime 目标；双 Provider Realtime 通过。并发
CodeGeneration affected 会话退出后已确认 `docker ps` 为空；diff check 与工作区状态在最终
收口时执行。
