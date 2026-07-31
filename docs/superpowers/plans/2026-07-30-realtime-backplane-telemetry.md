# Realtime Backplane Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Realtime Redis ready 探针增加可导出的低基数状态、结果与耗时指标，并给出可执行的生产告警基线。

**Architecture:** 将 Redis 连接与 `PING` 封装为 Realtime BuildingBlock 内部探针，健康检查只负责超时、结果映射和指标记录。指标使用进程级 `Meter`，由 Realtime 注册扩展自动加入 OpenTelemetry；观测旁路故障不得改变 ready 的健康语义。

**Tech Stack:** .NET 10、ASP.NET Core Health Checks、StackExchange.Redis、System.Diagnostics.Metrics、OpenTelemetry、MSTest

## Global Constraints

- 只修改 Realtime SignalR BuildingBlock、Realtime Unit、Realtime 运维文档和现有 Realtime Verification。
- 不修改 Jobs、CodeGeneration、Notifications、Host Program、数据库迁移、`eng/testing/test-matrix.json` 或路线图。
- Docker 与 Jobs 窗口串行避让；仅在 Jobs 明确释放后运行选择器命中的 Realtime 双库影响集，teardown 并确认容器为空后再释放。
- Meter、Instrument 与标签键使用小写点分层/小写 snake_case；标签值限定为 `healthy`、`timeout`、`failure`。
- 指标监听器故障属于旁路故障，不得改变 ready 结果或覆盖原始取消语义。
- 共享脏工作区不执行暂存或提交。

---

### Task 1: Ready 探针结果指标

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Realtime.SignalR/Health/IRealtimeBackplaneProbe.cs`
- Create: `src/BuildingBlocks/Full.NET.Realtime.SignalR/Health/RealtimeBackplaneProbe.cs`
- Create: `src/BuildingBlocks/Full.NET.Realtime.SignalR/Health/RealtimeBackplaneTelemetry.cs`
- Modify: `src/BuildingBlocks/Full.NET.Realtime.SignalR/Health/RealtimeBackplaneHealthCheck.cs`
- Test: `tests/Full.NET.UnitTests/Realtime/RealtimeBackplaneTelemetryTests.cs`

**Interfaces:**
- Produces: `IRealtimeBackplaneProbe.PingAsync(string, CancellationToken)`，供健康检查执行 Redis 短连接探测。
- Produces: `RealtimeBackplaneTelemetry.Record(long, string, bool)`，以 `Stopwatch` 起始时间戳记录当前状态、结果计数和耗时。

- [x] **Step 1: 写入失败测试**

```csharp
var probe = Substitute.For<IRealtimeBackplaneProbe>();
probe.PingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
    .Returns(Task.CompletedTask);
var check = new RealtimeBackplaneHealthCheck(
    Options.Create(new RealtimeOptions
    {
        RedisBackplaneConnectionString = "127.0.0.1:6379",
    }),
    probe);

var result = await check.CheckHealthAsync(new HealthCheckContext());

Assert.AreEqual(HealthStatus.Healthy, result.Status);
CollectionAssert.AreEquivalent(
    new[] { "outcome" },
    measurements.Single(item =>
        item.Name == "fullnet.realtime.backplane.readiness.checks")
        .Tags.Select(tag => tag.Key).ToArray());
```

- [x] **Step 2: 运行聚焦测试确认 RED**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
```

Expected: 因探针与 Telemetry 类型尚不存在而编译失败。

- [x] **Step 3: 实现最小探针与指标**

```csharp
internal interface IRealtimeBackplaneProbe
{
    Task PingAsync(
        string connectionString,
        CancellationToken cancellationToken);
}
```

```csharp
internal static class RealtimeBackplaneTelemetry
{
    public const string MeterName = "fullnet.realtime";

    public static void Record(
        long startedTimestamp,
        string outcome,
        bool isReady)
    {
        // state=1 表示本次探针成功；结果标签只来自健康检查的封闭映射。
    }
}
```

