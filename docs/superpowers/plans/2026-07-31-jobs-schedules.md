# Jobs Schedules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:test-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有 Jobs 模块中增加 Host 级一次性/Cron 计划、误触发策略、暂停恢复和可追溯执行历史，并保持双库原子领取与现有执行租约语义。

**Architecture:** `fn_jobs_schedule` 是调度真源，Cronos 只负责五段 Cron 与时区/DST 计算。Worker 在同一数据库事务内锁定到期计划、创建 `fn_jobs_execution` 记录并推进下一次触发，再复用现有执行领取、租约、重试和故障恢复。

**Tech Stack:** .NET 10、Minimal API、Dapper 显式 SQL、SQL Server、MySQL、Cronos 0.13.0、MSTest、DbUp。

**Status:** Completed on 2026-07-31. Implementation and verification evidence is recorded in [`jobs-schedules-2026-07-31.md`](../../verification/jobs-schedules-2026-07-31.md).

## Global Constraints

- 迁移号固定为成对 `040_JobsSchedules.sql`；`044`、`045` 归 CodeGeneration，禁止占用。
- 只支持 `manual`、`one_time`、`cron`，禁止动态 C#、HTTP 脚本和运行时程序集加载。
- 时区标识接受 IANA 或 Windows ID，持久化为规范 IANA ID；所有执行时刻持久化为 UTC。
- `skip` 只跳过跨过至少两个 Cron 周期的积压；正常轮询延迟仍执行当前周期。`fire_once` 把多个遗漏周期折叠为一次执行。
- 到期计划领取顺序固定为 `NextExecutionAtUtc, Id`，批大小复用 `Jobs:Worker:BatchSize`。
- 计划推进与执行记录创建必须位于同一事务；执行本身继续复用现有租约、续租、重试和失败终态。
- 所有 SQL 使用 `SqlDataScope.HostOnly` 并显式限制 `TenantId IS NULL`。
- 手写注释使用中文解释事务、时区、DST 与误触发不变量。

---

### Task 1: Cron 与时区计算

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `THIRD-PARTY-NOTICES`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Full.NET.Modules.Jobs.csproj`
- Create: `src/Modules/Full.NET.Modules.Jobs/Scheduling/JobScheduleCalculator.cs`
- Test: `tests/Full.NET.UnitTests/Jobs/JobScheduleCalculatorTests.cs`

**Interfaces:**
- Produces: `JobScheduleCalculator.TryNormalizeTimeZoneId(string, out string, out TimeZoneInfo)`
- Produces: `JobScheduleCalculator.GetNextCronOccurrence(string, TimeZoneInfo, DateTimeOffset)`
- Produces: `JobScheduleCalculator.CalculateDue(JobScheduleRecord, DateTimeOffset)`
- Produces: `JobScheduleDueDecision(bool CreateExecution, DateTimeOffset? ScheduledForUtc, DateTimeOffset? NextExecutionAtUtc, DateTimeOffset? CompletedAtUtc)`

- [ ] **Step 1: Write failing calculator tests**

  Cover Windows-to-IANA normalization, invalid IDs/expressions, spring DST gap, autumn overlap, one-time completion, normal Cron polling, `skip` backlog and `fire_once` backlog.

- [ ] **Step 2: Run RED**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~JobScheduleCalculatorTests
  ```

  Expected: compile failure because `JobScheduleCalculator` and schedule records do not exist.

- [ ] **Step 3: Add Cronos and minimal calculator**

  Add central/package references for exact version `0.13.0`, register its MIT notice, normalize Windows IDs through `TimeZoneInfo.TryConvertWindowsIdToIanaId`, and use `CronExpression.Parse(..., CronFormat.Standard)`.

- [ ] **Step 4: Run GREEN**

  Run the same focused test command; expected all calculator methods pass with zero warnings.

### Task 2: Schedule contracts, persistence and Host API

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Contracts/JobContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Contracts/JobsErrorCodes.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobRecords.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs`
- Create: `src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobSchedules/HostJobScheduleService.cs`
- Create: `src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobSchedules/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/JobsModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/JobsAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Serialization/JobsJsonSerializerContext.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Resources/JobsErrors.resx`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Resources/JobsErrors.en-US.resx`
- Test: `tests/Full.NET.UnitTests/Jobs/HostJobScheduleServiceTests.cs`

**Interfaces:**
- Produces: `HostJobScheduleResponse`
- Produces: `CreateHostJobScheduleRequest`, `UpdateHostJobScheduleRequest`, `ChangeHostJobScheduleStateRequest`
- Produces endpoints under `/api/v1/jobs/host-schedules`
- Produces permissions `jobs.schedules.read` and `jobs.schedules.write`

- [ ] **Step 1: Write failing service tests**

  Cover invalid trigger combinations, disabled definitions, create/read/update, optimistic concurrency, pause retaining the due instant and resume recalculating from the current UTC instant.

