# 高优先级日志独立通道实施计划

> **Implementation mode:** Inline execution in the current task; each behavior change follows RED-GREEN-REFACTOR.

**Goal:** 防止普通日志队列过载时连带丢失 `Error/Critical`，并为高优先级通道提供独立低基数指标与就绪降级信号。

**Architecture:** `Full.NET.Hosting` 在同一个 Serilog 入口内按日志等级路由到两条独立的有界异步队列。普通通道承载 `Information/Warning`，高优先级通道只承载 `Error/Critical` 且固定为非阻塞；两条队列分别由 `FullNetAsyncLogMonitor` 观测。审计数据继续由业务事务、数据库写入或 Outbox 持久化，不经过日志队列。

**Tech Stack:** .NET 10、Serilog 4.4、Serilog.Sinks.Async 2.1、OpenTelemetry Metrics、ASP.NET Core Health Checks、MSTest。

## Global Constraints

- 请求线程不得因日志队列满而同步等待网络或磁盘。
- `Error/Critical` 不得与可丢弃普通日志共享唯一容量。
- 指标标签只允许固定的 `channel=general|high_priority`。
- 高优先级日志降级不得改变业务结果；审计不得改走日志 Sink。
- 本切片不声明磁盘 Spool 或外部可靠 Sink 已完成；相关故障注入属于 Task 8B。

---

### Task 1: 用过载测试锁定独立日志通道

**Files:**
- Create: `tests/Full.NET.UnitTests/Hosting/HighPriorityLoggingTests.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Properties/AssemblyInfo.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/FullNetLoggingPipeline.cs`

**Interfaces:**
- Consumes: `LoggingOptions.AsyncBufferSize`、`Serilog.ILogger`
- Produces: `FullNetLoggingPipeline.Configure(LoggerConfiguration, string, LoggingOptions, FullNetLoggingMonitors, Action<LoggerSinkConfiguration>, Action<LoggerSinkConfiguration>)`

- [x] **Step 1: 写普通通道阻塞时高优先级日志仍可交付的失败测试**

  测试使用阻塞的普通 Sink 占满容量，连续写入普通日志触发丢弃，再写入一条 `Error`；断言高优先级收集 Sink 在普通 Sink 释放前已经收到该事件。

- [x] **Step 2: 运行聚焦测试并确认 RED**

  Run:

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter-class "Full.NET.UnitTests.Hosting.HighPriorityLoggingTests"
  ```

  Expected: 因 `FullNetLoggingPipeline` 与 `FullNetLoggingMonitors` 尚不存在而编译失败。

- [x] **Step 3: 实现最小双通道路由**

  `FullNetLoggingPipeline` 必须执行以下路由：

  ```csharp
  configuration
      .WriteTo.Logger(general => general
          .Filter.ByExcluding(logEvent => logEvent.Level >= LogEventLevel.Error)
          .WriteTo.Async(
              configureGeneralSink,
              bufferSize: options.AsyncBufferSize,
              blockWhenFull: false,
              monitor: monitors.General))
      .WriteTo.Logger(highPriority => highPriority
          .MinimumLevel.Error()
          .WriteTo.Async(
              configureHighPrioritySink,
              bufferSize: options.HighPriorityAsyncBufferSize,
              blockWhenFull: false,
              monitor: monitors.HighPriority));
  ```

  `Properties/AssemblyInfo.cs` 只向 `Full.NET.UnitTests` 开放内部测试边界。兼容配置 `BlockWhenFull=true` 必须在启动时被拒绝，不能重新引入请求线程阻塞。

- [x] **Step 4: 运行聚焦测试并确认 GREEN**

  Expected: 普通队列发生丢弃，高优先级 Sink 在普通 Sink 仍阻塞时收到 `Error`。

---

### Task 2: 独立指标、配置校验与就绪降级

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability/FullNetAsyncLogMonitor.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability/LoggingOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/FullNetLoggingMonitors.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/HighPriorityLoggingHealthCheck.cs`
- Modify: `tests/Full.NET.UnitTests/Observability/FullNetAsyncLogMonitorTests.cs`
- Modify: `tests/Full.NET.UnitTests/Hosting/HighPriorityLoggingTests.cs`

**Interfaces:**
- Produces: `LoggingOptions.HighPriorityAsyncBufferSize`
- Produces: `FullNetLoggingMonitors.General` 与 `FullNetLoggingMonitors.HighPriority`
- Produces: `HighPriorityLoggingHealthCheck.CheckHealthAsync(...)`
- Produces metrics:
  - `fullnet.logging.queue.depth{channel}`
  - `fullnet.logging.queue.capacity{channel}`
  - `fullnet.logging.events.dropped{channel}`

