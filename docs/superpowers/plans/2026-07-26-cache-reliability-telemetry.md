# 缓存恢复与可靠性指标实施计划

> **Implementation status:** All planned tasks and verification checkpoints are complete.

**Goal:** 在不改变安全关键缓存语义的前提下，为本机/分布式失效、陈旧命中和
Backplane 断开/恢复提供低基数指标，并用延迟 Worker 双库场景锁定可靠事件确认与最终
收敛。

**Architecture:** `Full.NET.Caching.Fusion` 拥有统一 Meter 和 FusionCache 事件桥接，
Tenancy 只在既有 `TenantCacheInvalidator` 边界记录本机与分布式失效结果。指标只使用固定
`scope`、`outcome`、`state` 标签；不包含租户、域名、缓存键或异常文本。现有事务 Outbox
继续拥有跨节点可靠交付语义，指标失败不得改变业务结果。

**Tech Stack:** .NET 10、System.Diagnostics.Metrics、FusionCache 2.6.0、
OpenTelemetry、MSTest、SQL Server 2022、MySQL 8.4、Redis。

## Global Constraints

- 安全关键缓存继续全局关闭 Fail-Safe，不得以陈旧值完成租户解析或授权。
- API 提交后只同步修复本节点；跨节点传播仍只由事务 Outbox Worker 确认。
- 指标标签必须是固定低基数集合，禁止租户 ID、域名、缓存键和异常消息。
- 不新增缓存实现、通用 Repository、网络服务边界或数据库对象。
- SQL Server 与 MySQL 必须扩展同一组延迟 Worker/恢复断言。

---

### Task 1: 建立缓存可靠性 Meter 与事件桥接

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/CacheReliabilityTelemetry.cs`
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/FusionCacheReliabilityMonitor.cs`
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/Properties/AssemblyInfo.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/ServiceCollectionExtensions.cs`
- Create: `tests/Full.NET.UnitTests/Caching/CacheReliabilityTelemetryTests.cs`

**Interfaces:**
- Consumes: `IFusionCache.Events.Hit` 与
  `IFusionCache.Events.Backplane.CircuitBreakerChange`。
- Produces: `CacheReliabilityTelemetry.RecordLocalInvalidation(TimeSpan, bool)`、
  `RecordDistributedInvalidation(TimeSpan, bool)` 和 Meter
  `Full.NET.Caching.Reliability`。

- [x] **Step 1: 写入失败的指标合同测试**

  新建两个测试方法。第一个通过 `MeterListener` 调用
  `RecordLocalInvalidation` 与 `RecordDistributedInvalidation`，断言：

  ```csharp
  CollectionAssert.Contains(names, "fullnet.cache.invalidation.duration");
  CollectionAssert.Contains(names, "fullnet.cache.invalidation.failures");
  CollectionAssert.AreEquivalent(
      ["scope=local", "outcome=success"],
      successfulTags);
  CollectionAssert.AreEquivalent(
      ["scope=distributed", "outcome=failure"],
      failedTags);
  ```

  第二个实例化 `FusionCacheReliabilityMonitor`，直接调用测试可见的事件处理方法，
  传入 `FusionCacheEntryHitEventArgs("key", true)` 与
  `FusionCacheCircuitBreakerChangeEventArgs(false/true)`，断言以下指标：

  ```text
  fullnet.cache.hits.stale
  fullnet.cache.backplane.circuit.transitions state=open
  fullnet.cache.backplane.circuit.transitions state=closed
  fullnet.cache.backplane.recoveries
  ```

- [x] **Step 2: 运行测试并确认 RED**

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release `
    --filter "FullyQualifiedName~CacheReliabilityTelemetryTests"
  ```

  预期：编译失败，指出 `CacheReliabilityTelemetry` 或
  `FusionCacheReliabilityMonitor` 尚不存在。

- [x] **Step 3: 实现固定低基数 Meter**

  `CacheReliabilityTelemetry` 使用进程生命周期静态 `Meter`，定义：

  ```csharp
  public const string MeterName = "Full.NET.Caching.Reliability";

  public static void RecordLocalInvalidation(TimeSpan duration, bool succeeded) =>
      RecordInvalidation("local", duration, succeeded);

  public static void RecordDistributedInvalidation(TimeSpan duration, bool succeeded) =>
      RecordInvalidation("distributed", duration, succeeded);
  ```

  `RecordInvalidation` 记录毫秒 Histogram；失败时增加失败 Counter。事件桥接内部方法
  分别记录陈旧命中、Backplane `open/closed` 转换和 `closed` 恢复计数。所有标签值必须
  来自上述固定字面量。

- [x] **Step 4: 连接 FusionCache 事件与 OpenTelemetry**

  `FusionCacheReliabilityMonitor` 实现 `IHostedService`。`StartAsync` 订阅 Hit 与
  CircuitBreakerChange，`StopAsync` 对称退订；重复 Start/Stop 必须幂等。事件处理只调用
  `CacheReliabilityTelemetry`，不得抛出或记录缓存键。

  `AddFullNetCaching` 注册单例 Monitor 为 `IHostedService`，并在现有 Metrics Builder
  增加：

  ```csharp
  .WithMetrics(metrics => metrics
      .AddMeter(CacheReliabilityTelemetry.MeterName)
      .AddFusionCacheInstrumentation());
  ```

  `Properties/AssemblyInfo.cs` 只向 `Full.NET.UnitTests` 开放 internal 测试边界。

- [x] **Step 5: 运行聚焦测试并确认 GREEN**

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release `
    --filter "FullyQualifiedName~CacheReliabilityTelemetryTests"
  ```

  预期：2/2 通过。

### Task 2: 在租户缓存失效边界记录结果

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantCacheInvalidator.cs`
- Modify: `tests/Full.NET.UnitTests/Tenancy/HostTenantCacheInvalidationTests.cs`
- Modify: `tests/Full.NET.UnitTests/Tenancy/TenantChangedCacheInvalidationHandlerTests.cs`

