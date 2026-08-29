# Worker Native AOT Phase 6 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 SQL Server 与 MySQL 上验证 linux-x64 Native Worker 对已软删除 Host 文件执行本地 Blob 与元数据清理，并在 Provider 不可解析时保留墓碑。

**Architecture:** JIT 测试夹具在隔离数据库写入两条 Host 级已删除文件记录，其中本地 Provider 记录拥有测试独占 Blob，未知 Provider 记录用于验证失败隔离。原生 Worker 显式启用 `Files:Cleanup`，测试等待本地 Blob 和元数据均被删除，同时要求未知 Provider 墓碑保留，最后通过 SIGTERM 正常停止。

**Tech Stack:** .NET 10 Native AOT、MSTest、Dapper、本地文件 Provider、SQL Server、MySQL、GitHub Actions。

## Global Constraints

- 不修改 Files 生产 SQL、清理状态机、默认关闭语义、数据库结构或业务 API。
- 两条记录均为 Host 作用域且 `DeletedAtUtc IS NOT NULL`；成功记录使用 `ProviderKey=local`，失败隔离记录使用不可解析的测试 Provider 机器码。
- 只删除测试生成的随机叶目录；未知 Provider 墓碑必须保留，禁止回退到默认 Provider。
- 本地非 Linux 只验证编译和精确发现门禁；真实结果必须来自 Linux 双库 CI。
- 本阶段不覆盖未释放 Reference Claim、S3、并发软删除、容量或生产等价负载。

---

### Task 1: 建立双库 Cleanup RED

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerSqlServerE2ETests.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerMySqlE2ETests.cs`

**Interfaces:**
- Produces: `VerifyFilesDeletedBlobCleanupAsync(DatabaseProvider, string, CancellationToken)` 调用契约。

- [x] **Step 1:** SQL Server/MySQL 各添加一项 `cleans_deleted_local_files` 测试。
- [x] **Step 2:** 运行 Release build，确认仅因断言方法不存在产生两个 `CS0117`。

### Task 2: 实现 Cleanup 探针和受控环境

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerFilesProbe.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerProcessHost.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerE2EAssertions.cs`

**Interfaces:**
- Produces: `NativeWorkerFilesProbe.PrepareCleanupAsync(...)`、`WaitForCleanupTerminalStatesAsync(...)`，以及显式 `enableFilesCleanup` 的 `NativeWorkerProcessHost.StartAsync(...)`。

- [x] **Step 1:** 同一事务写入本地与未知 Provider 两条墓碑，仅为本地记录创建 Blob。
- [x] **Step 2:** 原生 Worker 使用同一 Files root，并显式设置 `Files:Cleanup:Enabled=true`、`PollSeconds=5`、有界批次。
- [x] **Step 3:** 断言本地记录与 Blob 均删除，未知 Provider 墓碑保留。
- [x] **Step 4:** SIGTERM 后要求退出码 0、日志无致命故障，并清理精确临时目录。

### Task 3: 更新门禁和证据

**Files:**
- Modify: `eng/testing/test-matrix.json`
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotLinuxPublishRulesTests.cs`
- Modify: `docs/architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md`
- Create: `docs/verification/2026-08-29-worker-native-aot-phase6.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Produces: Worker Native AOT 精确发现门禁增加两项；状态继续保持 `Analysis-only` 直至 Linux CI。

- [x] **Step 1:** 更新矩阵、Architecture 与能力边界文档。
- [x] **Step 2:** 运行 Worker E2E、Architecture、analyzer、affected inner、Governance 和分片一致性。
- [x] **Step 3:** 独立审查后检查 `git diff --check`、工作区和分支状态；是否提交由当前授权决定。