- [x] **Step 1: 写高优先级固定非阻塞、指标标签和健康降级失败测试**

  覆盖以下行为：

  ```text
  1. 高优先级 Sink 阻塞且队列满时，调用方仍在有界时间内返回并累计 dropped。
  2. MeterListener 只观察到 general/high_priority 两个固定 channel。
  3. 高优先级队列深度达到容量 90% 时返回 Degraded，普通队列过载不改变健康状态。
  4. HighPriorityAsyncBufferSize <= 0 时启动校验失败。
  ```

- [x] **Step 2: 运行聚焦测试并确认 RED**

  Expected: 新配置、指标标签或健康检查缺失导致断言失败。

- [x] **Step 3: 实现最小监控与健康检查**

  - `FullNetAsyncLogMonitor` 接受固定通道名并为每个 ObservableGauge 返回带 `channel` 标签的 `Measurement<long>`。
  - `FullNetLoggingMonitors` 只拥有 `general` 与 `high_priority` 两个实例并负责释放。
  - 健康检查只根据高优先级当前队列占用率判断；丢弃累计值由指标告警处理，避免一次历史丢弃导致实例永久降级。
  - 配置校验必须同时要求两个容量为正数，高优先级通道禁止提供阻塞开关。

- [x] **Step 4: 运行聚焦测试并确认 GREEN**

  Expected: 所有高优先级日志聚焦测试与既有 Monitor 测试通过。

---

### Task 3: 接入三个宿主并同步运维事实

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability/ServiceDefaultsExtensions.cs`
- Create: `docs/operations/logging-degraded-mode.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/superpowers/plans/2026-07-18-architecture-hardening.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Create: `docs/verification/high-priority-logging-channel-2026-07-26.md`
- Modify canonical Unit thresholds in `README.md`, `.github/workflows/ci.yml`, `.agents/skills/fullnet-module-delivery/references/delivery-map.md`

**Interfaces:**
- Consumes: `IHostApplicationBuilder.AddFullNetServiceDefaults()`
- Produces: 三个官方宿主一致的双通道日志管道、`high_priority_logging` ready 健康检查与运维说明

- [x] **Step 1: 用服务注册测试确认当前只有单队列**

  扩展聚焦测试，构建 `HostApplicationBuilder` 后断言可解析唯一 `FullNetLoggingMonitors`，并通过两个测试 Sink 证明路由互斥。

- [x] **Step 2: 将 Service Defaults 改用共享双通道配置**

  Console Compact JSON 仍是本切片的本地可靠 Sink；普通与高优先级通道分别创建 Console Sink，避免共享 Async 容量。注册：

  ```csharp
  services.AddSingleton<FullNetLoggingMonitors>();
  services.AddHealthChecks().AddCheck<HighPriorityLoggingHealthCheck>(
      "high_priority_logging",
      failureStatus: HealthStatus.Degraded,
      tags: ["ready"]);
  ```

- [x] **Step 3: 同步文档与测试门槛**

  运维文档必须说明容量、非阻塞语义、指标、告警建议、健康降级、退出刷新限制，以及 Task 8B 尚未完成的外部 Sink/磁盘 Spool 故障注入。计划新增六项 Unit 测试，canonical 门槛从 `380` 更新为 `386`；若实际发现数量不同，必须先修正文档与门槛再继续。

- [x] **Step 4: 执行最终验证**

  Run:

  ```powershell
  dotnet build Full.NET.slnx -c Release
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --minimum-expected-tests 386
  dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --minimum-expected-tests 7
  dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 49
  pnpm test:naming
  pnpm test:skills
  pnpm test:governance
  pnpm test:integration:tooling
  pnpm test:integration:full
  git diff --check
  ```

  Expected: Release 构建零警告零错误；所有测试达到新门槛且无失败、无跳过；Integration 保持 184 项。

- [x] **Step 5: 完成规则与 Skill 复盘并确认提交范围**

  只在完整验证通过且工作区除用户既有 `.cache/`、`.tmp/art-design-pro/` 外无无关差异时提交：

  ```powershell
  git commit -m "feat: isolate high priority logging"
  ```

## Self-Review

- Spec coverage: 本计划关闭独立容量、非阻塞、指标、健康与运维边界；磁盘 Spool、外部平台不可用和真实进程退出演练明确留给 Task 8B，不把部分实现标记为完整 Task 8。
- Placeholder scan: 所有步骤包含精确文件、行为、命令和停止条件；Unit 计划门槛固定为 `386`，如发现数量不同必须先修正文档。
- Type consistency: `FullNetLoggingMonitors`、`General`、`HighPriority`、`HighPriorityAsyncBufferSize` 和 `HighPriorityLoggingHealthCheck` 在任务间保持一致；两条通道均固定 `blockWhenFull: false`。
