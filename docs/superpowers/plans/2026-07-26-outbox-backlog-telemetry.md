# Outbox Backlog Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:test-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Outbox Worker 暴露真实待处理消息数与最老消息年龄，并用 SQL Server/MySQL 真实数据锁定查询和指标语义。

**Architecture:** 独立 `IOutboxBacklogReader` 提供不改变租约状态的 backlog 快照，避免破坏既有 `IOutboxStore` 租约合同；Dapper 同一 scoped 实例实现两接口，并使用一条两库共同语义的 Host-only 聚合查询。Worker 按受校验的 5～3600 秒周期在领取前尽力采样，并通过独立 `Full.NET.Outbox` Meter 记录无标签 Gauge；采样或指标消费者故障不得阻断消息处理。

**Tech Stack:** .NET 10、System.Diagnostics.Metrics、OpenTelemetry、Dapper、Microsoft Testing Platform、MSTest、SQL Server 2022、MySQL 8.4。

## Global Constraints

- backlog 包含所有 `ProcessedAtUtc IS NULL AND DeadLetteredAtUtc IS NULL` 消息，包括等待重试和持有租约的消息；不能用本轮领取数代替。
- 指标固定为 `fullnet.outbox.backlog.messages` 与 `fullnet.outbox.backlog.oldest_age`，不得加入租户、消息类型、异常文本或其他高基数标签。
- backlog 查询只读且不得领取、续租、确认或修改消息。
- 采样失败时保留取消语义，但普通查询/指标异常不得阻断既有 Outbox 处理。
- SQL Server 与 MySQL 必须运行同一组快照语义断言。

---

### Task 1: 建立快照合同、Dapper 查询与 Worker 指标

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxBacklogReader.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxStore.cs`
- Create: `src/Hosts/Full.NET.Host.Worker/OutboxBacklogTelemetry.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/Program.cs`
- Modify: `tests/Full.NET.UnitTests/Outbox/OutboxProcessorTests.cs`

**Interfaces:**
- Produces: `Task<OutboxBacklogSnapshot> IOutboxBacklogReader.ReadBacklogAsync(CancellationToken)`
- Produces: `OutboxBacklogSnapshot(long PendingCount, DateTimeOffset? OldestOccurredAtUtc)`
- Produces: Meter `Full.NET.Outbox`
- Produces: 配置 `OutboxWorker:BacklogSampleSeconds`，默认 30 秒。
- Consumes: `IClock.UtcNow` 计算非负最老消息年龄秒数。

- [x] **Step 1: 写入失败的指标与旁路测试**

  在 `OutboxProcessorTests` 增加 `[DoNotParallelize]`，使用 `MeterListener` 捕获两个指标。测试替身返回：

  ```csharp
  new OutboxBacklogSnapshot(
      2,
      now.AddSeconds(-90))
  ```

  调用 `ProcessOnceAsync` 后断言消息 Gauge 为 `2`、最老年龄 Gauge 为 `90d`。第二个测试让
  `ReadBacklogAsync` 抛出 `InvalidOperationException`，同时返回一个可处理消息，断言消息仍被
  `MarkProcessedAsync`。

- [x] **Step 2: 运行测试并确认 RED**

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore -m:1
  ```

  预期：编译失败，指出 `OutboxBacklogSnapshot` 或 `ReadBacklogAsync` 尚不存在。

- [x] **Step 3: 增加只读 backlog 合同与 SQL**

  在独立 `IOutboxBacklogReader.cs` 增加中文 XML 文档和：

  ```csharp
  Task<OutboxBacklogSnapshot> ReadBacklogAsync(
      CancellationToken cancellationToken);

  public sealed record OutboxBacklogSnapshot(
      long PendingCount,
      DateTimeOffset? OldestOccurredAtUtc);
  ```

  在 `OutboxSql` 增加成对的 `outbox.read_backlog.sql_server` /
  `outbox.read_backlog.my_sql`。SQL Server 使用 `COUNT_BIG(*)`，MySQL 使用 `COUNT(*)`，
  共同的过滤与时间聚合为：

  ```sql
  SELECT COUNT(*) AS PendingCount,
         MIN(OccurredAtUtc) AS OldestOccurredAtUtc
  FROM fn_outbox_message
  WHERE ProcessedAtUtc IS NULL
    AND DeadLetteredAtUtc IS NULL;
  ```

  Statement 使用 `SqlDataScope.HostOnly`。`DapperOutboxStore` 将数据库 UTC `DateTime?`
  显式规范为 `DateTimeOffset?`，空队列返回 `0/null`。

