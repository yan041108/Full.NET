# Jobs Backlog Query Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `fullnet-performance-hardening` and `superpowers:test-driven-development` while executing this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为生产 Jobs backlog 聚合 SQL 建立可重复的 SQL Server/MySQL 代表性规模执行计划、正确性与尾延迟证据入口。

**Architecture:** 在现有 `Full.NET.Benchmarks` 可执行项目中新增 `jobs-backlog-query` 命令，直接消费
`JobSql.ReadBacklogSqlServer`/`ReadBacklogMySql`，避免 benchmark SQL 漂移。每个 Provider 使用独立
Testcontainers 数据库和正式迁移，写入确定性状态分布，完成预热、顺序采样、结果门禁与执行计划保存后
生成 JSON/Markdown 工件；不修改生产查询、索引、Worker 配置或并发默认值。

**Tech Stack:** .NET 10、Dapper、SQL Server 2022、MySQL 8.0、DbUp、Testcontainers、MSTest。

## Global Constraints

- 默认数据规模 100000 行，必须为 20 的倍数；最小 1000，最大 1000000。
- 默认单并发、5 次预热、30 次采样；本入口只模拟每个 Worker 采样点的一次顺序聚合查询。
- 数据分布固定为 50% Host pending，其中 15% 为已到期重试、10% 为未来重试；20% 为租户 pending
  噪声，其余为 Host running/succeeded/failed。
- 生产 SQL 必须通过 `JobSql` 直接消费；禁止复制后独立演进。
- SQL Server 保存实际 `STATISTICS XML`；MySQL 同时保存 `EXPLAIN FORMAT=JSON` 与
  `EXPLAIN ANALYZE`。
- 工件保存在 `.tmp`/`BenchmarkDotNet.Artifacts`，不提交原始计划；Verification 只摘录稳定事实。
- 不改变 037 索引、Jobs Worker 运行语义、默认并发或共享测试矩阵。

---

### Task 1: 冻结命令行、数据分布与生产 SQL 同源契约

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogQueryBenchmarkOptions.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogDataset.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogQueryStatistics.cs`
- Create: `tests/Full.NET.UnitTests/Jobs/JobsBacklogQueryBenchmarkTests.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Properties/AssemblyInfo.cs`

**Interfaces:**
- Produces: `JobsBacklogQueryBenchmarkOptions.Parse(IReadOnlyList<string>)`
- Produces: `JobsBacklogDataset.CreateRow(int, int, DateTimeOffset)` 与
  `JobsBacklogDataset.CreateExpectation(int, DateTimeOffset)`
- Produces: `JobsBacklogQueryStatistics.Calculate(IReadOnlyCollection<TimeSpan>)`
- Consumes: `JobSql.ReadBacklogSqlServer.Text` 与 `JobSql.ReadBacklogMySql.Text`

- [ ] **Step 1: 写入 Options、分布、百分位与 SQL 同源 RED**

  新增五个测试方法，分别断言默认值、边界拒绝、100000 行固定分布、nearest-rank P50/P95/P99，
  以及 benchmark 直接返回两个生产 `JobSql.Text`。

- [ ] **Step 2: 运行 RED**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsBacklogQueryBenchmarkTests"`

  Expected: 因 `JobsBacklogQueryBenchmarkOptions`、`JobsBacklogDataset` 与统计类型不存在而编译失败。

- [ ] **Step 3: 实现最小纯函数契约**

  Options 接受 `--rows`、`--warmup`、`--iterations`、`--providers`、`--reference-utc` 与
  `--output`；拒绝未知、重复 Provider、越界和非 20 倍数。数据集使用 `index % 20` 固定状态，
  并计算 `PendingCount = rows / 2`、`DueRetryCount = rows * 3 / 20`。

- [ ] **Step 4: 运行 GREEN**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsBacklogQueryBenchmarkTests"`

  Expected: 5/5 通过。

