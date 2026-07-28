# Outbox 保留清理并行容量矩阵 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 SQL Server/MySQL 的真实 API 混合负载中，对 Outbox 清理关闭/开启进行可重复 A/B，补齐请求与 Worker 尾延迟、删除吞吐、锁等待和事务日志/undo 写放大证据。

**Architecture:** 复用 `mixed-load` 的隔离 Testcontainers、真实 API Host、Dapper 和资源采样；仅在显式提供 Outbox retention profile 时启动基准内 drain loop，并预置严格早于 cutoff 的成功终态历史。`off` 与 `on` 使用独立数据库，`on` 额外运行生产 `IOutboxRetentionStore` 小批量删除。该切片不模拟 Handler 延迟、多副本或调整生产默认并发，这些仍属于 Task 24。

**Tech Stack:** .NET 10、MSTest、Dapper、SQL Server 2022、MySQL 8.4、Testcontainers、现有 `Full.NET.Benchmarks` mixed-load 工具。

## Global Constraints

- 任务 Git 基线固定为 `0f434fde7d3182deb03fadfac6995052cd0bb113`。
- 生产默认 `OutboxWorker:MaxConcurrency=1` 和 retention 默认关闭保持不变。
- SQL Server/MySQL 使用同一场景、并发、预热、采样时长、种子量和批大小。
- `off/on` 各自使用全新迁移数据库，禁止复用被上一档污染的数据。
- 只运行受影响测试；完整 199 项 Integration 仅由 main CI 四分片运行。

---

### Task 1: 冻结命令行、矩阵与证据契约

**Files:**
- Modify: `tests/Full.NET.UnitTests/Performance/MixedLoadContractTests.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadOptions.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadReportWriter.cs`

- [x] 先增加失败测试，固定仅显式启用的 `off,on` profile、历史种子量、清理批大小/间隔、重复参数与边界拒绝。
- [x] 增加 Worker/cleanup 样本与统计模型；A/B 门禁要求请求 P99 和 Worker P99 相对退化不超过 20%，低延迟档使用请求 100ms/Worker 250ms 保护带，Worker/cleanup 错误为 0，开启清理时必须实际删除记录。
- [x] 执行：

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~MixedLoadContractTests
```

### Task 2: 在真实混合负载中运行 Outbox drain 与 retention

**Files:**
- Modify: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadRunner.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadReportWriter.cs`

- [x] 每个 provider/concurrency/profile 启动独立数据库，直接预置过期成功终态历史；预置只属于 setup，不计入采样。
- [x] 在 measurement 期间启动有界单消费者 drain loop；`on` profile 同时通过 `IOutboxRetentionStore` 按生产单轮批次上限清理。
- [x] 采集 enqueue 所在真实 HTTP 请求延迟、Acquire/MarkProcessed Worker 延迟、cleanup 延迟/删除吞吐/错误、最终 Outbox 分类计数。
- [x] 扩展数据库快照：SQL Server 记录 log bytes flushed 与锁等待；MySQL 记录 `Innodb_os_log_written`、row-lock wait 和 history list。MySQL 诊断使用隔离容器 root 连接，业务负载仍使用最小权限账号；指标读取失败显式标记证据不完整。
- [x] 原始 Worker/cleanup 样本先以 NDJSON checkpoint 落盘，再从内存释放；JSON 与 Markdown 报告包含每一档及 off/on 对比结论。

### Task 3: 双库短矩阵、文档和受影响验证

**Files:**
- Modify: `docs/verification/outbox-retention-2026-07-29.md`
- Modify: `docs/superpowers/plans/2026-07-28-production-performance-hardening.md`

- [x] 先运行 SQL Server/MySQL、并发 1、短预热/短采样 smoke，确认两种 profile 都产生请求、Worker、清理和资源证据。
- [x] 再运行并发 `1,4` 的短 A/B；记录命令、环境、原始工件位置、P95/P99、吞吐、锁等待、日志/undo、结论与停止条件。
- [x] 执行受影响测试、Release 构建和任务基线影响选择：

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~MixedLoadContractTests
dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release
pnpm test:integration:affected:plan -- --base 0f434fd
pnpm test:integration:affected -- --base 0f434fd
git diff --check
git status --short --branch
```

- [x] 完成规则与 Skill 遗漏复盘；双库 A/B 证据完整且门禁通过，Task 23 Step 3 已关闭。规则无新增，两个项目 Skill reference 仅机械同步 canonical Unit `505 → 509`。