- [ ] **Step 2: Run RED**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~HostJobScheduleServiceTests
  ```

  Expected: compile failure because the schedule service and contracts do not exist.

- [ ] **Step 3: Implement minimal Host-only service and endpoints**

  Keep HTTP mapping in `Endpoint`, validation/transaction rules in `HostJobScheduleService`, and SQL/record mapping in the existing persistence boundary.

- [ ] **Step 4: Run GREEN**

  Run the same focused test command; expected all schedule service tests pass with zero warnings.

### Task 3: Atomic schedule materialization in Worker

**Files:**
- Create: `src/Modules/Full.NET.Modules.Jobs/Scheduling/JobScheduleDispatcher.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionHostedProcessor.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/JobsModule.cs`
- Test: `tests/Full.NET.UnitTests/Jobs/JobScheduleDispatcherTests.cs`
- Test: `tests/Full.NET.UnitTests/Jobs/JobExecutionHostedProcessorTests.cs`

**Interfaces:**
- Produces: `JobScheduleDispatcher.ProcessDueAsync(int batchSize, CancellationToken)`
- Consumes: existing `JobExecutionRunner.ProcessPendingAsync`

- [ ] **Step 1: Write failing dispatcher and processor tests**

  Assert stable/bounded provider-specific selection, transaction use, execution insert plus schedule advance, disabled definition exclusion, affected-row invariant and processor ordering.

- [ ] **Step 2: Run RED**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobScheduleDispatcherTests|FullyQualifiedName~JobExecutionHostedProcessorTests"
  ```

  Expected: compile failure because dispatcher behavior is absent.

- [ ] **Step 3: Implement minimal dispatcher**

  SQL Server uses `UPDLOCK, READPAST, ROWLOCK`; MySQL uses `FOR UPDATE SKIP LOCKED`. For each locked row, calculate the due decision, optionally insert one execution, then update the schedule version; any zero affected row throws and rolls back the batch.

- [ ] **Step 4: Run GREEN**

  Run the same focused tests; expected all pass with zero warnings.

### Task 4: Paired migration and dual-provider vertical slice

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/040_JobsSchedules.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/040_JobsSchedules.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration040JobsSchedulesRecoveryTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Jobs/JobsScheduleAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsHostDefinitionAssertions.cs`
- Modify: `contracts/openapi/jobs-host-definitions-v1.json`
- Modify: `tests/Full.NET.IntegrationTests/Api/OpenApiJobsHostDefinitionsContractAssertions.cs`

**Interfaces:**
- Produces table `fn_jobs_schedule`
- Adds nullable `JobScheduleId` and `ScheduledForUtc` to `fn_jobs_execution`
- Produces dual-provider API/Worker acceptance

- [ ] **Step 1: Write migration recovery and vertical-slice tests**

  Cover missing table/column/index recovery, schedule CRUD, pause/resume, one-time completion, Cron materialization, `skip`/`fire_once`, execution linkage and expired execution lease recovery.

- [ ] **Step 2: Run RED**

  Run only the selector-matched migration and Jobs tests after the task snapshot is active; expected failure because migration `040` is absent.

- [ ] **Step 3: Add idempotent paired migrations**

  SQL Server uses explicit existence/shape checks. MySQL converges implicit-commit partial states with a temporary procedure. UUID columns use `uniqueidentifier`/`BINARY(16)` and UTC uses `datetimeoffset(7)`/`datetime(6)`.

- [ ] **Step 4: Run GREEN**

  Run SQL Server/MySQL Jobs API, Worker schedule, migration `040` recovery and affected slice; expected both providers pass and teardown leaves zero containers.

### Task 5: Governance, documentation and final verification

**Files:**
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/capability-status.md`
- Create: `docs/verification/jobs-schedules-2026-07-31.md`
- Modify: `eng/testing/test-matrix.json` only after fresh discovery

- [ ] **Step 1: Run inner planning**

  ```powershell
  pnpm test:integration:affected:plan -- --snapshot adminnet-jobs-schedules-task5-20260731 --phase inner
  ```

- [ ] **Step 2: Run governance**

  Run `pnpm test:naming`, Jobs Unit, architecture/global SQL catalog, Jobs backlog benchmark contract and focused Release build.

- [ ] **Step 3: Run slice**

  ```powershell
  pnpm test:integration:affected -- --snapshot adminnet-jobs-schedules-task5-20260731 --phase slice
  ```

- [ ] **Step 4: Fresh discovery and documentation**

  Update the matrix only from fresh discovery, record exact commands/results and mark parity `Verified` only after both providers and teardown pass.

- [ ] **Step 5: Final hygiene**

  Run `git diff --check`, inspect scoped diff/status, confirm no shared runner/container remains, then release shared `.NET`/Docker to queued tasks.