### Task 2: 建立双库迁移、批量播种、查询与实际计划捕获

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogBenchmarkDatabase.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsBacklogQueryBenchmarkTests.cs`

**Interfaces:**
- Produces: `JobsBacklogBenchmarkDatabase.StartAsync(string, CancellationToken)`
- Produces: `SeedAsync(JobsBacklogQueryBenchmarkOptions, CancellationToken)`
- Produces: `ExecuteAsync(DateTimeOffset, CancellationToken)`
- Produces: `CapturePlansAsync(DateTimeOffset, CancellationToken)`
- Produces: `GetVersionAsync(CancellationToken)`

- [ ] **Step 1: 写入结果门禁 RED**

  扩展纯函数测试，断言 `JobsBacklogQueryResult.Matches(expectation)` 只有 pending、due 和两个时间
  边界全部吻合时为真。

- [ ] **Step 2: 实现数据库夹具**

  SQL Server 使用 `SqlBulkCopy`，MySQL 使用 `MySqlBulkCopy`；两者先执行正式 DbUp 迁移。执行路径
  直接使用生产 Statement 文本与 `ObservedAtUtc`/`PendingStatus = "pending"` 参数。SQL Server
  捕获包含运行时计数的 Showplan XML；MySQL 捕获 JSON 估算计划和实际 ANALYZE 文本。

- [ ] **Step 3: 运行纯函数 GREEN 与 Release build**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsBacklogQueryBenchmarkTests"`

  Run:
  `dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release`

  Expected: 测试全部通过，构建 0 警告、0 错误；此阶段不启动 Docker。

### Task 3: 建立 runner、工件与命令入口

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogQueryReportWriter.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsBacklogQueryBenchmarkRunner.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsBacklogQueryBenchmarkTests.cs`
- Modify: `.agents/skills/fullnet-performance-hardening/references/performance-map.md`

**Interfaces:**
- Produces: `JobsBacklogQueryBenchmarkRunner.RunAsync(options, cancellationToken)`
- Produces: `summary.json`、`README.md` 与 Provider 计划文件

- [ ] **Step 1: 写入报告 RED**

  用临时目录断言报告包含环境、数据分布、P50/P95/P99、正确性门禁、Provider 版本与相对计划路径。

- [ ] **Step 2: 实现 runner 与报告**

  每个 Provider 串行执行迁移、播种、预热、采样和一次计划捕获；任一采样不匹配期望或计划为空即失败。
  报告明确声明本机容器结果不是生产 SLA，Provider 间绝对耗时不可横向排名。

- [ ] **Step 3: 接入命令并运行 GREEN**

  `Program.cs` 新增 `jobs-backlog-query` 分支及 `--help`；性能地图登记正式命令与短开发命令。

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsBacklogQueryBenchmarkTests"`

  Expected: 报告测试和全部契约测试通过。

### Task 4: 双库代表性规模实测与收口

**Files:**
- Create: `docs/verification/jobs-backlog-query-evidence-2026-07-30.md`
- Modify: `docs/verification/jobs-backlog-telemetry-2026-07-30.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Consumes: `jobs-backlog-query` 工件
- Produces: 双库环境、数据分布、P50/P95/P99、计划索引/扫描事实与未验证项

- [ ] **Step 1: Realtime 释放 Docker 后运行双库正式入口**

  Run:
  `dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- jobs-backlog-query --output .tmp/jobs-backlog-query-evidence-20260730/formal`

  Expected: SQL Server/MySQL 各产生 30 个有效样本、正确性门禁通过且计划文件非空。

- [ ] **Step 2: 检查计划与成本事实**

  摘录 SQL Server 实际逻辑读/实际读行/使用索引和 MySQL access type/key/估算行/ANALYZE 实际行。
  如果任一 Provider 出现无界异常扫描或成本不可接受，只登记证据与后续索引 A/B，不在本切片修改 037。

- [ ] **Step 3: 完成受影响验证**

  运行 Jobs benchmark Unit、benchmark Release build、Jobs Unit、Architecture、Naming、Governance、
  affected inner plan、`git diff --check`、矩阵即时值和 `docker ps`；不运行完整 Integration。

- [ ] **Step 4: 更新验证与路线图**

  只把“代表性规模双库执行计划/成本证据”从缺口移除；生产指标导出、告警阈值及索引 A/B 是否仍需
  后续，由实际计划事实决定。
