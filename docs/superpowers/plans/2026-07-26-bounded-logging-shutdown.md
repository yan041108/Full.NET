# Bounded Logging Shutdown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为普通与高优先级日志通道建立共享的有界退出预算，确保 Sink 阻塞时日志释放不会无限拖住宿主退出。

**Architecture:** 用 Full.NET 自有的双通道异步调度器替换两次独立 `Serilog.Sinks.Async` 包装。两条有界队列继续并行消费且调用方固定非阻塞；释放时同时停止接收并开始排空，优先等待高优先级 Worker，但两条通道共同受一个总预算约束，超时后丢弃尚未进入 Sink 的内存事件并让后台线程自行结束。

**Tech Stack:** .NET 10、Serilog 4.4、`BlockingCollection<LogEvent>`、MSTest、OpenTelemetry Metrics

## Global Constraints

- 普通日志与 `Error/Critical` 必须保持独立容量，调用方不得因队列满或退出排空而同步等待网络或磁盘。
- `FullNet:Logging:ShutdownFlushTimeout` 默认 5 秒，只允许大于 0 且不超过 30 秒；该值是两条通道共享的总预算，不是每条通道各自预算。
- 高优先级通道在释放阶段先获得剩余等待预算，但普通与高优先级 Worker 始终并行排空。
- 超时只能放弃仍在内存队列中的日志，不得中止线程、反转业务事务、改变数据库审计或 Outbox 语义。
- 本切片不引入磁盘 Spool、跨重启重放、外部可靠 Sink 或投递确认；这些能力需要独立 ADR 明确加密、磁盘满、保留和重复投递语义。
- 代码标识符使用英文；手写注释和 XML 文档注释使用中文并解释退出边界与风险。

---

### Task 1: 双通道共享退出预算

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/FullNetBoundedAsyncSink.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/FullNetLoggingPipelineSink.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability/FullNetLoggingPipeline.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability/LoggingOptions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability/ServiceDefaultsExtensions.cs`
- Test: `tests/Full.NET.UnitTests/Hosting/HighPriorityLoggingTests.cs`

**Interfaces:**
- Consumes: `ILogEventSink.Emit(LogEvent)`, `IAsyncLogEventSinkInspector`, `FullNetAsyncLogMonitor`
- Produces: `LoggingOptions.ShutdownFlushTimeout : TimeSpan`
- Produces: `FullNetLoggingPipelineSink : ILogEventSink, IDisposable`
- Produces: `FullNetBoundedAsyncSink.Complete()`, `WaitForCompletion(TimeSpan)`, `AbandonPending()`

- [x] **Step 1: 写入会在现有实现上失败的退出测试**

  在 `HighPriorityLoggingTests` 增加三个行为测试：

  1. `Logger_disposal_uses_one_total_timeout_for_both_blocked_channels`：普通和高优先级 Sink 都阻塞，配置 100ms 退出预算；`Dispose()` 必须在 1 秒内完成，而不是等待两个 Sink 各自超时。
  2. `Logger_disposal_drains_both_channels_before_timeout`：写入一条普通日志和一条错误日志后立即释放 Logger；两条事件必须都到达对应 Sink。
  3. `Service_defaults_reject_out_of_range_shutdown_flush_timeout`：零值和超过 30 秒都必须在启动注册阶段抛出 `OptionsValidationException`。

  测试辅助方法 `CreateLogger` 增加 `TimeSpan? shutdownFlushTimeout = null`，并将值写入 `LoggingOptions.ShutdownFlushTimeout`。阻塞测试必须在 `finally` 中释放测试 Sink，避免 RED 阶段遗留挂起线程。

- [x] **Step 2: 运行聚焦测试确认 RED**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~HighPriorityLoggingTests" --no-restore
  ```

  Expected: 新退出测试失败，因为当前 `Serilog.Sinks.Async` 的释放会等待被阻塞的 Sink；配置校验测试失败，因为 `ShutdownFlushTimeout` 尚不存在。

- [x] **Step 3: 实现单通道有界异步 Sink**

  `FullNetBoundedAsyncSink` 使用指定容量的 `BlockingCollection<LogEvent>` 与后台线程：

  - `Emit` 只调用 `TryAdd`；容量满、队列已完成或与释放竞态时递增 `DroppedMessagesCount`，不得等待。
  - 构造时调用 `monitor.StartMonitoring(this)` 并启动 `IsBackground = true` 的 Worker。
  - Worker 按队列顺序调用内部 Sink，队列完成后在 Worker 上释放内部 Sink。
  - `Complete()` 只停止接收新事件。
  - `WaitForCompletion(timeout)` 只等待 Worker，不超过传入预算。
  - `AbandonPending()` 原子取走尚未进入 Sink 的事件并计入丢弃数，然后调用 `monitor.StopMonitoring(this)`。
  - 禁止用 `Thread.Abort`、同步网络回退或无限 `Join()`。

