# Jobs Failure Retry Classification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Jobs Worker 增加显式、默认关闭且有界的失败重试，使可重试失败延迟后重新领取，普通失败继续立即终止。

**Architecture:** `IJobHandler` 的实现通过抛出 `RetryableJobException` 显式声明失败可重试；Runner 根据当前 `AttemptCount`、`MaxAttempts` 和固定延迟决定回到 `pending` 或进入 `failed`。数据库使用 `NextAttemptAtUtc` 控制 SQL Server/MySQL 的领取资格，不引入内存计时器，也不改变 HTTP/JSON 契约。

**Tech Stack:** .NET 10、MSTest、Dapper、DbUp、SQL Server、MySQL、Microsoft.Extensions.Options

## Global Constraints

- 当前任务基线为 `975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`，任务快照为 `jobs-retry-classification-20260730`。
- 仅修改 Jobs 模块、`037` 成对迁移、Jobs 专项测试和本计划；必要的 Worker
  示例配置、037 测试矩阵登记、验证记录与 Jobs 能力矩阵行在和所有者窗口协调后
  精确更新；不修改 CodeGeneration、Tools、性能/Outbox 文件。
- `MaxAttempts` 默认值必须为 `1`，现有部署未显式配置时保持“失败立即终止”。
- 普通异常、缺失 Handler 和取消不得进入重试；只有 `RetryableJobException` 可以重试。
- SQL Server 与 MySQL 必须使用相同列名 `NextAttemptAtUtc`，且迁移必须支持 DbUp 未记账的半完成重入。
- 当前共享工作区已脏，不暂存、不提交，也不覆盖其他任务改动。

---

### Task 1: Freeze Retry Policy and Configuration

**Files:**
- Create: `src/Modules/Full.NET.Modules.Jobs/Execution/RetryableJobException.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobsWorkerOptions.cs`
- Test: `tests/Full.NET.UnitTests/Jobs/JobsWorkerOptionsTests.cs`
- Test: `tests/Full.NET.UnitTests/Jobs/JobExecutionRunnerTests.cs`

**Interfaces:**
- Produces: `public sealed class RetryableJobException : Exception`
- Produces: `JobsWorkerOptions.MaxAttempts` and `JobsWorkerOptions.RetryDelaySeconds`

- [x] **Step 1: Write failing options tests**

Add assertions that defaults are `MaxAttempts = 1` and `RetryDelaySeconds = 30`, and that startup validation rejects `MaxAttempts = 0`/`11` and `RetryDelaySeconds = 0`/`86401`.

- [x] **Step 2: Run the focused options tests and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsWorkerOptionsTests"
```

Expected: compile failure because the two option properties do not exist.

- [x] **Step 3: Implement the minimal public marker and bounded options**

Add the exception with standard message and inner-exception constructors. Add default values and validator messages:

```text
Jobs:Worker:MaxAttempts must be between 1 and 10.
Jobs:Worker:RetryDelaySeconds must be between 1 and 86400.
```

- [x] **Step 4: Run the focused options tests and verify GREEN**

Run the Step 2 command and require all discovered tests to pass with no warnings.

### Task 2: Add Database Due-Time Contract

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/037_JobsRetryScheduling.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/037_JobsRetryScheduling.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration037JobsRetrySchedulingRecoveryTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobRecords.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs`

**Interfaces:**
- Produces: nullable `fn_jobs_execution.NextAttemptAtUtc`
- Produces: `JobSql.RescheduleExecution`
- Consumes: `JobExecutionRecord.NextAttemptAtUtc`

- [x] **Step 1: Write failing migration recovery tests**

For both providers, migrate through the current head, remove `037` from `SchemaVersions`, leave `NextAttemptAtUtc` present while dropping the new pending index, rerun migration, and assert one nullable time column plus one correctly named pending index.

