# Jobs Backlog Index A/B Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在相同容器、相同 10 万行数据与相同生产 SQL 下，对 Jobs backlog 专用候选索引建立 SQL Server/MySQL 查询收益、执行计划、索引体积和写路径回归证据，并在证据不足时阻止生产迁移。

**Architecture:** 保留现有 `jobs-backlog-query` baseline 模式，在同一命令增加 `index-ab` 模式。每个 Provider 只启动一个隔离数据库，按 `baseline -> candidate -> candidate -> baseline` 镜像块采样，使用候选索引的显式建删切换状态；查询始终直接消费生产 backlog Statement，触发、领取和终态探针直接消费生产写 Statement，并在事务回滚后保持数据集不漂移。

**Tech Stack:** .NET 10、MSTest、Dapper、Microsoft.Data.SqlClient、MySqlConnector、Testcontainers、DbUp、SQL Server 2022、MySQL 8.0。

## Global Constraints

- 任务基线为 `975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`，任务快照为 `jobs-backlog-index-ab-20260730`。
- 只修改 Jobs benchmark、Jobs Unit、性能地图和独立计划/验证文档；不修改 Files、CodeGeneration、Realtime/Notifications 或共享测试矩阵。
- 本切片不修改 037、生产 SQL、Worker 配置或默认并发；只有双库 A/B 与写路径门禁均满足后，才另建正式迁移切片。
- 正式证据固定 100000 行、并发 1、每个 Variant 5 次预热和 30 次查询采样；Provider 必须串行运行。
- 候选索引固定为 `IX_fn_jobs_execution_BacklogStatusTenant`：SQL Server 键为 `(Status, TenantId)` 并 INCLUDE `(NextAttemptAtUtc, CreatedAtUtc)`；MySQL 键为 `(Status, TenantId, NextAttemptAtUtc, CreatedAtUtc)`。
- 正确性必须覆盖 pending、due、最老可领取时间、最老到期时间；写探针必须覆盖生产触发插入、领取和成功终态语句，所有写探针事务均回滚。
- 查询收益必须同时报告 P50/P95/P99、实际读取行与计划；索引成本必须报告创建耗时、体积和写路径 P50/P95/P99。

---

### Task 1: A/B 参数、索引契约与镜像采样

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogIndexAb.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogQueryBenchmarkOptions.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsBacklogQueryBenchmarkTests.cs`

**Interfaces:**
- Produces: `JobsBacklogQueryBenchmarkMode`、`JobsBacklogIndexVariant`、`JobsBacklogIndexCandidate.ForProvider(string)`。
- Produces: `JobsBacklogIndexAbSampling.CreateBlocks(int)`，返回每个 Variant 总采样数相同的四个镜像块。
- Produces: `JobsBacklogQueryBenchmarkOptions.Mode` 与 `MutationIterations`。

- [ ] **Step 1: Write the failing tests**

  增加测试断言默认 `Mode == Baseline`，`--mode index-ab --mutation-iterations 5` 可解析，未知模式和越界写采样被拒绝；断言双库候选 DDL 包含稳定索引名与目标列；断言 5 次采样生成 `baseline(3), candidate(3), candidate(2), baseline(2)`。

- [ ] **Step 2: Run test to verify it fails**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsBacklogQueryBenchmarkTests"`

  Expected: FAIL，因为 `JobsBacklogQueryBenchmarkMode`、`JobsBacklogIndexCandidate` 与 `JobsBacklogIndexAbSampling` 尚不存在。

- [ ] **Step 3: Write minimal implementation**

  实现 `baseline/index-ab` 两种模式、`3..100` 的写采样边界、Provider 专用 CREATE/DROP DDL，以及确定性的四块镜像顺序；baseline 的默认参数和输出目录保持兼容。

- [ ] **Step 4: Run test to verify it passes**

  Run: 与 Step 2 相同。

  Expected: 所有 `JobsBacklogQueryBenchmarkTests` 通过。

### Task 2: A/B 报告契约与收益门禁

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogIndexAbReportWriter.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsBacklogQueryBenchmarkTests.cs`

**Interfaces:**
- Consumes: `JobsBacklogIndexVariant`、`JobsBacklogQueryStatistics`。
- Produces: `JobsBacklogMutationStatistics`、`JobsBacklogIndexVariantResult`、`JobsBacklogIndexProviderResult`、`JobsBacklogIndexAbReport`。
- Produces: `JobsBacklogIndexAbAssessment.Assess(...)`，候选查询 P95/P99 未同时改善、任一写路径 P95 回归超过 20%、正确性失败或计划为空时返回不允许迁移。

- [ ] **Step 1: Write the failing tests**

  构造 baseline/candidate 样本，断言只有查询 P95/P99 同时改善、三类写路径均不超过 20% 回归且正确性/计划完整时 `MigrationAllowed` 为真；报告必须包含索引体积、查询与三类写探针尾延迟及明确门禁结论。

- [ ] **Step 2: Run test to verify it fails**

  Run: Task 1 的聚焦命令。

  Expected: FAIL，因为 A/B 结果、门禁和 writer 尚不存在。

- [ ] **Step 3: Write minimal implementation**

  使用不可变 record 保存原始样本和统计，门禁只做同 Provider 的 baseline/candidate 比较；Markdown 明确声明 Testcontainers 结果不是生产 SLA，且跨 Provider 绝对延迟不得排名。

- [ ] **Step 4: Run test to verify it passes**

  Run: Task 1 的聚焦命令。

  Expected: 所有聚焦测试通过。

### Task 3: 双库候选索引与生产写路径探针

**Files:**
- Modify: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogBenchmarkDatabase.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsBacklogQueryBenchmarkTests.cs`

**Interfaces:**
- Consumes: `JobsBacklogIndexCandidate` 与生产 `JobSql` Statements。
- Produces: `SetIndexVariantAsync(JobsBacklogIndexVariant, CancellationToken)`。
- Produces: `GetCandidateIndexSizeBytesAsync(CancellationToken)`。
- Produces: `MeasureMutationAsync(JobsBacklogMutationKind, DateTimeOffset, int, CancellationToken)`。

- [ ] **Step 1: Write the failing contract test**

  断言写探针 Statement 依次精确引用 `JobSql.InsertExecution`、SQL Server/MySQL 领取 Statement 与 `JobSql.MarkExecutionSucceeded`，禁止复制漂移 SQL。

- [ ] **Step 2: Run test to verify it fails**

  Run: Task 1 的聚焦命令。

  Expected: FAIL，因为写探针 Statement 契约尚不存在。

- [ ] **Step 3: Implement provider database operations**

  SQL Server 使用 `sys.dm_db_partition_stats` 读取候选索引页数；MySQL 在应用账号连接内禁用
  `INFORMATION_SCHEMA` 统计缓存、执行 `ANALYZE TABLE`，再以 `TABLES.INDEX_LENGTH`
  的 candidate/baseline 差值读取体积，禁止依赖 `mysql.*` 特权系统表。三类写探针使用真实生产
  Statement，在独立事务中校验影响行数后回滚；创建/删除索引均校验最终存在状态。

- [ ] **Step 4: Run test and Release build**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsBacklogQueryBenchmarkTests"`

  Run:
  `dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release`

  Expected: 测试通过，构建 0 warning / 0 error。

### Task 4: 同容器镜像 A/B Runner

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogIndexAbBenchmarkRunner.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogQueryBenchmarkOptions.cs`
- Modify: `.agents/skills/fullnet-performance-hardening/references/performance-map.md`

**Interfaces:**
- Consumes: Tasks 1–3 的选项、采样、数据库操作和报告 writer。
- Produces: `JobsBacklogIndexAbBenchmarkRunner.RunAsync(options, cancellationToken)`。
- Produces: `jobs-backlog-query --mode index-ab` 正式与短开发命令。

- [ ] **Step 1: Write the failing entrypoint test**

  断言帮助文本登记 `index-ab`、候选索引成本和写路径门禁，且 `Program` 按 Mode 选择 A/B runner。

- [ ] **Step 2: Run test to verify it fails**

  Run: Task 1 的聚焦命令。

  Expected: FAIL，因为帮助和 A/B runner 尚未接线。

- [ ] **Step 3: Implement the mirrored runner**

  每个 Provider 启动、迁移和 seed 一次；按四块切换索引状态，每块先预热，再采样 backlog 查询和三类写探针；每个 Variant 只保存一次非空计划，candidate 额外保存创建耗时和体积。Provider 完成后立即销毁容器，再进入下一个 Provider。

- [ ] **Step 4: Run no-Docker verification**

  Run: Task 3 的测试与构建命令。

  Run:
  `dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build -- jobs-backlog-query --help`

  Expected: 测试、构建与 help 均成功。

### Task 5: 正式双库证据与迁移决策

**Files:**
- Create: `docs/verification/jobs-backlog-index-ab-2026-07-30.md`
- Modify: `docs/verification/jobs-backlog-query-evidence-2026-07-30.md`
- Modify: `docs/operations/jobs-worker-observability.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Consumes: `jobs-backlog-query --mode index-ab` 的 JSON、Markdown 和计划工件。
- Produces: 双库同环境 A/B 结果、索引成本、写路径回归、门禁结论与后续工作。

- [ ] **Step 1: Run a short single-provider smoke**

  Run:
  `dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- jobs-backlog-query --mode index-ab --rows 2000 --warmup 1 --iterations 5 --mutation-iterations 3 --providers sqlserver --output .tmp/jobs-backlog-index-ab-20260730/smoke-sqlserver`

  Expected: baseline/candidate 各 5 个正确查询样本、各 3 个写路径样本、两类计划和非零候选索引体积。

- [ ] **Step 2: Run formal providers serially**

  Run:
  `dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- jobs-backlog-query --mode index-ab --output .tmp/jobs-backlog-index-ab-20260730/formal`

  Expected: SQL Server 完成并释放后才启动 MySQL；两库各输出完整 A/B 工件，进程退出后 `docker ps` 为空。

- [ ] **Step 3: Apply the migration gate**

  如果任一 Provider 查询 P95/P99 未同时改善、正确性或计划门禁失败、任一写路径 P95 回归超过 20%，则文档结论必须是“不允许迁移”，且不得新增迁移。全部通过时也只登记“允许进入独立迁移切片”，不在本计划内修改 037 或创建 038。

- [ ] **Step 4: Run affected verification**

  Run Jobs benchmark Unit、Jobs Unit、Architecture、Naming、Governance、performance governance、test matrix contract、benchmark Release build、`git diff --check`，并运行：

  `pnpm test:integration:affected:plan -- --snapshot jobs-backlog-index-ab-20260730 --phase slice`

  只执行选择器命中的 Jobs 影响集；其它并发窗口负责的集合不重复运行。

- [ ] **Step 5: Fresh discovery and resource closure**

  在其它窗口停止写入测试方法后运行 Release discovery，按真实结果更新 `eng/testing/test-matrix.json`；不得从旧门槛手算。最终确认 benchmark 进程、Jobs 容器和 Ryuk 全部退出，并将 Docker 释放状态通知其它窗口。
