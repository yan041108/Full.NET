# Jobs Retry Backoff and Jitter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变现有部署行为的前提下，为显式可重试的 Jobs 失败增加有界指数退避与可选对称抖动，降低多 Worker 同时重试造成的热点。

**Architecture:** 在 Jobs `Execution` 边界新增纯延迟计算器和低成本随机源；`JobExecutionRunner` 仍只负责把计算后的 `NextAttemptAtUtc` 交给既有 `jobs.reschedule_host_execution` Statement。配置默认继续使用固定 30 秒且不抖动；只有显式选择 `exponential` 才按当前一基尝试次数增长，并始终受最大延迟约束。

**Tech Stack:** .NET 10、Microsoft Options、Dapper/现有 Jobs SQL、MSTest、Microsoft Testing Platform。

## Global Constraints

- 基线固定为 `975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`；`jobs-retry-backoff-20260730` 仅保留计划阶段审计。前置队列全部冻结后、落 RED 前必须新建 `jobs-retry-backoff-implementation-20260730`，所有 affected 命令只使用这个 fresh implementation snapshot。
- 只修改 Jobs 重试配置、Runner、Jobs Unit/既有双库重试断言、Worker 示例配置和独立计划/验证文档。
- 不修改数据库结构、037/038/后续迁移、SQL Statement、公共 HTTP/JSON 契约、Files、Realtime、Settings、CodeGeneration、全局 SQL catalog 或共享测试矩阵。
- `MaxAttempts = 1`、`RetryDelaySeconds = 30`、`RetryBackoffMode = "fixed"`、`RetryMaxDelaySeconds = 86400`、`RetryJitterPercent = 0` 是兼容默认值。
- `RetryBackoffMode` 只接受稳定小写机器值 `fixed` 与 `exponential`；未知值必须在 Worker 启动期失败。
- `RetryMaxDelaySeconds` 范围为 `1..86400` 且不得小于 `RetryDelaySeconds`；`RetryJitterPercent` 范围为 `0..50`。
- 指数退避使用当前一基 `AttemptCount`：第 1 次失败使用基础延迟，第 2 次失败使用 2 倍，之后继续翻倍并在乘法前封顶，禁止整数溢出。
- 抖动在 `[-RetryJitterPercent, +RetryJitterPercent]` 内对称分布，最终秒数按 `AwayFromZero` 取整并约束到 `1..RetryMaxDelaySeconds`。
- 普通异常、缺失 Handler、宿主取消、租约丢失、尝试耗尽、租户与 Host Context 语义全部保持不变。
- 当前 Files → Realtime → Admin Task 3 → CodeGeneration 模板 Task 1 队列释放前，只允许计划与只读检查；不得落 compile-breaking RED、启动 .NET build 或 Docker。
- 本共享工作区禁止提交、暂存、清理或覆盖其它窗口变更。

---

### Task 0: Establish a fresh implementation boundary

**Files:**
- No source changes

**Interfaces:**
- Consumes: explicit release from Realtime, Admin Task 3, and CodeGeneration template Task 1
- Produces: `jobs-retry-backoff-implementation-20260730`

- [ ] **Step 1: Confirm the queue is fully released**

Require explicit evidence that Admin Task 3 and CodeGeneration template Task 1
have completed their builds/affected tests, Testcontainers and Ryuk have
exited, and Docker running/residual counts are zero.

- [ ] **Step 2: Create the implementation snapshot immediately before RED**

Run:

```powershell
pnpm test:task:start -- jobs-retry-backoff-implementation-20260730
```

Expected: snapshot creation succeeds. Do not reuse the earlier planning
snapshot because it predates Files, Realtime, Settings, and CodeGeneration
changes from the shared queue.

### Task 1: Freeze bounded retry configuration

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobsWorkerOptions.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/appsettings.json`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsWorkerOptionsTests.cs`

**Interfaces:**
- Produces: `JobsWorkerOptions.RetryBackoffMode`
- Produces: `JobsWorkerOptions.RetryMaxDelaySeconds`
- Produces: `JobsWorkerOptions.RetryJitterPercent`
- Consumes: `JobsWorkerOptionsValidator.Validate(...)`

- [ ] **Step 1: Extend the existing options test with compatibility defaults and invalid bounds**

Add these default assertions to
`AddBackgroundServices_BindsDefaultsAndRejectsUnsafeBounds`:

```csharp
Assert.AreEqual("fixed", defaultOptions.RetryBackoffMode);
Assert.AreEqual(86400, defaultOptions.RetryMaxDelaySeconds);
Assert.AreEqual(0, defaultOptions.RetryJitterPercent);
```

Add invalid configuration values and assert the exact failures:

```csharp
["Jobs:Worker:RetryBackoffMode"] = "random",
["Jobs:Worker:RetryMaxDelaySeconds"] = "0",
["Jobs:Worker:RetryJitterPercent"] = "51",
```

