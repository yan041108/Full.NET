# Jobs Active Lease Renewal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让运行时间超过初始租约的 Jobs 执行持续延长当前批次租约，避免健康长任务被其他 Worker 当作过期任务重复领取。

**Architecture:** 复用 `fn_jobs_execution.LeaseId/LeaseExpiresAtUtc`，不新增迁移。`JobExecutionRunner` 用同一 `LeaseId` 领取批次后，在处理期间按有界间隔更新仍属于该租约且仍为 `running` 的记录；续租失败或所有权丢失时取消当前处理并让本轮失败，宿主取消仍停止续租并由既有租约过期恢复。SQL Server/MySQL 使用同一条参数化 UPDATE。

**Tech Stack:** .NET 10、Options、Microsoft Testing Platform、MSTest、Dapper、SQL Server、MySQL、Testcontainers。

## Global Constraints

- 不新增数据库对象、迁移、公开 HTTP/JSON 契约、权限码或 Handler API。
- 配置仍位于 `Jobs:Worker`：`LeaseSeconds` 默认 300、范围 30～3600；`LeaseRenewalSeconds` 默认 60、范围 5～1200，且不得大于 `LeaseSeconds / 2`。
- 续租 UPDATE 必须同时匹配 Host 作用域、`LeaseId` 和 `running` 状态；不得延长已完成或已被其他 Worker 重新领取的记录。
- 宿主取消不是业务失败：停止续租、传播取消，不清理租约，由过期恢复路径接管。
- Unit canonical 预计从 400 增至 404；Integration 保持 189，最终数字以同步最新 main 后的发现结果为准。
- 当前只做非 Docker 开发；最终双库验证与 main 合并排在 Outbox → admin-real-stack E2E → session lease-horizon 之后。

---

### Task 1: Bound Lease Configuration

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobsWorkerOptions.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/JobsModule.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/appsettings.json`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobsWorkerOptionsTests.cs`

**Interfaces:**
- Produces: `JobsWorkerOptions.LeaseSeconds`、`JobsWorkerOptions.LeaseRenewalSeconds`。
- Consumes: `JobsModule.AddBackgroundServices(IServiceCollection, IConfiguration)`。

- [x] **Step 1: 写配置 RED 测试**

  新增测试方法，断言默认值为 300/60，并拒绝租约低于 30 秒、续租低于 5 秒，以及续租间隔超过租约一半。

- [x] **Step 2: 运行 RED**

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
  ```

  预期：因两个配置属性尚不存在而编译失败。

- [x] **Step 3: 实现最小配置与校验**

  在 `JobsWorkerOptions` 增加两个秒级属性；Validator 返回稳定错误文本：

  ```text
  Jobs:Worker:LeaseSeconds must be between 30 and 3600.
  Jobs:Worker:LeaseRenewalSeconds must be between 5 and 1200.
  Jobs:Worker:LeaseRenewalSeconds must not exceed half of LeaseSeconds.
  ```

  `RegisterExecutionCore` 注册默认 Options，使 API 手动触发与 Worker Runner 都能解析同一默认租约；Worker Profile 继续绑定并 `ValidateOnStart`。`appsettings.json` 显式写入 300/60。

- [x] **Step 4: 运行 GREEN**

  构建并运行 `JobsWorkerOptionsTests`，预期 2/2、失败 0、跳过 0。

### Task 2: Renew the Owned Batch While It Runs

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobExecutionRunnerTests.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobExecutionHostedProcessorTests.cs`

**Interfaces:**
- Produces: `JobSql.RenewExecutionLease` 与 Runner 内部续租循环。
- Consumes: `JobsWorkerOptions.LeaseSeconds`、`JobsWorkerOptions.LeaseRenewalSeconds`、当前批次 `LeaseId`。

- [x] **Step 1: 写 Runner RED 测试**

  新增一个会阻塞到第一次续租发生的 Handler；配置测试专用 2 秒租约和 1 秒续租，断言 Runner 在 Handler 完成前执行 `JobSql.RenewExecutionLease`，参数包含当前 `LeaseId`、`running` 状态及晚于初始租约的 `LeaseExpiresAtUtc`。
  增加终态竞态回归：让最后一个 Handler 与返回 0 的续租确定性交错，先证明已成功写入终态的批次会被误报所有权丢失。

- [x] **Step 2: 运行 RED**

  构建并运行 `JobExecutionRunnerTests`，预期新测试因缺少续租语句和循环失败，既有宿主取消测试保持通过。

