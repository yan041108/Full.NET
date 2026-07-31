# Jobs Backlog Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `fullnet-module-delivery`, `fullnet-performance-hardening` and `superpowers:test-driven-development` while executing this plan task-by-task.

**Goal:** 以单次有界双库聚合查询暴露 Jobs 待处理深度、最老可领取等待年龄、到期重试数量和最老到期重试年龄。

**Architecture:** Jobs 模块内部新增只读快照读取器，按 Provider 选择 SQL Server/MySQL 聚合语句；HostedProcessor 在现有 Host Scope 内按独立采样间隔读取一次并记录无标签 Gauge。数据库或指标旁路失败只记录日志，领取和执行语义保持不变。

**Tech Stack:** .NET 10、Dapper 抽象、SQL Server、MySQL 8、OpenTelemetry Metrics、MSTest、Testcontainers。

## Global Constraints

- 保持 `MaxConcurrency = 1` 默认值和现有领取、续租、终态、重试语义。
- 只读取 `TenantId IS NULL` 的 Host Jobs 数据，不新增 API、迁移、缓存或跨模块契约。
- 每次采样只执行一个聚合 SQL；默认 `BacklogSampleSeconds = 30`，合法范围 `5..3600`。
- 指标不包含 JobKey、ExecutionId、TenantId、异常、SQL 或 URL 标签。
- SQL Server/MySQL 使用现有 037 pending 索引；没有执行计划与代表性数据前不声明性能收益。
- 共享工作区不暂存、不提交，不覆盖其它窗口维护的测试矩阵数值。

---

### Task 1: 冻结快照、SQL 与 Provider 映射契约

**Files:**
- Create: `src/Modules/Full.NET.Modules.Jobs/Execution/JobsBacklogSnapshot.cs`
- Create: `src/Modules/Full.NET.Modules.Jobs/Execution/JobsBacklogReader.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs`
- Create: `tests/Full.NET.UnitTests/Jobs/JobsBacklogReaderTests.cs`

**Interfaces:**
- Consumes: `IQueryExecutor`、`IOptions<DatabaseOptions>`、`JobExecutionStatuses.Pending`。
- Produces: `Task<JobsBacklogSnapshot> ReadAsync(DateTimeOffset observedAtUtc, CancellationToken cancellationToken)`。

- [ ] **Step 1: 写入 SQL Server 映射 RED**

  用记录型 `IQueryExecutor` 断言读取器选择 `jobs.read_backlog.sql_server`，传入 `ObservedAtUtc` 与
  `PendingStatus`，并把 `DateTimeOffset?` 行映射为快照。

- [ ] **Step 2: 运行 RED**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsBacklogReaderTests"`

  Expected: 编译失败，因为 `JobsBacklogReader` 和 `JobsBacklogSnapshot` 尚不存在。

- [ ] **Step 3: 实现最小双库读取器**

  SQL Server 使用 `COUNT_BIG(CASE...)`；MySQL 使用
  `COALESCE(SUM(CASE ... ELSE 0 END), 0)`。两条语句都以
  `WHERE TenantId IS NULL AND Status = @PendingStatus` 收敛数据，并在一次往返内返回：
  `PendingCount`、`OldestClaimableCreatedAtUtc`、`DueRetryCount`、`OldestDueRetryAtUtc`。
  MySQL `DateTime?` 必须显式按 UTC 转换为 `DateTimeOffset?`。

- [ ] **Step 4: 写入 MySQL 映射与未知 Provider RED/GREEN**

  断言 MySQL Statement、UTC 转换以及未知 Provider 抛出包含 Provider 名称的
  `InvalidOperationException`。

- [ ] **Step 5: 运行 GREEN**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsBacklogReaderTests"`

  Expected: 3/3 通过。