```text
Jobs:Worker:RetryBackoffMode must be 'fixed' or 'exponential'.
Jobs:Worker:RetryMaxDelaySeconds must be between 1 and 86400.
Jobs:Worker:RetryJitterPercent must be between 0 and 50.
```

Add a separate invalid provider with
`RetryDelaySeconds=60` and `RetryMaxDelaySeconds=30`, then assert:

```text
Jobs:Worker:RetryMaxDelaySeconds must not be less than RetryDelaySeconds.
```

- [ ] **Step 2: Run the focused options test and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~JobsWorkerOptionsTests"
```

Expected: compile failure because the three new option properties do not exist.

- [ ] **Step 3: Add the properties and startup validation**

Add to `JobsWorkerOptions`:

```csharp
public string RetryBackoffMode { get; set; } = "fixed";

public int RetryMaxDelaySeconds { get; set; } = 86400;

public int RetryJitterPercent { get; set; }
```

Use Chinese XML comments to explain compatibility defaults and the maximum
delay invariant. Extend `JobsWorkerOptionsValidator` with exact ordinal checks
for `fixed`/`exponential`, the numeric bounds above, and the cross-property
maximum-delay check.

- [ ] **Step 4: Add explicit safe defaults to Worker appsettings**

Add under `Jobs:Worker`:

```json
"RetryBackoffMode": "fixed",
"RetryMaxDelaySeconds": 86400,
"RetryJitterPercent": 0
```

- [ ] **Step 5: Re-run the focused options test**

Expected: all `JobsWorkerOptionsTests` pass with zero warning and zero skipped
test.

### Task 2: Implement the pure bounded delay policy

**Files:**
- Create: `src/Modules/Full.NET.Modules.Jobs/Execution/JobsRetryDelayCalculator.cs`
- Create: `tests/Full.NET.UnitTests/Jobs/JobsRetryDelayCalculatorTests.cs`

**Interfaces:**
- Produces: `JobsRetryDelayCalculator.CalculateSeconds(JobsWorkerOptions, int, double)`
- Produces: `IJobsRetryJitterSource.NextUnitInterval()`
- Produces: `SystemJobsRetryJitterSource`

- [ ] **Step 1: Write three focused calculator tests**

Create these test methods:

```csharp
[TestMethod]
public void CalculateSeconds_FixedModePreservesConfiguredDelay()
```

Assert attempts 1 and 9 both return `30` when mode is `fixed`, maximum is
`86400`, jitter is `0`, and the supplied unit sample differs.

```csharp
[TestMethod]
public void CalculateSeconds_ExponentialModeGrowsAndCapsWithoutOverflow()
```

Use base `30`, maximum `100`, jitter `0`; assert attempts 1/2/3/10 return
`30/60/100/100`.

```csharp
[TestMethod]
public void CalculateSeconds_JitterIsSymmetricAndRemainsBounded()
```

Use fixed base `100`, maximum `110`, jitter `20`; assert unit samples
`0.0/0.5/1.0` return `80/100/110`. Also use base/max `1` with sample `0.0`
and assert the result remains `1`.

- [ ] **Step 2: Run the calculator tests and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~JobsRetryDelayCalculatorTests"
```

Expected: compile failure because the calculator and jitter source do not
exist.

- [ ] **Step 3: Implement the minimal pure policy**

`CalculateSeconds` must:

1. start with `RetryDelaySeconds`;
2. for `exponential`, double at most `AttemptCount - 1` times and stop as soon
   as the configured maximum is reached;
3. map a unit sample to `[-1, +1]`;
4. apply the configured percent;
5. round with `MidpointRounding.AwayFromZero`;
6. clamp the final value to `1..RetryMaxDelaySeconds`.

`SystemJobsRetryJitterSource.NextUnitInterval()` delegates to
`Random.Shared.NextDouble()`. It contains no state, IDs, tenant data, SQL, or
labels.

- [ ] **Step 4: Re-run the calculator tests**

Expected: 3/3 pass.

### Task 3: Wire the policy into retry rescheduling

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/JobsModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobExecutionRunnerTests.cs`

**Interfaces:**
- Consumes: `IJobsRetryJitterSource`
- Consumes: `JobsRetryDelayCalculator.CalculateSeconds(...)`
- Preserves: `JobSql.RescheduleExecution`

- [ ] **Step 1: Add a Runner hookup RED test**

Add:

```csharp
[TestMethod]
public async Task ProcessPendingAsync_WhenExponentialRetryIsConfigured_UsesAttemptBasedDelay()
```

Create a retryable execution with `AttemptCount = 3`, base delay `30`,
maximum `1000`, mode `exponential`, jitter `20`, and inject a fixed jitter
source that returns `1.0`; assert the `NextAttemptAtUtc` command parameter
equals `now + 144 seconds`.

Extend `CreateFailureRunner` to accept an optional fully constructed
`JobsWorkerOptions` and optional `IJobsRetryJitterSource`; keep existing
callers on their current fixed-delay values.

- [ ] **Step 2: Run the focused Runner test and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProcessPendingAsync_WhenExponentialRetryIsConfigured"
```

