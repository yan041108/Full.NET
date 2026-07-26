# Jobs Worker Bounded Options Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Jobs Worker 的批大小与轮询间隔从硬编码值收敛为启动期校验的 `Jobs:Worker` 配置，同时保持现有默认行为不变。

**Architecture:** 配置类型和校验器留在 Jobs 主模块的 `Execution` 边界，由 `JobsModule.AddBackgroundServices` 绑定并启用启动校验；API 模块注册不携带 Worker 专属配置。`JobExecutionHostedProcessor` 缓存已校验配置，每轮把 `BatchSize` 传给现有 Runner，并以 `PollMilliseconds` 计算等待时间；不修改领取 SQL、租约、数据库结构或公开 HTTP/JSON 契约。

**Tech Stack:** .NET 10、Options、BackgroundService、MSTest、Microsoft Testing Platform。

## Global Constraints

- 配置节固定为 `Jobs:Worker`；`BatchSize` 范围 **1～50**，默认 **10**；`PollMilliseconds` 范围 **100～60000**，默认 **2000**。
- 无效值必须在宿主启动期失败，禁止由 Runner 的 `Math.Clamp` 静默掩盖运维配置错误。
- 只在 `AddBackgroundServices` 绑定 Worker 配置，`AddServices` 的 API Profile 不注册后台 Hosted Service。
- 不修改数据库、SQL、租约时长、公共 API、OpenAPI、Identity、客户端或 Integration 测试数量；不占用 Docker。
- 当前队列合并后预计 Unit canonical 从 **398 → 400**；Compatibility/Architecture/Integration 保持 **7/49/189**，最终数字必须以同步后的最新 main 为准。

---

### Task 1: Define and Validate Jobs Worker Options

**Files:**
- Create: `src/Modules/Full.NET.Modules.Jobs/Execution/JobsWorkerOptions.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/JobsModule.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/appsettings.json`
- Create: `tests/Full.NET.UnitTests/Jobs/JobsWorkerOptionsTests.cs`

**Interfaces:**
- Produces: `JobsWorkerOptions.SectionName = "Jobs:Worker"`、`BatchSize`、`PollMilliseconds` 与 `JobsWorkerOptionsValidator`。
- Consumes: `JobsModule.AddBackgroundServices(IServiceCollection, IConfiguration)`。

- [x] **Step 1: 写配置 RED 测试**

  新增一个测试方法，同时锁定安全默认值与两个非法下界：

  ```csharp
  [TestMethod]
  public void Validator_AcceptsDefaultsAndRejectsUnsafeBounds()
  {
      var validator = new JobsWorkerOptionsValidator();

      Assert.IsTrue(
          validator.Validate(Options.DefaultName, new JobsWorkerOptions()).Succeeded);

      var result = validator.Validate(
          Options.DefaultName,
          new JobsWorkerOptions
          {
              BatchSize = 0,
              PollMilliseconds = 99,
          });

      Assert.IsFalse(result.Succeeded);
      CollectionAssert.Contains(
          (result.Failures ?? []).ToArray(),
          "Jobs:Worker:BatchSize must be between 1 and 50.");
      CollectionAssert.Contains(
          (result.Failures ?? []).ToArray(),
          "Jobs:Worker:PollMilliseconds must be between 100 and 60000.");
  }
  ```

- [x] **Step 2: 运行 RED**

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
  ```

  预期：因 `JobsWorkerOptions` 与 `JobsWorkerOptionsValidator` 尚不存在而编译失败；失败不得来自测试语法或依赖还原。

- [x] **Step 3: 实现最小配置与注册**

  新建配置类型与显式校验器：

  ```csharp
  internal sealed class JobsWorkerOptions
  {
      public const string SectionName = "Jobs:Worker";

      public int BatchSize { get; set; } = 10;

      public int PollMilliseconds { get; set; } = 2000;
  }
  ```

  校验器分别拒绝 `BatchSize` 不在 1～50、`PollMilliseconds` 不在 100～60000 的配置，并返回上述稳定错误文本。`AddBackgroundServices` 使用：

  ```csharp
  services.AddOptions<JobsWorkerOptions>()
      .Bind(configuration.GetSection(JobsWorkerOptions.SectionName))
      .ValidateOnStart();
  services.TryAddEnumerable(
      ServiceDescriptor.Singleton<
          IValidateOptions<JobsWorkerOptions>,
          JobsWorkerOptionsValidator>());
  ```

  Worker `appsettings.json` 显式保留默认配置：

  ```json
  "Jobs": {
    "Worker": {
      "BatchSize": 10,
      "PollMilliseconds": 2000
    }
  }
  ```

- [x] **Step 4: 运行 GREEN**

  重复 Step 2 构建，再运行：

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~JobsWorkerOptionsTests"
  ```

  预期：**1/1**，失败 0、跳过 0。