- [x] **Step 3: 实现所有权受控续租**

  增加 Provider-neutral Statement：

  ```sql
  UPDATE fn_jobs_execution
  SET LeaseExpiresAtUtc = @LeaseExpiresAtUtc
  WHERE TenantId IS NULL
    AND LeaseId = @LeaseId
    AND Status = @RunningStatus
  ```

  Runner 初始租约改用 `LeaseSeconds`。批次处理与续租循环并行：处理完成时取消并等待续租任务；续租 UPDATE 返回 0 或抛异常时取消 Handler 使用的 linked token、等待其退出并传播租约故障；宿主取消继续传播 `OperationCanceledException`。

- [x] **Step 4: 运行 GREEN 与 Jobs Unit 回归**

  构建并运行 `JobExecutionRunnerTests|JobsWorkerOptionsTests|JobExecutionHostedProcessorTests`，预期 7/7、失败 0、跳过 0；其中一项锁定续租返回 0 时取消 Handler 并传播所有权丢失，另一项锁定最后一个执行已写入终态时不把零行续租误报为故障。

### Task 3: Prove Renewal on SQL Server and MySQL

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Jobs/JobsActiveLeaseRenewalAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsHostDefinitionAssertions.cs`

**Interfaces:**
- Consumes: 真实 `IQueryExecutor`/`ICommandExecutor`、测试专用阻塞 `IJobHandler`、两个 `JobExecutionRunner`。
- Produces: 两库相同场景证明初始租约过期后第二个 Worker 仍领取 0 条，释放 Handler 后原执行成功且只增加一次 `AttemptCount`。

- [x] **Step 1: 在既有 Jobs 场景加入长任务夹具**

  写入测试专用定义与 pending 执行；第一个 Runner 使用 4 秒租约/1 秒续租并阻塞 Handler。轮询数据库直到 `LeaseExpiresAtUtc` 晚于初始租约，等待初始租约时点过去，再让第二个 Runner尝试领取并断言返回 0。

- [x] **Step 2: 完成原执行并核对终态**

  释放 Handler，断言第一个 Runner 返回 1；执行终态为 `succeeded`、`AttemptCount = 1`、租约字段清空。所有等待使用明确超时，避免挂起测试进程。

- [ ] **Step 3: 等待队列后运行双库聚焦**

  ```powershell
  dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --nologo
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter 'FullyQualifiedName~JobsApi' --minimum-expected-tests 2 --timeout 20m
  ```

  预期 2/2、失败 0、跳过 0；Integration canonical 仍为 189。

### Task 4: Synchronize Evidence and Close the Branch

**Files:**
- Create: `docs/verification/jobs-active-lease-renewal-2026-07-27.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify after latest-main synchronization: `README.md`
- Modify after latest-main synchronization: `docs/development/getting-started.md`
- Modify after latest-main synchronization: `.github/workflows/ci.yml`
- Modify after latest-main synchronization: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify after latest-main synchronization: `docs/verification/test-threshold-audit-2026-07-19.md`

**Interfaces:**
- Produces: 最新 main 上准确的 canonical 门槛、配置/故障语义和双库证据。

- [ ] **Step 1: 记录能力与限制**

  记录默认值、合法范围、续租所有权条件、续租故障取消语义、双库命令/数量/耗时；保留 Cron/延迟调度、失败重试分类、运维重放和真实大规模压力基准为未完成项。

- [ ] **Step 2: 同步最终 main**

  等 Outbox、E2E 与 session lease-horizon 依次合并清理后，将最新 main 合入本分支，解决共享 canonical/路线图文档差异。

- [ ] **Step 3: 执行最终门禁**

  运行 Release、Unit、Compatibility、Architecture、Integration 分片发现、Jobs 双库 2/2、Governance、Skills、workspace 与 `git diff --check`。若本任务新增 4 个 Unit 且前序无 .NET 测试变化，目标为 404/7/49/189。

- [ ] **Step 4: 规则与 Skills 复盘**

  读取并执行 `rules/rule-evolution.md`、`rules/skill-evolution.md`；只有形成跨任务重复模式并达到门槛时才更新规则或项目 Skill。

- [ ] **Step 5: 提交、合并和清理**

  精确暂存 owned 文件，提交 Jobs 切片；在最新 main 验证后合并到 main，删除 `codex/jobs-active-lease-renewal` 分支与工作树，并确认 Docker/Integration 进程已释放。

## Self-Review

- Spec coverage：覆盖配置、初始租约、周期续租、所有权丢失、宿主取消、双库竞争、文档、canonical 与清理。
- Placeholder scan：无 `TBD`、`TODO`、泛化“适当处理”或未定义实现动作。
- Type consistency：统一使用 `LeaseSeconds`、`LeaseRenewalSeconds`、`JobSql.RenewExecutionLease` 与当前批次 `LeaseId`。
