# Worker Native AOT Phase 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 SQL Server 与 MySQL 上验证 linux-x64 Native Worker 对 Legacy Outbox 的领取、MemoryPack Handler 调度、成功确认和损坏载荷死信终态。

**Architecture:** JIT 测试夹具在迁移后的隔离数据库写入一条合法 Notifications 公告事件和一条同路由损坏载荷，随后启动正常 `LegacyPolling` 原生 Worker。测试按消息 Id 等待确定终态，要求合法消息首次尝试即成功，损坏消息首次尝试即进入稳定 `outbox.invalid_payload` 死信，最后通过 SIGTERM 正常停止。

**Tech Stack:** .NET 10 Native AOT、MSTest、Dapper、MemoryPack、SQL Server、MySQL、GitHub Actions。

## Global Constraints

- 不修改 Outbox SQL、状态机、默认并发、重试策略、消息契约或数据库结构。
- 测试消息为 Host 作用域，合法事件使用生产 `MemoryPackIntegrationEventSerializer` 和稳定 Notifications 路由。
- Realtime 保持关闭，由 `NullRealtimePublisher` 消除外部 Redis 依赖；不把本测试表述为 SignalR 投递证据。
- 本地非 Linux 只验证编译和 6 项发现门禁；真实结果必须来自 Linux 双库 CI。

---

### Task 1: 建立双库 Legacy Outbox RED

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerSqlServerE2ETests.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerMySqlE2ETests.cs`

**Interfaces:**
- Produces: `VerifyLegacyOutboxDeliveryAsync(DatabaseProvider, string, CancellationToken)` 调用契约。

- [x] **Step 1:** SQL Server/MySQL 各添加一项 `processes_legacy_outbox_terminal_states` 测试。
- [x] **Step 2:** Release build 必须因断言方法不存在而产生精确 `CS0117`。

### Task 2: 实现消息准备与终态断言

**Files:**
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerOutboxProbe.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerE2EAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerProcessHost.cs`

**Interfaces:**
- Produces: `NativeWorkerOutboxProbe.EnqueueAsync(...)` 与 `WaitForTerminalStatesAsync(...)`。

- [x] **Step 1:** 使用生产 MemoryPack 序列化器写入合法公告事件，使用相同路由/ContentType 写入损坏载荷。
- [x] **Step 2:** 启动常驻 Worker，按两个 UUID 等待终态，拒绝仅凭日志判断成功。
- [x] **Step 3:** 断言合法消息 `Attempts=1`、已处理且非死信；损坏消息 `Attempts=1`、未处理且死信原因为 `outbox.invalid_payload`。
- [x] **Step 4:** SIGTERM 后要求退出码 0，日志无轮询、容量、可重试消息或 AOT 致命故障。

### Task 3: 更新门禁和证据

**Files:**
- Modify: `eng/testing/test-matrix.json`
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotLinuxPublishRulesTests.cs`
- Modify: `docs/architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md`
- Create: `docs/verification/2026-08-29-worker-native-aot-phase3.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Produces: Worker Native AOT 最低发现数 6；状态继续保持 `Analysis-only` 直至 Linux CI。

- [x] **Step 1:** 更新矩阵、Architecture 与能力边界文档。
- [x] **Step 2:** 运行 Worker E2E、Architecture、analyzer、affected inner、Governance 和分片一致性。
- [x] **Step 3:** 独立审查后检查 `git diff --check`、工作区和分支状态，不提交未获授权改动。