### Task 2: 建立有界采样与旁路失败语义

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobsWorkerOptions.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionHostedProcessor.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/JobsModule.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsWorkerOptionsTests.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobExecutionHostedProcessorTests.cs`

**Interfaces:**
- Consumes: `JobsBacklogReader.ReadAsync`、`IClock.UtcNow`。
- Produces: `JobsWorkerOptions.BacklogSampleSeconds` 和每个 HostedProcessor 实例独立的下一采样时间。

- [ ] **Step 1: 扩展现有 Options 测试并运行 RED**

  在既有默认值/边界测试中断言默认值 30、下界 5、上界 3600 和稳定错误消息。

- [ ] **Step 2: 扩展 HostedProcessor RED**

  断言同一采样窗口内两轮只读取一次 backlog、两轮仍各自领取；backlog 查询失败时领取仍执行；
  取消异常在宿主取消时继续传播。

- [ ] **Step 3: 实现最小采样**

  在 Host Context 建立后、领取前调用 `RecordBacklogAsync`。先推进下一采样点，再执行查询；普通异常
  使用稳定 LoggerMessage 记录并继续，匹配取消令牌的 `OperationCanceledException` 原样传播。

- [ ] **Step 4: 运行 GREEN**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobExecutionHostedProcessorTests|FullyQualifiedName~JobsWorkerOptionsTests"`

  Expected: 所有命中测试通过。

### Task 3: 记录低基数 Gauge

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobsTelemetry.cs`
- Create: `tests/Full.NET.UnitTests/Jobs/JobsBacklogTelemetryTests.cs`

**Interfaces:**
- Consumes: `JobsBacklogSnapshot` 和 `observedAtUtc`。
- Produces: `fullnet.jobs.backlog.executions`、`fullnet.jobs.backlog.oldest_age`、
  `fullnet.jobs.retry.due`、`fullnet.jobs.retry.oldest_due_age`。

- [ ] **Step 1: 写入 Gauge RED**

  使用只订阅 `Full.NET.Jobs` 的 `MeterListener`，断言 2、90 秒、3、120 秒四个测量值；空时间输出 0，
  未来时间年龄钳制为 0。

- [ ] **Step 2: 运行 RED**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsBacklogTelemetryTests"`

  Expected: 编译失败或缺少四个测量值。

- [ ] **Step 3: 实现 Gauge**

  使用无标签 `Gauge<long>`/`Gauge<double>`；全部记录包裹在现有观测旁路异常隔离中。

- [ ] **Step 4: 运行 GREEN**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsBacklogTelemetryTests"`

  Expected: 全部通过。

### Task 4: 双库真实语义与成本证据

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsRetrySchedulingAssertions.cs`
- Modify: `docs/operations/jobs-worker-observability.md`
- Create: `docs/verification/jobs-backlog-telemetry-2026-07-30.md`

**Interfaces:**
- Consumes: 现有 SQL Server/MySQL Jobs API 聚焦入口。
- Produces: 对未来重试、到期重试与终态清空的快照断言。

- [ ] **Step 1: 扩展现有重试生命周期断言并运行 RED**

  读取基线后断言：未来重试只增加 pending；时钟越过到期时间后 due 增加且最老到期时间存在；重试耗尽后
  两项恢复基线。该扩展不新增 Integration 测试方法。

- [ ] **Step 2: 运行 SQL Server/MySQL GREEN**

  在 Notifications 明确释放 Docker 后串行运行两个 `JobsApi*Tests` 聚焦入口，预期各 1/1。

- [ ] **Step 3: 保存计划与边界证据**

  记录 037 现有索引、双库实际执行计划、代表性测试数据规模与查询耗时；只证明查询可用且有界，不声明生产
  延迟或吞吐改善。

- [ ] **Step 4: 完成受影响验证**

  运行 Jobs Unit、Jobs 模块/Worker Release build、Architecture、Naming、affected plan、
  `git diff --check` 和共享工作区状态；按新鲜 discovery 协调测试矩阵，不覆盖其它窗口计数。
