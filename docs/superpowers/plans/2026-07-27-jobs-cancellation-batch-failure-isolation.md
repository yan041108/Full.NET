# Jobs Cancellation and Batch Failure Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Jobs Runner 在宿主取消时立即传播取消且不把任务误记为业务失败，并用 SQL Server/MySQL 真实数据库锁定同批坏任务不会阻断后续有效任务。

**Architecture:** 保留现有租约、领取 SQL、公开 API 与数据库结构。`JobExecutionRunner` 只在调用令牌确实取消时单独传播 `OperationCanceledException`，让租约按既有过期恢复语义接管；普通处理器异常与缺失处理器继续落为失败并处理批次后续执行。双库场景嵌入既有两项 Jobs Integration，不增加 Integration canonical。

**Tech Stack:** .NET 10、Microsoft Testing Platform、MSTest、NSubstitute、Dapper、SQL Server、MySQL、Testcontainers。

## Global Constraints

- 不新增或修改公开 HTTP/JSON 契约、迁移、数据库对象、权限码或配置键。
- Unit canonical 从 395 增至 396；Integration 保持 189，四分片保持 35/35/62/57。
- 所有新增手写注释使用中文，只解释取消、租约和批次隔离不变量。
- SQL Server/MySQL 必须运行同一 Jobs 聚焦场景；Docker 运行期间通知其他任务独占状态。
- main 合并顺序为 session refresh → 本任务 → Identity Task 15；本任务清理后 Identity 最终更新到 398/7/49/189。

---

### Task 1: Propagate Host Cancellation

**Files:**
- Create: `tests/Full.NET.UnitTests/Jobs/JobExecutionRunnerTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs`

**Interfaces:**
- Consumes: `JobExecutionRunner.ProcessPendingAsync(int, CancellationToken)`、`IJobHandler.ExecuteAsync(CancellationToken)`。
- Produces: 调用令牌取消时抛出 `OperationCanceledException`，不执行 `JobSql.MarkExecutionFailed`。

- [x] **Step 1: 写取消 RED 测试**

  构造一个 SQL Server Runner，领取一条执行记录；处理器在执行时取消传入令牌并返回取消任务。断言：

  ```csharp
  await Assert.ThrowsAsync<OperationCanceledException>(
      () => runner.ProcessPendingAsync(1, cancellationTokenSource.Token));
  await commandExecutor.DidNotReceive().ExecuteAsync(
      JobSql.MarkExecutionFailed,
      Arg.Any<object>(),
      Arg.Any<CancellationToken>());
  ```

- [x] **Step 2: 运行 RED**

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter 'FullyQualifiedName~JobExecutionRunnerTests' --minimum-expected-tests 1 --timeout 5m
  ```

  预期：测试失败，因为当前通用 `catch (Exception)` 吞掉取消并尝试标记失败。

- [x] **Step 3: 写最小 GREEN 实现**

  在普通异常捕获之前增加：

  ```csharp
  catch (OperationCanceledException)
      when (cancellationToken.IsCancellationRequested)
  {
      throw;
  }
  ```

  不清理租约；中断期间已领取的执行由现有租约过期恢复路径接管。

- [x] **Step 4: 运行 GREEN**

  重复 Step 2 命令，预期 1/1、失败 0、跳过 0。

### Task 2: Prove Batch Failure Isolation on Both Providers

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Jobs/JobsBatchFailureIsolationAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsHostDefinitionAssertions.cs`

**Interfaces:**
- Consumes: 既有启用的 `jobs.ping` 定义、`JobExecutionRunner.ProcessPendingAsync(2, CancellationToken)`。
- Produces: 一条缺失 Handler 的执行为 `failed`，紧随其后的 `jobs.ping` 执行为 `succeeded`；两条均 `AttemptCount = 1`、清空租约并写入结束时间。

- [x] **Step 1: 在既有 Jobs 生命周期内加入同批场景**

  直接用 Host-only 测试 SQL 写入：

  ```text
  jobs.missing-handler 定义 + pending 执行（CreatedAtUtc 较早）
  既有 jobs.ping 定义 + pending 执行（CreatedAtUtc 较晚）
  ```

  调用一次批大小为 2 的 Runner，并只按本场景两个执行 ID 回查，避免前置 API/并发夹具记录污染断言。

- [x] **Step 2: 运行 SQL Server/MySQL 聚焦验证**

  ```powershell
  dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --nologo
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter 'FullyQualifiedName~JobsApi' --minimum-expected-tests 2 --timeout 15m
  ```

  预期：2/2、失败 0、跳过 0；SQL Server 与 MySQL 均证明坏任务不阻断同批后续执行。

### Task 3: Synchronize Evidence and Canonical Gates

**Files:**
- Create: `docs/verification/jobs-cancellation-batch-failure-isolation-2026-07-27.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`

**Interfaces:**
- Produces: 本任务阶段 canonical `396/7/49/189`；同时把 session refresh 事实修正为 `localStorage` 跨 Tab 短租约回退。

- [x] **Step 1: 更新验证事实与限制**

  记录取消不落失败、租约过期接管、双库批次隔离、实际命令/数量/耗时，并保留“失败重试分类、Cron/延迟和运维重放仍未完成”。

- [x] **Step 2: 更新四处 Unit 门槛和最新审计增补**

  把 Unit 最小数量 395 更新为 396，Compatibility/Architecture/Integration 保持 7/49/189；同步审计中的 session refresh 存储事实。

- [x] **Step 3: 执行最终门禁**

  ```powershell
  dotnet build Full.NET.slnx -c Release --no-restore --nologo
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 396 --timeout 10m
  node scripts/testing/verify-integration-shards.mjs
  pnpm test:governance
  pnpm test:skills
  pnpm test:workspace
  git diff --check
  ```

  预期：Release 0 warning/0 error；Unit 396/396；分片 35/35/62/57=189；Governance 11/11；Skill 52；workspace 与 diff check 通过。

- [x] **Step 4: 完成规则与 Skill 复盘**

  读取 `rules/rule-evolution.md` 与 `rules/skill-evolution.md`。若只命中单次 Jobs 取消缺陷且现有并发/取消规则已覆盖，则记录无需新增规则或 Skill。

- [ ] **Step 5: 提交、合并并清理**

  精确暂存本计划列出的 owned 文件，提交 `fix(jobs): preserve cancellation and isolate batch failures`；同步最新 main 后重新运行受影响门槛，快进合并到 main，删除 `codex/jobs-batch-failure-isolation` 工作树与分支，并确认 Docker/Integration 进程已释放。

## Self-Review

- Spec coverage：覆盖取消传播、失败批次隔离、租约状态、双库、canonical、文档和清理；不扩展 Cron、重试、重放或 API。
- Placeholder scan：无 `TBD`、`TODO` 或未定义实现步骤。
- Type consistency：统一使用 `JobExecutionRunner`、`JobSql.MarkExecutionFailed`、`JobExecutionStatuses`、`396/7/49/189`。
