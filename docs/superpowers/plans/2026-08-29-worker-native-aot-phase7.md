# Worker Native AOT Phase 7 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Files Reference Claim Reconciliation 建立 SQL Server/MySQL 原生 Worker 外部进程门禁，证明真实 Document 引用晋升为 `active`，超龄孤儿 Claim 释放为 `released`。

**Architecture:** 测试先由 JIT Migrator 建立隔离数据库，再用低层 Dapper fixture 写入一个 Ready 文件、一个 Document Item/Version 和两条超龄 Pending Claim。启动已发布的 Linux Native Worker 时显式关闭 Files Upload Reconciliation 与 Cleanup，只开启 Reference Claim Reconciliation；测试轮询数据库确定两个终态并检查进程优雅退出和致命日志。

**Tech Stack:** .NET 10、MSTest、Dapper、SQL Server、MySQL Binary16、Native AOT linux-x64、PowerShell/Node.js 治理门禁。

## Global Constraints

- 基线提交固定为 `0f4a2ee10380569626880ab8ab30f7e4a523c0ee`。
- 不修改生产状态机、SQL、数据库结构、API 或消息契约。
- MySQL 测试连接必须使用 `MySqlGuidStorageMode.Binary16`，UTC 参数写入 `DateTime`。
- 外部进程场景必须显式覆盖三个 Files 后台开关，避免生产默认值污染测试。
- 本机缺少 Linux 原生产物时测试只能报告 Inconclusive，不能声明 `Aot-published`。
- Worker 状态保持 `Analysis-only / Worker Aot-analysis-clean`，直到 Linux CI 双库证据成功。

---

### Task 1: 建立 Phase 7 红测

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerSqlServerE2ETests.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerMySqlE2ETests.cs`

**Interfaces:**
- Consumes: `SharedDatabaseFixture.CreateSqlServerDatabaseAsync()`、`CreateMySqlDatabaseAsync()`。
- Produces: 对 `NativeWorkerE2EAssertions.VerifyFilesReferenceClaimReconciliationAsync(DatabaseProvider, string, CancellationToken)` 的两个调用点。

- [x] **Step 1: 写入 SQL Server 与 MySQL 测试方法**

  两个方法分别命名为 `SqlServer_native_worker_reconciles_pending_file_reference_claims` 与 `MySql_native_worker_reconciles_pending_file_reference_claims`，沿用 Artifact 不可用时 Inconclusive 的现有模式。

- [x] **Step 2: 运行 Release 构建验证红测**

  Run: `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore`

  Expected: 仅因 `VerifyFilesReferenceClaimReconciliationAsync` 不存在产生两个 `CS0117`，且无 warning。

### Task 2: 实现双库 fixture 与进程开关

**Files:**
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerFilesReferenceClaimProbe.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerProcessHost.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerE2EAssertions.cs`

**Interfaces:**
- Consumes: `NativeWorkerProcessHost.StartAsync(...)`、`NativeApiDatabaseBootstrap.BootstrapAsync(...)`。
- Produces: `NativeWorkerFilesReferenceClaimProbe.PrepareAsync(...)`、`WaitForTerminalStatesAsync(...)` 与 `VerifyFilesReferenceClaimReconciliationAsync(...)`。

- [x] **Step 1: 新增 Reference Claim fixture**

  写入 Ready 文件、Document Item/Version、真实 Pending Claim 和孤儿 Pending Claim；两条 Claim 均早于 `MinimumAgeSeconds`，孤儿 Claim 同时早于 `ReleaseGraceSeconds`。等待结果要求真实 Claim 为 `active` 且具有 `ConfirmedAtUtc`，孤儿 Claim 为 `released` 且具有 `ReleasedAtUtc`。

- [x] **Step 2: 隔离宿主配置**

  在 `StartAsync` 增加 `enableFilesReferenceClaimReconciliation = false`，并无条件写入 `Files__ReferenceClaimReconciliation__Enabled`。启用场景使用 `BatchSize=10`、`MaxBatchesPerRun=1`、`MinimumAgeSeconds=30`、`ReleaseGraceSeconds=60`、`PollSeconds=5`。

- [x] **Step 3: 新增端到端断言**

  Bootstrap 后准备 fixture，启动仅启用 Reference Claim 的原生 Worker，等待两类终态，SIGTERM 后断言退出码为 0 且不存在致命日志。

- [x] **Step 4: 运行 Release 构建验证绿测**

  Run: `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore`

  Expected: 0 warning、0 error。

### Task 3: 收紧门禁并记录证据

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotLinuxPublishRulesTests.cs`
- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md`
- Modify: `docs/roadmap/capability-status.md`
- Create: `docs/verification/2026-08-29-worker-native-aot-phase7.md`

**Interfaces:**
- Consumes: 新增双库测试方法与 Probe 文件。
- Produces: `workerNativeAotIntegration.minimum = 14`、Architecture 必需文件/方法断言、Phase 7 状态与验证记录。

- [x] **Step 1: 更新自动化门槛**

  把 Worker Native AOT Integration 最低发现数从 12 提高到 14；把 Integration `infrastructure` 分区从 155 提高到 157，总数从 647 提高到 649，并让 Architecture Test 锁定新 Probe 和双库方法名。

- [x] **Step 2: 更新 ADR 与能力状态**

  ADR 增加 Phase 7 的已覆盖/未覆盖边界；能力表保持 Analysis-only，并链接 Phase 7 Verification。

- [x] **Step 3: 运行受影响验证**

  Run:

  - `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-restore --filter FullyQualifiedName~WorkerNativeAot_HasIsolatedPublishAndDualDatabaseE2EGates`
  - `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-build --filter FullyQualifiedName~NativeWorker --logger "console;verbosity=normal"`
  - `pnpm test:aot:worker:analyzers`
  - `pnpm test:integration:partitions`
  - `pnpm test:inner -- --base 0f4a2ee10380569626880ab8ab30f7e4a523c0ee`
  - `git diff --check`

  Expected: Architecture 通过；本机无 Linux Artifact 时 14 项全部 Inconclusive；analyzer 脚本的 AOT 分析与强制 JIT 重建均为 0 warning/0 error；分区共 649 且无遗漏/重复；inner 通过。

- [x] **Step 4: 写入 Verification 并完成提交准备**

  Verification 必须记录新鲜命令输出、环境缺口和 `Analysis-only` 结论。提交信息使用 `test: verify Native AOT Files reference claims`。
