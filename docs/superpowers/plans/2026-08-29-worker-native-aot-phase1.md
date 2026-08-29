# Worker Native AOT Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `Full.NET.Host.Worker` 建立独立 linux-x64 Native AOT publish 和 SQL Server/MySQL 一次性外部进程 E2E，证明最小数据库、Dapper 物化与 JSON 退出链路。

**Architecture:** 复用既有 Native AOT SDK 镜像与 publish warning 门禁，在共享发布脚本中按显式 Host 选择隔离契约和产物目录。E2E 通过 JIT Migrator 准备双库，再运行 Worker 的 Outbox 版本退役扫描命令，断言稳定 JSON、退出码和无 AOT 致命标记。持续轮询、Kafka/CDC、Jobs 自动领取与容量仍属于后续 Phase。

**Tech Stack:** .NET 10 Native AOT、Node.js/pnpm、MSTest、Testcontainers SQL Server/MySQL、GitHub Actions。

## Global Constraints

- 默认 Worker JIT 部署保持不变，本阶段只新增构建与验证门禁。
- Worker 与 Host.Api 产物、manifest、日志和 TRX 必须使用隔离目录。
- 数据库 schema 只由 JIT Migrator 准备，不把 Migrator 纳入 Native AOT。
- 双库 E2E 必须运行同一命令和断言，不改变 Outbox 状态机或可靠性语义。
- 本地 Docker 不可用时只允许验证发现和静态门禁，不得声明 `Worker Aot-published`。

---

### Task 1: 固化 Worker publish 与 E2E 契约

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotLinuxPublishRulesTests.cs`
- Modify: `tests/governance/native-aot-publish.test.mjs`
- Modify: `eng/testing/test-matrix.json`

**Interfaces:**
- Produces: `workerNativeAotPublish`、`workerNativeAotIntegration` 机器门禁。

- [x] **Step 1:** 添加失败的 Architecture 断言，要求 Worker 独立命令、产物路径、双库发现数和 CI 工作流；既有 Governance publish 门禁继续覆盖共享脚本。
- [x] **Step 2:** 运行聚焦测试，确认因契约与文件缺失而失败。
- [x] **Step 3:** 添加矩阵节点，固定 `linux-x64`、Worker executable、两个双库测试与独立超时。

### Task 2: 扩展共享 Linux Native AOT publish

**Files:**
- Modify: `scripts/testing/api-native-aot-publish-contract.mjs`
- Modify: `scripts/testing/run-api-aot-publish-linux.mjs`
- Modify: `package.json`

**Interfaces:**
- Produces: `workerNativeAotPublishContract`、`pnpm test:aot:worker:publish:linux`。

- [x] **Step 1:** 在现有契约模块增加 Worker 项目、隔离输出、manifest、log 和 executable。
- [x] **Step 2:** 让发布脚本接受显式 `--host api|worker`，旧 API 命令保持兼容默认值。
- [x] **Step 3:** 运行 Governance/Architecture，使发布契约门禁转绿。

### Task 3: 建立 Worker 双库原生外部进程断言

**Files:**
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerArtifactLocator.cs`
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerProcessRunner.cs`
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerE2EAssertions.cs`
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerSqlServerE2ETests.cs`
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerMySqlE2ETests.cs`
- Create: `scripts/testing/run-native-aot-worker-e2e.mjs`

**Interfaces:**
- Consumes: JIT `NativeApiDatabaseBootstrap` 与 Worker 退役扫描 CLI。
- Produces: `pnpm test:aot:worker:native:e2e`、SQL Server/MySQL 两项发现门禁。

- [x] **Step 1:** 添加两个平台受控测试；非 Linux 或缺产物时 Inconclusive，Linux 门禁必须实际执行。
- [x] **Step 2:** 启动原生 Worker，传入数据库环境与固定 Notifications 事件路由，捕获 stdout/stderr/退出码。
- [x] **Step 3:** 断言退出码 0、`outbox.version_retirement.safe`、路由/版本/计数和无反射/AOT 致命标记。
- [x] **Step 4:** 在 Windows 运行 runner，确认发现两项而不把 skip 当作原生通过。

### Task 4: 接入独立 Linux CI 并收口证据

**Files:**
- Create: `.github/workflows/worker-native-aot-linux.yml`
- Modify: `docs/architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md`
- Create: `docs/verification/2026-08-29-worker-native-aot-phase1.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Produces: Worker publish/analyzer/Architecture/E2E 的可重复 CI，但只有真实 Linux 双库运行通过后才能升级状态。

- [x] **Step 1:** CI 顺序固定为 analyzer → publish → Architecture → Integration build → Worker E2E。
- [x] **Step 2:** 上传 Worker manifest、executable、publish log、TRX 和进程日志。
- [x] **Step 3:** 本地运行 analyzer、Architecture、Governance、测试发现、affected plan 与可执行的影响集。
- [x] **Step 4:** 验证记录明确本机 Docker 阻塞；在 CI 成功前保持 `Build-verified / Analysis-only`，不声明 `Aot-published`。
