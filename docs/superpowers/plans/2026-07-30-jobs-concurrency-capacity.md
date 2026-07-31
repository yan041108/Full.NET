# Jobs Concurrency Capacity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Jobs 显式并发建立可重复的 SQL Server/MySQL 持续积压容量 A/B 入口，覆盖慢 Handler、批次续租、故障隔离、多副本、连接池与数据库资源证据，但不自动修改生产默认并发。

**Architecture:** 新增独立 `jobs-capacity` 命令，复用 `MixedLoadDatabase` 的正式 DbUp/Testcontainers、Dapper/连接池/容器资源采集能力，并直接运行生产 `JobExecutionRunner`。每个场景重建 Jobs 数据集，按固定 JobKey 分组和固定失败键运行预热与稳态窗口；停止领取后等待当前批次排空，再以数据库终态、Handler 观测和低基数遥测共同判定正确性。完整矩阵仅由手动 CI 在同一构建中串行执行双 Provider，本地只运行每库一个缩小 smoke。

**Tech Stack:** .NET 10、Dapper、SQL Server 2022、MySQL 8.0、Testcontainers、Microsoft.Extensions.DependencyInjection、System.Text.Json、GitHub Actions。

**Status:** 本地实现与双库短 smoke 已完成；完整容量矩阵保留给手工 CI，未执行。

## Global Constraints

- 任务基线为 `975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`，任务快照为 `jobs-concurrency-capacity-20260730`。
- 只修改 Jobs benchmark、Jobs benchmark Unit、独立手动性能工作流、性能地图和本切片计划/验证文档。
- 不修改生产 `JobExecutionRunner`、`JobsWorkerOptions`、SQL、迁移、默认 `MaxConcurrency = 1`、共享测试矩阵或其它模块。
- 正式场景固定使用 SQL Server 与 MySQL；任一 Provider 的正确性、租约、连接池或数据库错误门禁失败，都必须保持默认并发为 `1`。
- Handler 顺序键继续是 `JobKey`；相同 JobKey 串行、不同 JobKey 才能并行，每条并发执行继续使用独立 Scope。
- 指标和报告不得包含原始 SQL、异常消息、执行 ID、租户、用户或实际数据库连接字符串。
- 本地只允许 `repetitions=1`、短预热/稳态和单 Provider smoke；完整 `1/2/4/8`、慢 Handler、多副本矩阵只能由手动 CI 运行。

---

### Task 1: CLI options and bounded scenario catalog

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityOptions.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityScenario.cs`
- Create: `tests/Full.NET.UnitTests/Jobs/JobsCapacityBenchmarkTests.cs`

**Interfaces:**
- Produces: `JobsCapacityOptions.Parse(IReadOnlyList<string>)`
- Produces: `JobsCapacityScenario(int Concurrency, int HandlerDelayMilliseconds, int Replicas)`
- Produces: `JobsCapacityScenarioCatalog.Build(JobsCapacityOptions)`

- [ ] **Step 1: Write failing option and catalog tests**

```csharp
[Test]
public void Parse_Defaults_create_bounded_manual_ci_matrix()
{
    var options = JobsCapacityOptions.Parse([]);

    Assert.SequenceEqual(["sqlserver", "mysql"], options.Providers);
    Assert.SequenceEqual([1, 2, 4, 8], options.ConcurrencyLevels);
    Assert.SequenceEqual([0, 1000], options.HandlerDelayMilliseconds);
    Assert.SequenceEqual([1, 2], options.ReplicaCounts);
    Assert.AreEqual(3, options.Repetitions);
    Assert.AreEqual(16, options.BatchSize);
    Assert.AreEqual(8, options.HandlerKeyCount);
    Assert.AreEqual(1, options.FailingHandlerKeyCount);
    Assert.AreEqual(TimeSpan.FromSeconds(30), options.Lease);
    Assert.AreEqual(TimeSpan.FromSeconds(5), options.LeaseRenewal);
}