### Task 2: Make the Hosted Processor Consume the Options

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionHostedProcessor.cs`
- Create: `tests/Full.NET.UnitTests/Jobs/JobExecutionHostedProcessorTests.cs`

**Interfaces:**
- Consumes: `IOptions<JobsWorkerOptions>`、`JobExecutionRunner.ProcessPendingAsync(int, CancellationToken)`。
- Produces: `JobExecutionHostedProcessor.ProcessOnceAsync(CancellationToken)` 与只读 `PollingDelay`，两者均为模块内部测试边界。

- [x] **Step 1: 写 Processor RED 测试**

  使用真实 `JobExecutionRunner` 和手写 `IQueryExecutor` 记录 SQL 参数；配置 `BatchSize = 7`、
  `PollMilliseconds = 250`，断言：

  ```csharp
  await processor.ProcessOnceAsync(CancellationToken.None);

  Assert.AreEqual(7, queryExecutor.ObservedBatchSize);
  Assert.AreEqual(TimeSpan.FromMilliseconds(250), processor.PollingDelay);
  ```

  Query fake 对 `JobSql.AcquireExecutionsSqlServer` 返回空集合，避免构造业务任务或数据库。

- [x] **Step 2: 运行 RED**

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
  ```

  预期：因 Processor 尚未接收 Options，且缺少 `ProcessOnceAsync`/`PollingDelay` 而编译失败。

- [x] **Step 3: 实现最小 Processor 变更**

  Processor 构造函数增加 `IOptions<JobsWorkerOptions>` 并缓存 `.Value`；循环改为：

  ```csharp
  await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
  await Task.Delay(PollingDelay, stoppingToken).ConfigureAwait(false);
  ```

  `ProcessOnceAsync` 只创建作用域、解析 Runner，并调用：

  ```csharp
  await runner
      .ProcessPendingAsync(_options.BatchSize, cancellationToken)
      .ConfigureAwait(false);
  ```

  `PollingDelay` 返回 `TimeSpan.FromMilliseconds(_options.PollMilliseconds)`。

- [x] **Step 4: 运行 GREEN 与 Jobs 回归**

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~JobsWorkerOptionsTests|FullyQualifiedName~JobExecutionHostedProcessorTests|FullyQualifiedName~JobExecutionRunnerTests"
  ```

  预期：新增 **2** 项连同既有 Runner 回归全部通过，失败 0、跳过 0。

### Task 3: Synchronize Evidence, Canonical Gates, and Main

**Files:**
- Create: `docs/verification/jobs-worker-bounded-options-2026-07-27.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify after latest-main synchronization: `README.md`
- Modify after latest-main synchronization: `docs/development/getting-started.md`
- Modify after latest-main synchronization: `.github/workflows/ci.yml`
- Modify after latest-main synchronization: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify after latest-main synchronization: `docs/verification/test-threshold-audit-2026-07-19.md`

**Interfaces:**
- Produces: 最新 main 基线上的 Unit canonical（预计 **400**）及有界配置的运维事实。

- [x] **Step 1: 记录配置边界与未改变项**

  验证记录必须列出默认值、合法范围、启动失败语义、API Profile 不注册 Hosted Service，以及未改变的领取 SQL、租约和双库行为。能力矩阵只增加配置事实，不提升 `Build-verified` 状态。

- [ ] **Step 2: 等待并同步既定合并队列**

  顺序固定为 Task15 → OpenAPI schema → 客户端损坏锁记录 → 本任务。收到客户端最终 main HEAD 后，在隔离分支合并最新 main，解决共享审计/门槛文档差异，再以实际测试发现数更新四处 canonical。

- [ ] **Step 3: 执行最终非 Docker 门禁**

  ```powershell
  dotnet build Full.NET.slnx -c Release --no-restore --nologo
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 400 --timeout 10m
  dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7 --timeout 10m
  dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 49 --timeout 10m
  node scripts/testing/verify-integration-shards.mjs
  pnpm test:governance
  pnpm test:skills
  pnpm test:workspace
  git diff --check
  ```

  预期：Release 0 warning/0 error；Unit 使用同步后的准确数量；Compatibility **7/7**；
  Architecture **49/49**；Integration 发现 **35/35/62/57 = 189**；Governance **11/11**；
  Skill **52**；workspace 与 diff check 通过。

- [ ] **Step 4: 规则、Skill、提交和清理**

  读取并执行 `rules/rule-evolution.md`、`rules/skill-evolution.md`；若仅命中一次 Jobs 配置硬编码且已有 Options/启动校验规则覆盖，则结论为无新增规则/Skill。提交
  `feat(jobs): bound worker polling options`，fast-forward 合入 main，在 main 重跑 Unit/Jobs 聚焦与静态门禁后删除
  `codex/jobs-worker-options` 分支和 `.worktrees/jobs-worker-options` 工作树。

## Self-Review

- Spec coverage：配置键、默认值、上下界、启动校验、Processor 实际消费、Profile 边界、文档、canonical、同步顺序和清理均有对应步骤。
- Placeholder scan：无 `TBD`、`TODO` 或未定义的实现动作。
- Type consistency：统一使用 `JobsWorkerOptions`、`JobsWorkerOptionsValidator`、`ProcessOnceAsync`、`PollingDelay`、`BatchSize` 与 `PollMilliseconds`。
