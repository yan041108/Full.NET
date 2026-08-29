# Worker Native AOT Phase 0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 Files AOT 静态参数门禁假绿，并让 `Full.NET.Host.Worker` 完成独立 Native AOT analysis 静态闭包，不在本阶段声明 Linux 原生发布完成。

**Architecture:** Host.Api 已完成的 Dapper AOT Registry 继续作为共享执行机制；Worker Profile 在模块 `AddBackgroundServices` 中同步注册实际后台路径需要的 binder/materializer。Worker 自身使用固定 SQL 参数和源生成 JSON，并通过独立 analyzer 命令验证完整引用闭包。正式 publish、外部进程 E2E 与 Migrator 留给后续阶段。

**Tech Stack:** .NET 10、Native AOT analyzers、System.Text.Json source generation、Dapper 静态 Registry、MSTest Architecture tests、pnpm/Node 验证脚本。

## Global Constraints

- 不改变 Host.Api、Worker 的业务语义、Outbox 状态机、租户边界或双库 SQL。
- 禁止匿名 SQL 参数、反射式 JSON fallback、通配 linker root、`NoWarn=IL*` 或无依据 suppression。
- 本阶段只声明 `Worker Aot-analysis-clean`；不声明 `Aot-published`、Provider verified 或生产容量。
- Worker/Migrator 属于独立运行角色；Migrator 不进入本计划。

---

### Task 1: 固化 Worker Native AOT 决策边界

**Files:**
- Create: `docs/architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md`
- Create: `docs/verification/2026-08-29-worker-native-aot-phase0.md`

**Interfaces:**
- Consumes: ADR-0008/ADR-0009 的 Host.Api 状态边界。
- Produces: Worker Phase 0 的允许范围、完成状态和后续 publish 停止条件。

- [x] **Step 1:** 写入 ADR，明确 analysis-only、双库后台路径与禁止外推边界。
- [x] **Step 2:** 建立验证记录骨架，只记录本轮实际执行结果。

### Task 2: 修复 Files 匿名 SQL 参数门禁假绿

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Cleanup/DeletedHostFileBlobCleanupRunner.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReconciliationRunner.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReferenceClaimReconciliationRunner.cs`

**Interfaces:**
- Consumes: `ContainsAnonymousSqlParameterObject(string)`。
- Produces: Files 全模块统一的换行安全匿名参数扫描和固定 `IReadOnlyDictionary<string, object?>` 参数。

- [x] **Step 1:** 将 Files 测试改为对所有源文件调用统一正则 helper。
- [x] **Step 2:** 运行该测试并确认因三个 Runner 失败。
- [x] **Step 3:** 将 Runner 查询和更新参数改为显式字典，保持参数名和值不变。
- [x] **Step 4:** 重跑测试并确认通过。

### Task 3: 建立 Worker 自身 SQL 与 JSON 静态闭包

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/ShadowEventComparisonProcessor.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/Program.cs`
- Create: `src/Hosts/Full.NET.Host.Worker/WorkerJsonSerializerContext.cs`
- Create: `src/Hosts/Full.NET.Host.Worker/WorkerDapperAotRegistration.cs`

**Interfaces:**
- Consumes: `DapperAotMaterializerRegistry`、`AotDataReaderExtensions`、`JsonTypeInfo<T>`。
- Produces: `WorkerErrorResponse`、`WorkerJsonSerializerContext` 和 `WorkerDapperAotRegistration.Register()`。

- [x] **Step 1:** 添加 Architecture RED，拒绝 Worker 匿名 SQL 参数和运行时 `JsonSerializerOptions`。
- [x] **Step 2:** 运行测试并确认同时命中 Shadow SQL 与 Program JSON。
- [x] **Step 3:** 将 Shadow 参数改为固定字典，使 fingerprint row 可由 Worker 静态注册。
- [x] **Step 4:** 添加 Worker JSON context，并让 Program 使用生成的 `JsonTypeInfo`。
- [x] **Step 5:** 在 Worker 启动、首个数据库请求前调用 Worker Dapper AOT 注册。
- [x] **Step 6:** 重跑 Architecture 测试并确认通过。

### Task 4: 让 Worker Profile 注册模块后台物化器

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`
- Modify: each SQL-backed module `*Module.cs` that implements `AddBackgroundServices`

**Interfaces:**
- Consumes: 各模块既有 `*DapperAotMaterializerContributor`。
- Produces: Worker Profile 首次 SQL 前同步完成的模块 binder/materializer 注册。

- [x] **Step 1:** 添加 Architecture RED，枚举 Worker SQL 模块并要求后台注册 contributor。
- [x] **Step 2:** 运行测试并确认因当前注册只存在于 API `AddServices` 而失败。
- [x] **Step 3:** 在各模块后台注册入口的 `FULLNET_AOT_COMPILE` 分支调用既有 contributor；重复注册必须保持幂等。
- [x] **Step 4:** 重跑 Architecture 测试并确认通过。

### Task 5: 增加 Worker AOT analyzer 入口并关闭真实告警

**Files:**
- Inspect: `src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj`（共享 `Directory.Build.targets` 已提供分析属性，无需项目级重复配置）
- Create: `scripts/testing/run-worker-aot-analyzers.mjs`
- Modify: `package.json`
- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`

**Interfaces:**
- Consumes: `FullNetAotAnalysis=true` 和 `Directory.Build.targets` 的 `FULLNET_AOT_COMPILE`。
- Produces: `pnpm test:aot:worker:analyzers`。

- [x] **Step 1:** 添加 Architecture RED，要求独立 Worker analyzer 命令和脚本。
- [x] **Step 2:** 运行测试确认失败。
- [x] **Step 3:** 添加与 API analyzer 隔离的 Worker build/恢复 JIT 产物脚本及 package 命令。
- [x] **Step 4:** 运行 Worker analyzer；只修自有代码真实告警，不添加通配 suppression。
- [x] **Step 5:** 重跑 API analyzer，确认 Worker 改造不破坏 Host.Api 闭包。

### Task 6: 分层验证和证据收口

**Files:**
- Modify: `docs/verification/2026-08-29-worker-native-aot-phase0.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Consumes: fresh analyzer、Architecture、inner/governance 输出。
- Produces: 精确的 `Worker Aot-analysis-clean` 或未关闭阻塞项。

- [x] **Step 1:** 运行 Native AOT Architecture 选择器、Worker/API analyzers。
- [x] **Step 2:** 审查 affected plan 并运行 `pnpm test:inner -- --snapshot worker-native-aot-phase0-20260829`；Docker 不可用导致双库用例未进入产品代码，按未验证记录。
- [x] **Step 3:** 运行 governance、`git diff --check` 和最终状态检查。
- [x] **Step 4:** 更新验证记录和能力矩阵，只声明实际完成范围。