**Interfaces:**
- Consumes: Task 1 的两个 `Record*Invalidation` 方法。
- Produces: 本机与分布式失效成功/失败时长；原异常与取消语义保持不变。

- [x] **Step 1: 扩展现有测试建立 RED**

  在本机失效成功测试中监听
  `fullnet.cache.invalidation.duration{scope=local,outcome=success}`；在现有
  ThrowingBackplane 测试中监听
  `duration{scope=distributed,outcome=failure}` 和
  `failures{scope=distributed,outcome=failure}`。运行：

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release `
    --filter "FullyQualifiedName~HostTenantCacheInvalidationTests|FullyQualifiedName~TenantChangedCacheInvalidationHandlerTests"
  ```

  预期：新增指标断言失败，因为 `TenantCacheInvalidator` 尚未记录。

- [x] **Step 2: 实现最小计时包装**

  `InvalidateAsync` 接收固定 `bool distributed`，使用 `Stopwatch.GetTimestamp()`，
  在成功后记录 `succeeded: true`，在 `catch` 中记录 `succeeded: false` 后原样
  `throw`。记录逻辑不得吞掉 `OperationCanceledException`、Backplane 或 L2 异常。

- [x] **Step 3: 运行聚焦测试并确认 GREEN**

  重复 Step 1 命令。预期：现有测试数量全部通过，原 Backplane 异常类型断言仍成立。

### Task 3: 锁定延迟 Worker 可靠确认与恢复收敛

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Caching/CacheConsistencyTests.cs`

**Interfaces:**
- Consumes: 既有 `TenantChanged` Outbox、双 API 节点、Redis Backplane 和
  `OutboxProcessor.ProcessOnceAsync` 测试装配。
- Produces: SQL Server/MySQL 各自证明 Worker 延迟时 `TenantChanged` 仍未确认；共享 L2
  可以让 secondary 提前收敛，但 Worker 恢复后仍必须正式确认事件，停用继续在消费后
  收敛为 NotFound。

- [x] **Step 1: 在现有双库方法增加恢复前断言**

  更新租户名称后读取该租户最新 `fullnet.tenancy.tenant.changed` Outbox 状态；调用
  Processor 前断言 `ProcessedAtUtc` 与 `DeadLetteredAtUtc` 均为空，调用后断言
  `ProcessedAtUtc` 已写入，并保留 secondary 最终收敛断言。不要把共享 L2 可能产生的
  提前收敛当成可靠发布完成证据。

- [x] **Step 2: 运行缓存聚焦 Integration**

  ```powershell
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll `
    --filter "FullyQualifiedName~CacheConsistencyTests" --no-ansi --progress off `
    --minimum-expected-tests 6 --timeout 20m
  ```

  预期：6/6 通过；SQL Server/MySQL 都观察到延迟确认窗口与恢复收敛。

### Task 4: 文档、门槛与完整验证

**Files:**
- Modify: `README.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/superpowers/plans/2026-07-18-architecture-hardening.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Create: `docs/verification/cache-reliability-telemetry-2026-07-26.md`

**Interfaces:**
- Consumes: Tasks 1–3 的新鲜 RED/GREEN 与双库证据。
- Produces: canonical 门槛 `380/7/49/184` 和 Task 7 的最新真实状态。

- [x] **Step 1: 同步状态与门槛**

  将 Unit 门槛由 378 提升到 380，其余保持 7/49/184。能力状态说明新增低基数缓存可靠性
  指标和延迟 Worker 双库证据，但完整 Outbox backlog 指标与生产告警仍未完成，因此能力
  继续标记 `Build-verified`，不得标记 `Verified`。

- [x] **Step 2: 执行完整门禁**

  ```powershell
  dotnet build Full.NET.slnx -c Release
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 380 --timeout 20m
  dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7 --timeout 10m
  dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 49 --timeout 10m
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 184 --timeout 90m
  pnpm test:naming
  pnpm test:skills
  pnpm test:governance
  pnpm test:integration:tooling
  pnpm test:integration:partitions
  git diff --check
  git status --short --branch
  ```

  预期：构建 0 warning/0 error；380/7/49/184 全部通过；Node 门禁全部通过。

- [x] **Step 3: 完成规则与 Skill 复盘**

  先按 `rules/rule-evolution.md` 判断是否形成第二次可泛化遗漏，再按
  `rules/skill-evolution.md` 判断是否需要演进项目 Skill。仅在既有升级门槛满足时修改规则
  或 Skill；否则在验证记录中写明“不升级”及证据。
