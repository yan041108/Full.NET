# Worker Native AOT Phase 4 Implementation Plan

> **For agentic workers:** Execute each checkbox in order and preserve the Linux CI evidence boundary.

**Goal:** 在 SQL Server 与 MySQL 上验证 linux-x64 Native Worker 自动领取 Host 级 Ping Job，并写入完整成功终态。

**Architecture:** JIT 测试夹具在迁移后的隔离数据库中直接写入一个启用的 `ping` 定义和一条 Pending 手动执行记录，随后启动正常 `LegacyPolling` 原生 Worker。测试只按执行 Id 等待数据库终态，要求首次领取成功、时间戳完整，并清空租约、重试和错误字段，最后通过 SIGTERM 正常停止。

**Tech Stack:** .NET 10 Native AOT、MSTest、Dapper、SQL Server、MySQL、GitHub Actions。

## Global Constraints

- 不修改 Jobs SQL、状态机、默认并发、租约、重试策略、数据库结构或业务 API。
- 测试使用生产内置 `PingJobExecutor` 的稳定 HandlerKind，定义与执行均为 Host 作用域。
- 本地非 Linux 只验证编译和 8 项发现门禁；真实结果必须来自 Linux 双库 CI。
- 本阶段不覆盖失败重试、租约续期、崩溃恢复、多 Worker 竞争、计划调度或容量。

---

### Task 1: 建立双库 Jobs RED

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerSqlServerE2ETests.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerMySqlE2ETests.cs`

**Interfaces:**
- Produces: `VerifyJobsPingExecutionAsync(DatabaseProvider, string, CancellationToken)` 调用契约。

- [x] **Step 1:** SQL Server/MySQL 各添加一项 `processes_pending_ping_job` 测试。
- [x] **Step 2:** Release build 必须因断言方法不存在而精确失败。

### Task 2: 实现 Jobs 准备与终态断言

**Files:**
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerJobsProbe.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerE2EAssertions.cs`

**Interfaces:**
- Produces: `NativeWorkerJobsProbe.EnqueuePingAsync(...)` 与 `WaitForSucceededAsync(...)`。

- [x] **Step 1:** 双库写入 Host 级启用 Ping 定义与 Pending 手动执行。
- [x] **Step 2:** 启动常驻 Worker，按执行 UUID 等待数据库终态。
- [x] **Step 3:** 断言 `Succeeded`、`AttemptCount=1`、开始/结束时间非空，且错误、租约和重试字段均为空。
- [x] **Step 4:** SIGTERM 后要求退出码 0，日志无 Jobs/AOT 致命故障。

### Task 3: 更新门禁和证据

**Files:**
- Modify: `eng/testing/test-matrix.json`
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotLinuxPublishRulesTests.cs`
- Modify: `docs/architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md`
- Create: `docs/verification/2026-08-29-worker-native-aot-phase4.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Produces: Worker Native AOT 最低发现数 8；状态继续保持 `Analysis-only` 直至 Linux CI。

- [x] **Step 1:** 更新矩阵、Architecture 与能力边界文档。
- [x] **Step 2:** 运行 Worker E2E、Architecture、analyzer、affected inner、Governance 和分片一致性。
- [x] **Step 3:** 独立审查后检查 `git diff --check`、工作区和分支状态，不提交未获授权改动。