- [x] **Step 2: Run the focused migration tests and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Migration037JobsRetrySchedulingRecoveryTests"
```

Expected: failure because migration `037` and its schema objects do not exist.

- [x] **Step 3: Implement paired idempotent migrations and SQL projections**

The SQL Server migration must add `NextAttemptAtUtc datetimeoffset(7) NULL`, replace `IX_fn_jobs_execution_PendingLease` with `IX_fn_jobs_execution_PendingNextAttemptLease` on `(Status, NextAttemptAtUtc, LeaseExpiresAtUtc, CreatedAtUtc)`, and preserve the `Status = 'pending'` filter. The MySQL migration must add `datetime(6) NULL` and the same four-column index without a filtered predicate. Both scripts must use schema inspection so a partially completed, unrecorded migration converges.

Update every execution projection, insert, and claim predicate. Pending rows are claimable only when:

```sql
(NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
```

Acquisition clears `NextAttemptAtUtc` after ownership is established. `MarkExecutionSucceeded` and `MarkExecutionFailed` also clear it. `RescheduleExecution` sets `Status = pending`, clears lease and finish fields, retains the bounded error message, and sets `NextAttemptAtUtc`.

- [x] **Step 4: Run migration tests and SQL naming gates**

Run:

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Migration037JobsRetrySchedulingRecoveryTests"
pnpm test:naming
pnpm test:sql-safety
```

Expected: focused recovery tests and both static SQL gates pass.

### Task 3: Implement RED-GREEN Runner Classification

**Files:**
- Modify: `tests/Full.NET.UnitTests/Jobs/JobExecutionRunnerTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs`

**Interfaces:**
- Consumes: `RetryableJobException`, `MaxAttempts`, `RetryDelaySeconds`, `JobSql.RescheduleExecution`
- Preserves: host cancellation propagation and terminal handling for all other exceptions

- [x] **Step 1: Write failing runner tests**

Add three focused tests:

1. a retryable exception with `AttemptCount = 1`, `MaxAttempts = 3` calls `RescheduleExecution` with `NextAttemptAtUtc = clock.UtcNow + RetryDelaySeconds`;
2. the same exception with `AttemptCount = 3` calls `MarkExecutionFailed`;
3. an ordinary exception with remaining attempts calls `MarkExecutionFailed`.

The recording command executor must capture statement parameters so the due time and bounded message can be asserted.

- [x] **Step 2: Run the runner tests and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobExecutionRunnerTests"
```

Expected: failure because retryable failures are still marked terminal and `RescheduleExecution` does not exist.

- [x] **Step 3: Implement the minimal classification**

Catch `RetryableJobException` after the cancellation branch. If `execution.AttemptCount < MaxAttempts`, call `RescheduleExecution`; otherwise call `MarkExecutionFailed`. Keep the existing generic exception branch terminal. Truncate persisted messages to 2000 characters in one shared helper.

- [x] **Step 4: Run all Jobs unit tests**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~Full.NET.UnitTests.Jobs"
```

Expected: all Jobs tests pass with no warning or skipped test.

### Task 4: Verify Real Dual-Provider Retry Lifecycle

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Jobs/JobsRetrySchedulingAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsHostDefinitionAssertions.cs`

**Interfaces:**
- Consumes: real `JobExecutionRunner`, database clock values, paired `037` schema
- Produces: SQL Server/MySQL evidence for due-time gating and attempt exhaustion

- [x] **Step 1: Write the dual-provider failing lifecycle assertion**

Register a scoped test handler that throws `RetryableJobException`. Insert one execution, run with `MaxAttempts = 2`, and assert:

- first processing returns `pending`, `AttemptCount = 1`, non-null `NextAttemptAtUtc`, cleared lease, null finish;
- a second processing before the due time returns `0`;
- after advancing a test clock beyond `NextAttemptAtUtc`, processing reaches `failed`, `AttemptCount = 2`, null due time, cleared lease, non-null finish.

- [x] **Step 2: Run SQL Server and MySQL Jobs tests and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~JobsApiSqlServerTests|FullyQualifiedName~JobsApiMySqlTests"
```

Expected: failure until the paired schema and runner retry path are wired into the real scope.

- [x] **Step 3: Complete only the test-service registration required by the real lifecycle**

Use `configureTestServices` to register the test handler as `IJobHandler`, then construct the scoped Runner with `MaxAttempts = 2`, `RetryDelaySeconds = 30` and a controllable clock. Do not add production-only test hooks.

- [x] **Step 4: Run affected verification**

Run:

```powershell
pnpm test:integration:affected:plan -- --snapshot jobs-retry-classification-20260730 --phase inner
pnpm test:integration:affected -- --snapshot jobs-retry-classification-20260730 --phase slice
dotnet build src/Modules/Full.NET.Modules.Jobs/Full.NET.Modules.Jobs.csproj -c Release
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release
git diff --check
git status --short --branch
```

Expected: selector contains Jobs and migration `037` recovery tests; selected tests and both Release builds pass; diff check is clean except pre-existing line-ending warnings.

实际共享任务快照还包含其他窗口在快照后写入的 CodeGeneration、Notifications、
Realtime 与 Worker 文件，因此没有重复执行被扩大到约 14 分钟的完整 slice。当前窗口
改为运行 Jobs Unit、Jobs SQL Server/MySQL 生命周期、037 SQL Server/MySQL 恢复测试
以及 SQL/naming 静态门禁；共享影响集保留给各所有者窗口和 main CI。

### Task 5: Restore Worker Host Context

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionHostedProcessor.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobExecutionHostedProcessorTests.cs`

- [x] **Step 1: Reproduce the real Worker failure in a focused test**

Notifications 的真实 Worker 宿主验证暴露 `HostContextRequiredException`。专项测试在
领取 SQL 执行时记录 `CurrentTenantAccessor.IsHost`，修复前稳定观测为 `false`。

- [x] **Step 2: Establish and clear Host Context per polling scope**

`ProcessOnceAsync` 在创建 Scope 后先调用 `SetHost()`，并在 `finally` 中调用
`Clear()`；这与 Outbox、Auditing Retention 等后台 Processor 的既有边界一致。

- [x] **Step 3: Verify RED to GREEN**

`JobExecutionHostedProcessorTests` 从 **1/2 失败**（领取时无 Host Context）变为
**2/2 通过**，同时断言每轮结束后 Context 已清理。

## Self-Review

- Spec coverage: explicit classification, bounded defaults, due-time persistence, attempt exhaustion, cancellation preservation, dual-provider recovery, and real dual-provider behavior each map to one task.
- Placeholder scan: no deferred implementation, undefined interface, or vague error-handling step remains.
- Type consistency: `RetryableJobException`, `MaxAttempts`, `RetryDelaySeconds`, `NextAttemptAtUtc`, and `JobSql.RescheduleExecution` use the same names in all tasks.