- [x] **Step 4: 实现低基数 Gauge 与非阻断采样**

  `OutboxBacklogTelemetry` 创建：

  ```csharp
  public const string MeterName = "Full.NET.Outbox";
  ```

  并记录：

  ```text
  fullnet.outbox.backlog.messages      {message}
  fullnet.outbox.backlog.oldest_age    s
  ```

  `OutboxProcessor` 在每轮 `AcquireAsync` 前调用 `ReadBacklogAsync`。采样方法对请求取消继续
  抛出，对其他异常只写稳定源生成日志并继续领取；指标记录自身吞掉监听器异常。`Program.cs`
  将 `Full.NET.Outbox` 加入 OpenTelemetry Metrics。`BacklogSampleSeconds` 默认 30 秒并限制在
  5～3600 秒；先推进下一采样点再查询，防止依赖故障时每轮重复施压。

- [x] **Step 5: 运行聚焦单测并确认 GREEN**

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore -m:1
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll `
    --filter "FullyQualifiedName~OutboxProcessorTests" --no-ansi --progress off `
    --minimum-expected-tests 11 --timeout 10m
  ```

  结果：11/11 通过，失败 0、跳过 0；包含周期去重与配置下界门禁。

### Task 2: SQL Server/MySQL 快照语义验证

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Messaging/OutboxRecoveryTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `ReadBacklogAsync`。
- Produces: 两库各一项测试，证明未处理总数、最老时间、处理后收敛及空队列状态。

- [x] **Step 1: 增加两库参数化共享断言**

  SQL Server/MySQL 各创建隔离数据库并执行迁移。先写入并终结一条更早的死信，再按
  `2026-07-26T00:00:00Z` 与 `2026-07-26T00:02:00Z` 写入两条待处理 Outbox：

  ```csharp
  Assert.AreEqual(2L, snapshot.PendingCount);
  Assert.AreEqual(firstOccurredAtUtc, snapshot.OldestOccurredAtUtc);
  ```

  领取并成功处理第一条后断言 `1/secondOccurredAtUtc`；处理第二条后断言 `0/null`。

- [x] **Step 2: 构建 Integration 并执行双库聚焦测试**

  ```powershell
  dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj `
    -c Release --no-restore -m:1
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll `
    --filter "FullyQualifiedName~OutboxRecoveryTests" --no-ansi --progress off `
    --minimum-expected-tests 8 --timeout 20m
  ```

  结果：加强死信排除断言后的 Integration 构建 0 warning/0 error；SQL Server/MySQL 合计
  8/8 通过，失败 0、跳过 0，耗时 2m50s。

### Task 3: 状态、门槛、复盘与主线收口

**Files:**
- Modify: `README.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `scripts/testing/run-integration-shard.mjs`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/operations/outbox-worker-topology.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/superpowers/plans/2026-07-18-architecture-hardening.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Create: `docs/verification/outbox-backlog-telemetry-2026-07-26.md`

**Interfaces:**
- Consumes: Tasks 1–2 的 RED/GREEN、双库和完整门禁证据。
- Produces: 同步主线后 canonical 门槛 `390/7/49/186`；若并行任务实际改变发现数，以测试运行器新鲜发现结果同时更新全部 canonical 来源。

- [ ] **Step 1: 同步状态与运维语义**

  记录两个指标、无标签边界、采样故障不阻断消费，以及建议的 pending count/oldest age
  告警基线。缓存/Outbox 能力继续保持 `Build-verified`：生产指标导出、真实告警平台和完整
  S0/S1/S2 分级仍未验证，不能标记 `Verified`。

- [ ] **Step 2: 同步并行任务后的 main**

  等高优先级日志任务提交后，将最新 `main` 合入本分支；解决共享文档门槛时保留两边已验证
  事实。API Key UI 分支若已合入，也同样保留其双端状态，不覆盖其文件。

- [ ] **Step 3: 执行最终完整门禁**

  ```powershell
  dotnet build Full.NET.slnx -c Release
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 390 --timeout 20m
  dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7 --timeout 10m
  dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 49 --timeout 10m
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 186 --timeout 90m
  pnpm test:naming
  pnpm test:skills
  pnpm test:governance
  pnpm test:integration:tooling
  pnpm test:integration:partitions
  git diff --check
  git status --short --branch
  ```

  预期：Release 0 warning/0 error；所有发现测试通过、失败 0、跳过 0；Node 门禁全部通过。

- [ ] **Step 4: 完成规则与 Skill 复盘**

  按 `rules/rule-evolution.md` 判断是否形成第二次可泛化遗漏，再按
  `rules/skill-evolution.md` 判断是否需要演进项目 Skill。未达到门槛时只在验证记录写明
  结论，不新增近义规则。

- [ ] **Step 5: 提交、合并并清理**

  在隔离分支生成一个聚焦提交；切回主工作区确认日志任务已提交且 `main` 无受保护改动后，
  将 `codex/outbox-backlog-telemetry` 合并到 `main`。合并成功后删除工作树和已合并分支，
  再检查 `git status`、`git worktree list` 与分支列表。