Expected: assertion failure because Runner still adds only
`RetryDelaySeconds`.

- [ ] **Step 3: Register the jitter source and calculate the due time**

In `JobsModule.RegisterExecutionCore` register:

```csharp
services.TryAddSingleton<
    IJobsRetryJitterSource,
    SystemJobsRetryJitterSource>();
```

Add `IJobsRetryJitterSource? retryJitterSource = null` as the final optional
Runner constructor parameter, after the existing optional scope factory, so
the ten direct test call sites keep their current signatures. Resolve the
registered service through DI in production and fall back to the stateless
system source only for direct construction. In the retryable-failure branch,
replace the fixed addition with:

```csharp
var retryDelaySeconds = JobsRetryDelayCalculator.CalculateSeconds(
    scopedWorkerOptions,
    execution.AttemptCount,
    retryJitterSource.NextUnitInterval());
var nextAttemptAtUtc = scopedClock.UtcNow.AddSeconds(retryDelaySeconds);
```

Do not change `RescheduleAsync`, SQL, telemetry cardinality, exception
classification, or ownership checks.

- [ ] **Step 4: Re-run the Runner and all Jobs Unit tests**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~Full.NET.UnitTests.Jobs"
```

Expected: all discovered Jobs tests pass with zero warning and zero skipped
test.

### Task 4: Prove exponential scheduling through both databases

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsRetrySchedulingAssertions.cs`

**Interfaces:**
- Consumes: existing SQL Server/MySQL Jobs API fixtures
- Preserves: existing `fn_jobs_execution.NextAttemptAtUtc` and 037 schema

- [ ] **Step 1: Extend the shared assertion before changing production**

Configure the existing assertion with:

```csharp
MaxAttempts = 3,
RetryDelaySeconds = 30,
RetryBackoffMode = "exponential",
RetryMaxDelaySeconds = 86400,
RetryJitterPercent = 0
```

Keep the first failure assertion at `+30s`. Advance the mutable clock beyond
the first due time, run again, and assert the same execution is pending with
`AttemptCount = 2` and `NextAttemptAtUtc = secondFailureTime + 60s`. Advance
beyond the second due time, run a third time, and retain the existing terminal
failure assertions with `AttemptCount = 3`.

- [ ] **Step 2: Run SQL Server and MySQL focused tests**

Only after the shared Docker queue is explicitly released, run providers
serially:

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --filter "FullyQualifiedName~JobsApiSqlServerTests"
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --filter "FullyQualifiedName~JobsApiMySqlTests"
```

Expected: the Jobs retry lifecycle passes on both providers; Testcontainers
and Ryuk exit before the next provider starts.

### Task 5: Verification record and affected gates

**Files:**
- Create: `docs/verification/jobs-retry-backoff-jitter-2026-07-30.md`
- Modify: `docs/verification/jobs-retry-classification-2026-07-30.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Consumes: focused Unit and dual-provider outputs
- Produces: an evidence-backed `Build-verified` retry-backoff record

- [ ] **Step 1: Document the exact contract and non-claims**

Record the baseline, task snapshot, defaults, bounds, calculation semantics,
RED/GREEN evidence, both provider results, Docker teardown, and the fact that
this slice does not prove capacity gains or change `MaxConcurrency`.

- [ ] **Step 2: Audit the affected set**

Run:

```powershell
pnpm test:integration:affected:plan -- --snapshot jobs-retry-backoff-implementation-20260730 --phase inner
```

Inspect the selected targets before running anything. At slice close, run only
the selector-owned set:

```powershell
pnpm test:integration:affected -- --snapshot jobs-retry-backoff-implementation-20260730 --phase slice
```

Do not duplicate CodeGeneration, Files, Realtime, Settings, or other targets
that their owning windows have already run from the same shared state.

- [ ] **Step 3: Run final local gates**

Run:

```powershell
dotnet build src/Modules/Full.NET.Modules.Jobs/Full.NET.Modules.Jobs.csproj -c Release --no-restore
pnpm test:naming
pnpm test:governance
git diff --check
git status --short --branch
```

Expected: Jobs Release build succeeds with zero warning/error, governance and
naming pass, and diff check has no whitespace error. Line-ending notices from
the pre-existing shared worktree are warnings only.

- [ ] **Step 4: Hand off test counts without editing the matrix**

Report the exact number of newly added Unit/Integration methods to the final
matrix owner. Do not update `eng/testing/test-matrix.json` while downstream
windows are still adding tests; final thresholds must come from fresh Release
discovery.