[Test]
public void Catalog_builds_single_replica_ab_and_one_slow_replica_shape()
{
    var options = JobsCapacityOptions.Parse([]);

    var scenarios = JobsCapacityScenarioCatalog.Build(options);

    Assert.HasCount(9, scenarios);
    Assert.IsTrue(scenarios.Contains(new JobsCapacityScenario(2, 1000, 2)));
    Assert.IsFalse(scenarios.Contains(new JobsCapacityScenario(8, 0, 2)));
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsCapacityBenchmarkTests"
```

Expected: compile failure because `JobsCapacityOptions` and `JobsCapacityScenarioCatalog` do not exist.

- [ ] **Step 3: Implement bounded parsing and catalog**

```csharp
public sealed record JobsCapacityOptions(
    IReadOnlyList<string> Providers,
    IReadOnlyList<int> ConcurrencyLevels,
    IReadOnlyList<int> HandlerDelayMilliseconds,
    IReadOnlyList<int> ReplicaCounts,
    int Repetitions,
    TimeSpan Warmup,
    TimeSpan Duration,
    int SeedJobs,
    int BatchSize,
    int HandlerKeyCount,
    int FailingHandlerKeyCount,
    TimeSpan Lease,
    TimeSpan LeaseRenewal,
    bool ResumeEnabled,
    int MaximumNewSamples,
    string OutputDirectory);

public sealed record JobsCapacityScenario(
    int Concurrency,
    int HandlerDelayMilliseconds,
    int Replicas)
{
    public string Name =>
        $"c{Concurrency}-d{HandlerDelayMilliseconds}-r{Replicas}";
}
```

`Parse` must accept only these options:

```text
--providers
--concurrency
--handler-delay-ms
--replicas
--repetitions
--warmup-seconds
--duration-seconds
--seed-jobs
--batch-size
--handler-keys
--failing-handler-keys
--lease-seconds
--lease-renewal-seconds
--resume
--max-new-samples
--output
```

Enforce `Concurrency <= BatchSize <= 50`,
`FailingHandlerKeyCount < HandlerKeyCount <= BatchSize`,
`SeedJobs >= BatchSize * Replicas * 2`, and
`LeaseRenewal <= Lease / 2`.

`JobsCapacityScenarioCatalog.Build` must create:

```csharp
foreach (var concurrency in options.ConcurrencyLevels)
foreach (var delay in options.HandlerDelayMilliseconds)
    scenarios.Add(new(concurrency, delay, Replicas: 1));

var replicaConcurrency = options.ConcurrencyLevels
    .FirstOrDefault(value => value >= 2, options.ConcurrencyLevels[0]);
var slowestDelay = options.HandlerDelayMilliseconds.Max();
foreach (var replicas in options.ReplicaCounts)
    scenarios.Add(new(replicaConcurrency, slowestDelay, replicas));
```

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the command from Step 2.

Expected: all `JobsCapacityBenchmarkTests` pass.

---

### Task 2: Sustained backlog planner and latency statistics

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityBacklogPlanner.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityStatistics.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsCapacityBenchmarkTests.cs`

**Interfaces:**
- Produces: `JobsCapacityBacklogPlanner.CalculateRequiredJobs(...)`
- Produces: `JobsCapacityStatistics.Calculate(IReadOnlyCollection<double>)`

- [ ] **Step 1: Write failing planner/statistics tests**

```csharp
[Test]
public void Backlog_planner_keeps_warmup_rate_sustained_with_safety_margin()
{
    var required = JobsCapacityBacklogPlanner.CalculateRequiredJobs(
        configuredMinimum: 64,
        completedDuringWarmup: 40,
        warmup: TimeSpan.FromSeconds(2),
        duration: TimeSpan.FromSeconds(10),
        batchSize: 16,
        replicas: 2);

    Assert.AreEqual(364, required);
}

[Test]
public void Statistics_use_nearest_rank_for_tail_latency()
{
    var statistics = JobsCapacityStatistics.Calculate(
        Enumerable.Range(1, 100).Select(value => (double)value).ToArray());

    Assert.AreEqual(50d, statistics.P50Milliseconds);
    Assert.AreEqual(95d, statistics.P95Milliseconds);
    Assert.AreEqual(99d, statistics.P99Milliseconds);
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Expected: compile failure because planner/statistics types do not exist.

- [ ] **Step 3: Implement deterministic calculations**

```csharp
public static int CalculateRequiredJobs(
    int configuredMinimum,
    long completedDuringWarmup,
    TimeSpan warmup,
    TimeSpan duration,
    int batchSize,
    int replicas)
{
    var measuredRate = warmup > TimeSpan.Zero
        ? completedDuringWarmup / warmup.TotalSeconds
        : 0d;
    var measuredNeed = (int)Math.Ceiling(
        measuredRate * duration.TotalSeconds * 1.5d);
    var drainReserve = checked(batchSize * replicas * 2);
    return Math.Max(configuredMinimum, checked(measuredNeed + drainReserve));
}
```

Reject non-positive durations, batch/replica values and results above
`1_000_000` instead of silently overflowing.

- [ ] **Step 4: Run focused tests and verify GREEN**

Expected: all focused tests pass.

---

### Task 3: Real Jobs database fixture and scoped handlers

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityDatabase.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityRuntime.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsCapacityBenchmarkTests.cs`

**Interfaces:**
- Consumes: `MixedLoadDatabase.ConnectionString`, `MixedLoadDatabase.Provider`
- Produces: `JobsCapacityDatabase.ResetAndSeedAsync(...)`
- Produces: `JobsCapacityDatabase.ReadStateAsync(...)`
- Produces: `JobsCapacityRuntime.BuildServices(...)`
- Produces: `JobsCapacityRuntime.RunUntilStoppedAsync(...)`
- Produces: `JobsCapacityProbe.Snapshot()`

- [ ] **Step 1: Write failing probe and failure-classification tests**

```csharp
[Test]
public async Task Probe_records_fixed_key_failures_without_high_cardinality_data()
{
    var probe = new JobsCapacityProbe();
    var handler = new JobsCapacityHandler(
        "jobs.benchmark.capacity.failure.0",
        TimeSpan.Zero,
        fails: true,
        Guid.CreateVersion7(),
        probe);

    await Assert.ThrowsAsync<JobsCapacityExpectedFailureException>(
        () => handler.ExecuteAsync(CancellationToken.None));

    var snapshot = probe.Snapshot();
    Assert.AreEqual(1, snapshot.Invocations);
    Assert.AreEqual(1, snapshot.ExpectedFailures);
    Assert.HasCount(1, snapshot.ScopeIds);
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Expected: compile failure because the runtime/probe types do not exist.

- [ ] **Step 3: Implement fixture and scoped runtime**

`JobsCapacityDatabase` must:

```csharp
public Task ResetAndSeedAsync(
    int jobCount,
    int handlerKeyCount,
    int failingHandlerKeyCount,
    DateTimeOffset createdAtUtc,
    CancellationToken cancellationToken);

public Task<JobsCapacityDatabaseState> ReadStateAsync(
    DateTimeOffset createdAtUtc,
    CancellationToken cancellationToken);
```

Use parameterized Dapper and fixed benchmark-only Statement names. Seed
`handlerKeyCount` Host definitions and round-robin pending executions. SQL
Server and MySQL must use the same logical fields; provider-specific queue
latency SQL may use `DATEDIFF_BIG` versus `TIMESTAMPDIFF`.

`JobsCapacityRuntime.BuildServices` must register:

```csharp
services.AddScoped<CurrentTenantAccessor>();
services.AddScoped<ICurrentTenant>(provider =>
    provider.GetRequiredService<CurrentTenantAccessor>());
services.AddFullNetDapper(configuration, "Benchmark");
new JobsModule().AddBackgroundServices(services, configuration);
services.AddScoped<JobsCapacityScopeIdentity>();
```

Register one scoped `IJobHandler` factory per stable key. Failure keys throw
`JobsCapacityExpectedFailureException` after the configured delay; healthy
keys return normally. `RunUntilStoppedAsync` creates one runner scope per
replica, sets Host context, repeatedly calls production
`JobExecutionRunner.ProcessPendingAsync(BatchSize)`, stops starting new
batches when requested, and allows the in-flight batch to finish.

- [ ] **Step 4: Run focused tests and verify GREEN**

Expected: all focused tests pass without starting Docker.

---

### Task 4: Run result, correctness gate and concurrency assessment

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityResult.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityAssessment.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsCapacityBenchmarkTests.cs`

**Interfaces:**
- Produces: `JobsCapacityRunResult.CorrectnessGatePassed`
- Produces: `JobsCapacityAssessment.Assess(...)`
- Produces: `JobsCapacityRecommendation.KeepConcurrencyOne|EligibleForCanaryAtTwo`

- [ ] **Step 1: Write failing correctness and recommendation tests**

```csharp
[Test]
public void Assessment_requires_both_providers_and_all_c2_safety_gates()
{
    var runs = JobsCapacityResultFixtures.PassingDualProviderRuns();

    var assessment = JobsCapacityAssessment.Assess(runs);

    Assert.AreEqual(
        JobsCapacityRecommendation.EligibleForCanaryAtTwo,
        assessment.Recommendation);
}

[Test]
public void Assessment_keeps_one_when_mysql_has_database_failure()
{
    var runs = JobsCapacityResultFixtures.PassingDualProviderRuns()
        .Select(run => run.Provider == "mysql" && run.Scenario.Concurrency == 2
            ? run with { Dapper = run.Dapper with { Failures = 1 } }
            : run)
        .ToArray();

    var assessment = JobsCapacityAssessment.Assess(runs);

    Assert.AreEqual(
        JobsCapacityRecommendation.KeepConcurrencyOne,
        assessment.Recommendation);
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Expected: compile failure because result and assessment types do not exist.

- [ ] **Step 3: Implement strict gates**

Each run passes correctness only when:

```csharp
TerminalExecutions == Handler.Invocations
&& FailedExecutions == Handler.ExpectedFailures
&& AttemptCountGreaterThanOne == 0
&& RunningExecutions == 0
&& TerminalExecutionsWithLease == 0
&& PendingExecutions > 0
&& Dapper.Failures == 0
&& Dapper.Cancellations == 0
&& UnexpectedProcessorErrors == 0
&& ConnectionPool.CapacityHeadroomPassed
&& ConnectionPool.EvidenceComplete
&& DatabaseContainer.EvidenceComplete
```

For each Provider and each single-replica delay, compare median `c2` to
median `c1`. `EligibleForCanaryAtTwo` requires:

```text
c2 correctness for every repetition
c2 median terminals/second >= c1 median * 1.20
c2 median queue P95 <= c1 median queue P95
zero database failures/deadlocks/duplicate attempts
slow c1 and c2 each contain at least one lease-renew statement
the two-replica slow c2 shape passes correctness
both SQL Server and MySQL satisfy every gate
```

The assessment is evidence only. It must never edit configuration or return a
recommendation above `EligibleForCanaryAtTwo`.

- [ ] **Step 4: Run focused tests and verify GREEN**

Expected: all focused tests pass.

---

### Task 5: Checkpointed runner, atomic report and CLI dispatch

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityCheckpoint.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityReportWriter.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Jobs/JobsCapacityRunner.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsCapacityBenchmarkTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4
- Produces: `jobs-capacity` CLI
- Produces: `report.json` and `summary.md`

- [ ] **Step 1: Write failing checkpoint/report tests**

```csharp
[Test]
public async Task Checkpoint_rejects_a_different_build_fingerprint()
{
    var report = JobsCapacityResultFixtures.Report(
        buildFingerprint: "old-build");
    await JobsCapacityResultFixtures.WriteReportAsync(report);

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => JobsCapacityCheckpoint.LoadAsync(
            report.Options,
            report.Scenarios,
            buildFingerprint: "new-build",
            CancellationToken.None));

    StringAssert.Contains("构建指纹", exception.Message);
}

