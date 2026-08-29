# Worker Native AOT Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 SQL Server 与 MySQL 上验证 linux-x64 Native Worker 的正常常驻启动、后台空载轮询、Jobs 心跳和 SIGTERM 优雅退出。

**Architecture:** 保留 Phase 1 一次性退役扫描，新增独立的常驻进程测试宿主。测试由 JIT Migrator 准备 schema，启动原生 Worker 的 `LegacyPolling` 模式，通过 `/health/live` 判定启动完成，通过 `fn_jobs_worker_instance` 心跳证明 Jobs 后台循环至少执行一轮，最后发送 SIGTERM 并断言正常退出和日志无原生/AOT/后台迭代故障标记。

**Tech Stack:** .NET 10 Native AOT、MSTest、Testcontainers SQL Server/MySQL、ASP.NET Core health endpoint、Dapper、GitHub Actions。

## Global Constraints

- 默认 Worker JIT 部署、Outbox/Jobs 可靠性语义和默认并发保持不变。
- 数据库 schema 只由 JIT Migrator 准备；原生 Worker 只执行生产常驻路径。
- 测试只验证空载持续运行与 Jobs 心跳，不把它表述为消息处理、Files 启用态或容量证据。
- 本地非 Linux 只验证测试发现和编译；真实通过必须来自 Linux 双库 CI。

---

### Task 1: 固化常驻 Worker 双库测试契约

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerSqlServerE2ETests.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerMySqlE2ETests.cs`
- Modify: `eng/testing/test-matrix.json`
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotLinuxPublishRulesTests.cs`

**Interfaces:**
- Produces: SQL Server/MySQL 各一项 `persistent_background_runtime` 测试，Worker Native AOT 最低发现数为 4。

- [x] **Step 1:** 添加调用 `VerifyPersistentRuntimeAsync` 的双库失败测试。
- [x] **Step 2:** 运行 Worker E2E 构建，确认因常驻断言尚不存在而失败。
- [x] **Step 3:** 更新测试矩阵与 Architecture 发现数门禁。

### Task 2: 实现原生 Worker 常驻进程宿主

**Files:**
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerProcessHost.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerE2EAssertions.cs`

**Interfaces:**
- Produces: `NativeWorkerProcessHost.StartAsync(...)`、`StopGracefullyAsync(...)`、`AssertNoFatalMarkersInLogs()`。

- [x] **Step 1:** 使用隔离端口、Worker content root 和低频测试配置启动原生产物并持续泵送 stdout/stderr。
- [x] **Step 2:** 轮询 `/health/live`，若提前退出或超时则附带日志尾部失败。
- [x] **Step 3:** 查询 `fn_jobs_worker_instance`，等待 Host Worker 心跳出现，证明 Jobs 循环至少完成一次数据库写入。
- [x] **Step 4:** Linux 发送 SIGTERM，等待退出码 0；失败或取消时终止进程树并保留日志。

### Task 3: 收口门禁与证据

**Files:**
- Modify: `docs/architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md`
- Create: `docs/verification/2026-08-29-worker-native-aot-phase2.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Produces: Phase 2 常驻空载闭包证据；仍不升级为完整 `Aot-published`。

- [x] **Step 1:** 运行 Worker E2E 发现门禁、Architecture、analyzer、affected inner 与 Governance。
- [x] **Step 2:** 文档明确覆盖和未覆盖边界，等待 Linux CI 真实双库结果。
- [x] **Step 3:** 检查 `git diff --check`、工作区和分支状态，不提交未获授权的改动。
