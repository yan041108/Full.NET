# Worker Native AOT Phase 5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 SQL Server 与 MySQL 上验证 linux-x64 Native Worker 对陈旧 Pending Host 文件执行本地 Blob 存在提升和缺失清理终态。

**Architecture:** JIT 测试夹具在隔离数据库写入两条 Host 级 Pending 文件记录，并只为其中一条在唯一临时 Files root 创建本地 Blob。测试通过显式环境覆盖让原生 Worker 使用同一 root，随后按两个文件 Id 等待 `ready` 与删除终态，并通过 SIGTERM 正常停止。

**Tech Stack:** .NET 10 Native AOT、MSTest、Dapper、本地文件 Provider、SQL Server、MySQL、GitHub Actions。

## Global Constraints

- 不修改 Files 生产 SQL、状态机、默认 Provider、轮询语义、数据库结构或业务 API。
- 测试记录均为 Host 作用域、`ProviderKey=local`、`StorageState=pending`，创建时间早于 30 秒准入阈值。
- 测试显式传入唯一临时 Files root，并在结束后只清理该精确目录。
- 本地非 Linux 只验证编译和 10 项发现门禁；真实结果必须来自 Linux 双库 CI。
- 本阶段不覆盖 `publishing` 保留、软删除 Blob Cleanup、Reference Claim 对账、S3 或容量。

---

### Task 1: 建立双库 Files RED

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerSqlServerE2ETests.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerMySqlE2ETests.cs`

**Interfaces:**
- Produces: `VerifyFilesUploadReconciliationAsync(DatabaseProvider, string, CancellationToken)` 调用契约。

- [x] **Step 1:** SQL Server/MySQL 各添加一项 `reconciles_pending_local_files` 测试。
- [x] **Step 2:** Release build 必须因断言方法不存在而产生精确 `CS0117`。

### Task 2: 实现 Files 探针和受控环境

**Files:**
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerFilesProbe.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerProcessHost.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerE2EAssertions.cs`

**Interfaces:**
- Produces: `NativeWorkerFilesProbe.PrepareAsync(...)`、`WaitForTerminalStatesAsync(...)` 与显式 Files root 的 `NativeWorkerProcessHost.StartAsync(...)` 重载。

- [x] **Step 1:** 同一事务写入两条陈旧 Pending 记录，只为有效记录创建本地 Blob。
- [x] **Step 2:** 原生 Worker 使用同一 Files root，`MinimumAgeSeconds=30`、`PollSeconds=5`。
- [x] **Step 3:** 按 UUID 断言有效记录变成 `ready` 且保留，缺失 Blob 的记录被删除。
- [x] **Step 4:** SIGTERM 后要求退出码 0，日志无 Files/AOT 致命故障，并清理精确临时目录。

### Task 3: 更新门禁和证据

**Files:**
- Modify: `eng/testing/test-matrix.json`
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotLinuxPublishRulesTests.cs`
- Modify: `docs/architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md`
- Create: `docs/verification/2026-08-29-worker-native-aot-phase5.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Produces: Worker Native AOT 最低发现数 10；状态继续保持 `Analysis-only` 直至 Linux CI。

- [x] **Step 1:** 更新矩阵、Architecture 与能力边界文档。
- [x] **Step 2:** 运行 Worker E2E、Architecture、analyzer、affected inner、Governance 和分片一致性。
- [x] **Step 3:** 独立审查后检查 `git diff --check`、工作区和分支状态；是否提交由当前授权决定。