[Test]
public async Task Report_writer_replaces_json_and_markdown_atomically()
{
    var report = JobsCapacityResultFixtures.Report(
        buildFingerprint: "same-build");

    await JobsCapacityReportWriter.WriteAsync(
        report.Options,
        report.Scenarios,
        report.Providers,
        CancellationToken.None);

    Assert.IsTrue(File.Exists(Path.Combine(
        report.Options.OutputDirectory,
        "report.json")));
    Assert.IsTrue(File.Exists(Path.Combine(
        report.Options.OutputDirectory,
        "summary.md")));
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Expected: compile failure because checkpoint/writer/runner do not exist.

- [ ] **Step 3: Implement sequential Provider runner**

For each Provider:

1. start one `MixedLoadDatabase`;
2. for every scenario/repetition not present in checkpoint:
3. reset/seed warmup data and run warmup;
4. calculate adaptive required jobs;
5. reset/seed measurement data;
6. reset Dapper/pool telemetry and capture database/process/container before;
7. run replicas for `Duration`, stop new batches and drain in-flight batches;
8. capture state and telemetry;
9. atomically write checkpoint;
10. dispose the Provider container before starting the next Provider.

The report must store:

```text
source version
SHA-256 executing-assembly build fingerprint
runtime/OS/CPU
container image/database version
scenario/repetition
actual duration and drain duration
terminal executions/second
Handler P50/P95/P99
queue P50/P95/P99
expected failures and unexpected processor errors
attempt/lease/running/pending correctness
lease renewal statement count
Dapper statement/failure reason summaries
connection pool, database lock/log and container/process resources
assessment and explicit default-concurrency conclusion
```

Dispatch in `Program.cs`:

```csharp
else if (args.FirstOrDefault() is "jobs-capacity")
{
    var capacityArguments = args.Skip(1).ToArray();
    if (capacityArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(JobsCapacityOptions.HelpText);
        return;
    }

    await JobsCapacityRunner.RunAsync(
        JobsCapacityOptions.Parse(capacityArguments));
}
```

- [ ] **Step 4: Run focused tests and benchmark build**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~JobsCapacityBenchmarkTests"
dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-restore
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build -- jobs-capacity --help
```

Expected: all tests pass; build is 0 warning/0 error; help exits 0.

---

### Task 6: Manual CI, dual-provider smoke and documentation

**Files:**
- Create: `.github/workflows/jobs-capacity.yml`
- Modify: `.agents/skills/fullnet-performance-hardening/references/performance-map.md`
- Modify: `docs/verification/jobs-bounded-concurrency-2026-07-29.md`
- Create: `docs/verification/jobs-concurrency-capacity-2026-07-30.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Consumes: `jobs-capacity` CLI
- Produces: one dual-provider manual CI artifact with a cross-provider assessment

- [x] **Step 1: Add a manual-only dual-provider run**

```yaml
name: Jobs capacity

on:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  capacity:
    runs-on: ubuntu-latest
    timeout-minutes: 90
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet restore benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj
      - run: dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-restore
      - run: >-
          dotnet run
          --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj
          -c Release --no-build --
          jobs-capacity
          --output BenchmarkDotNet.Artifacts/jobs-capacity
      - if: always()
        uses: actions/upload-artifact@v4
        with:
          name: jobs-capacity
          path: BenchmarkDotNet.Artifacts/jobs-capacity/**
          if-no-files-found: error
```

- [x] **Step 2: Run one local smoke per Provider**

Run serially after Docker is explicitly released:

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build -- jobs-capacity --providers sqlserver --concurrency 1,2 --handler-delay-ms 50 --replicas 1 --repetitions 1 --warmup-seconds 1 --duration-seconds 2 --seed-jobs 64 --batch-size 8 --handler-keys 4 --failing-handler-keys 1 --output .tmp/jobs-concurrency-capacity-20260730/smoke-sqlserver
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build -- jobs-capacity --providers mysql --concurrency 1,2 --handler-delay-ms 50 --replicas 1 --repetitions 1 --warmup-seconds 1 --duration-seconds 2 --seed-jobs 64 --batch-size 8 --handler-keys 4 --failing-handler-keys 1 --output .tmp/jobs-concurrency-capacity-20260730/smoke-mysql
```

Expected: both reports are complete; every run passes correctness; containers
and Ryuk exit after each Provider. Smoke is correctness evidence only and must
not decide the production default.

- [x] **Step 3: Update performance map and verification**

Document the manual CI entry, exact matrix, local smoke boundary, metric
definitions, artifact paths and assessment rules. Until complete manual CI
artifacts from the same build show both Providers eligible, the verification
conclusion must remain:

```text
MaxConcurrency default remains 1; capacity benefit is not yet verified.
```

- [x] **Step 4: Run final repository gates**

Run:

```powershell
pnpm test:performance-governance
pnpm test:governance
pnpm test:naming
pnpm test:skills
pnpm test:integration:affected:plan -- --snapshot jobs-concurrency-capacity-20260730 --phase slice
git diff --check
git status --short --branch
```

Run only the affected targets selected for this Jobs slice. Do not duplicate
other windows' CodeGeneration, Files or Realtime container runs. Fresh test
discovery and `eng/testing/test-matrix.json` updates are deferred until every
window has frozen its test methods.

## Self-review

- Spec coverage: Task 26 Step 3 的 `1/2/4/8`、慢 Handler、持续积压、多副本、
  续租、故障隔离、连接池和双库门禁均映射到 Tasks 1–6。
- Scope: no production Jobs, migration, SQL or default configuration change.
- Local/CI boundary: local only two focused smoke commands; full matrix only
  the manual workflow.
- Type consistency: options, scenario, runtime, result, checkpoint, report and
  assessment names are identical across tasks.
- Placeholder scan: no unresolved placeholder or migration number remains.