- [x] **Step 4: 实现双通道路由与共享预算**

  `FullNetLoggingPipelineSink` 持有普通与高优先级两个 `FullNetBoundedAsyncSink`：

  - `Emit` 将 `Error/Fatal` 路由到高优先级，其余已通过最小等级的事件路由到普通通道。
  - `Dispose` 先对两条通道调用 `Complete()`，让两个 Worker 并行排空。
  - 使用单个 `Stopwatch` 计算共享剩余预算；先等待高优先级，再用剩余时间等待普通通道。
  - 到期后对两条通道调用 `AbandonPending()`；重复释放必须幂等。

  `FullNetLoggingPipeline.Configure` 继续设置全局最小等级、ASP.NET Core 覆盖、LogContext 与 Application 属性，但只注册一个 `FullNetLoggingPipelineSink`。两个传入的 Sink 配置分别构建为最小等级 `Verbose` 的内部 Logger，避免重复过滤或丢失 Warning/Error。

- [x] **Step 5: 增加配置和启动校验**

  在 `LoggingOptions` 增加：

  ```csharp
  public TimeSpan ShutdownFlushTimeout { get; set; } = TimeSpan.FromSeconds(5);
  ```

  `AddFullNetServiceDefaults` 在创建日志管道前拒绝：

  - `ShutdownFlushTimeout <= TimeSpan.Zero`
  - `ShutdownFlushTimeout > TimeSpan.FromSeconds(30)`

  校验消息必须包含稳定配置属性名 `ShutdownFlushTimeout`。

- [x] **Step 6: 运行聚焦测试确认 GREEN**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~HighPriorityLoggingTests|FullyQualifiedName~FullNetAsyncLogMonitorTests" --no-restore
  ```

  Expected: 原 7 项与新增 3 项全部通过，退出测试不依赖延长全局测试超时。

### Task 2: 文档、门槛与验证记录

**Files:**
- Modify: `README.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/operations/logging-degraded-mode.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/superpowers/plans/2026-07-18-architecture-hardening.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Create: `docs/verification/bounded-logging-shutdown-2026-07-26.md`

**Interfaces:**
- Consumes: Task 1 的 `ShutdownFlushTimeout`、共享预算语义与测试输出
- Produces: 运维配置边界、故障注入证据与 Task 8 剩余范围

- [x] **Step 1: 更新运维边界**

  `logging-degraded-mode.md` 必须说明：

  - 默认总预算 5 秒、允许范围 `(0, 30s]`。
  - 两个 Worker 并行排空且高优先级先获得等待权。
  - 超时后只放弃未进入 Sink 的内存事件；正在执行的阻塞 Sink 只能由后台线程自行返回。
  - 该机制不是持久化，不提供跨重启保证。

- [x] **Step 2: 同步状态与测试门槛**

  将新增 3 项 Unit 纳入 canonical 门槛。最终同步 Realtime 基线后，实际门槛从 `392` 更新为 `395`；四个 canonical 来源与最新审计记录必须使用该实际值：

  - `README.md`
  - `.github/workflows/ci.yml`
  - `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
  - `docs/development/getting-started.md`

  路线图和架构硬化计划将状态写为“Task 8B1 完成”，但完整 Task 8 仍不得标记 `Verified`；剩余范围明确为磁盘 Spool、平台不可用、磁盘满、跨重启与投递确认。

- [x] **Step 3: 写入验证记录**

  `bounded-logging-shutdown-2026-07-26.md` 记录 RED 失败、聚焦 GREEN、Release 构建、Unit/Compatibility/Architecture/Integration、治理门禁以及真实测试数量。不得把未运行的磁盘满或外部平台故障注入写成已验证。

- [x] **Step 4: 执行最终验证**

  Run:

  ```powershell
  dotnet build Full.NET.slnx -c Release --no-restore
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --minimum-expected-tests 395
  dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --minimum-expected-tests 7
  dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 49
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --minimum-expected-tests 189 --timeout 90m
  pnpm test:openapi
  pnpm test:openapi:breaking -- --base-ref main
  pnpm test:governance
  pnpm test:skills
  pnpm test:naming
  pnpm test:workspace
  pnpm test:integration:tooling
  pnpm test:integration:partitions
  git diff --check
  ```

  Expected: 所有命令退出码为 0；若仓库后续合并已提高任何测试门槛，以构建产物实际数量和最新 canonical 门槛为准，不得降低门槛。

- [x] **Step 5: 复盘并提交**

  读取 `rules/rule-evolution.md` 与 `rules/skill-evolution.md`，记录“无变化”或满足门槛的可审查差异。确认 `.cache/`、`.tmp/art-design-pro/` 与其他用户文件未暂存后提交：

  ```powershell
  git commit -m "feat: bound logging shutdown flush"
  ```

## Self-Review

- Spec coverage: 本计划只关闭 Task 8B 的进程退出有界刷新和对应故障注入；磁盘 Spool、外部平台可靠投递、磁盘满、跨重启与重复投递语义明确保留，未把局部能力描述为完整 Task 8。
- Placeholder scan: 每个行为、文件、命令、边界与停止条件均已明确；测试门槛已按最终同步树更新为 `395/7/49/189`。
- Type consistency: `ShutdownFlushTimeout`、`FullNetBoundedAsyncSink`、`FullNetLoggingPipelineSink`、`Complete()`、`WaitForCompletion(TimeSpan)` 与 `AbandonPending()` 在任务间保持一致。