健康检查对成功、内部两秒超时和 Redis/配置故障分别映射
`healthy`、`timeout`、`failure`；调用方取消继续传播且不伪造故障结果。

- [x] **Step 4: 运行聚焦测试确认 GREEN**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
& 'tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.exe' --filter 'FullyQualifiedName~RealtimeBackplaneTelemetryTests'
```

Expected: 新增健康、超时、失败、调用方取消和监听器故障测试全部通过。

### Task 2: Realtime 自注册 OpenTelemetry Meter

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Realtime.SignalR/Full.NET.Realtime.SignalR.csproj`
- Modify: `src/BuildingBlocks/Full.NET.Realtime.SignalR/ServiceCollectionExtensions.cs`
- Modify: `tests/Full.NET.UnitTests/Realtime/RealtimeBackplaneRegistrationTests.cs`

**Interfaces:**
- Consumes: `RealtimeBackplaneTelemetry.MeterName`
- Produces: 配置 Redis Backplane 时可由宿主既有 OpenTelemetry 管道导出的 Realtime Meter。

- [x] **Step 1: 写入注册失败测试**

```csharp
Assert.IsNotNull(
    provider.GetRequiredService<IRealtimeBackplaneProbe>());
Assert.IsNotNull(provider.GetRequiredService<MeterProvider>());
```

- [x] **Step 2: 运行注册测试确认 RED**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
& 'tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.exe' --filter 'FullyQualifiedName~RealtimeBackplaneRegistrationTests'
```

Expected: 内部探针或 MeterProvider 尚未注册。

- [x] **Step 3: 注册探针和 Meter**

```csharp
services.TryAddSingleton<
    IRealtimeBackplaneProbe,
    RealtimeBackplaneProbe>();
services
    .AddOpenTelemetry()
    .WithMetrics(metrics =>
        metrics.AddMeter(RealtimeBackplaneTelemetry.MeterName));
```

仅在 Redis Backplane 已配置并注册 ready 检查时注册，不改变禁用模式与单节点无 Redis 模式。

- [x] **Step 4: 运行 Realtime Unit 与 Release build**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
& 'tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.exe' --filter 'FullyQualifiedName~Full.NET.UnitTests.Realtime'
dotnet build src/BuildingBlocks/Full.NET.Realtime.SignalR/Full.NET.Realtime.SignalR.csproj -c Release --no-restore --nologo
```

Expected: Realtime Unit 全绿，BuildingBlock 为 0 warning / 0 error。

### Task 3: 运维告警与验证收口

**Files:**
- Modify: `docs/operations/realtime-redis-backplane.md`
- Modify: `docs/verification/realtime-signalr-foundation-2026-07-26.md`

**Interfaces:**
- Consumes: 三个 Realtime ready 指标及 `healthy|timeout|failure` 结果枚举。
- Produces: 多副本最小告警规则、排障顺序、证据边界和未验证项。

- [x] **Step 1: 登记指标与告警基线**

```text
fullnet.realtime.backplane.readiness.state
fullnet.realtime.backplane.readiness.checks{outcome}
fullnet.realtime.backplane.readiness.duration{outcome}
```

告警基线要求按实例聚合：连续两个 ready 周期 `state=0` 触发告警；任一实例持续失败时不得被副本平均值掩盖；`timeout` 与 `failure` 分开定位容量/网络和配置/协议问题。阈值必须由部署平台按实际探针周期换算，不在代码内假定固定抓取频率。

- [x] **Step 2: 运行文档与影响集检查**

Run:

```powershell
pnpm test:naming
pnpm test:integration:affected:plan -- --snapshot realtime-backplane-telemetry-20260730 --phase inner
pnpm test:integration:affected -- --snapshot realtime-backplane-telemetry-20260730 --phase inner
docker ps --format "{{.ID}} {{.Names}} {{.Status}}"
git diff --check
git status --short --branch
```

Expected: naming 通过；影响计划只作审计；diff check 无 whitespace error；状态中仅保留共享工作区与本切片预期变化。
