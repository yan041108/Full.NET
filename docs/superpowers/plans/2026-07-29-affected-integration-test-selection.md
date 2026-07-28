# Affected Integration Test Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 提供跨任务窗口复用的 Integration 影响集选择入口，本地只测试实际受影响能力，完整 193 项只保留给 `main` CI 的并行分片。

**Architecture:** 新脚本从任务基线 Git ref 到当前工作区收集已提交、暂存、未暂存和未跟踪文件，映射为 tooling、模块双库过滤集、Smoke 或 migrations 分片；多个目标可以组合，但本地入口没有 full 分支。聚焦模式先构建当前 Integration 程序集，再用 MTP JSON 验证 SQL Server/MySQL 都存在，并把精确发现数作为执行下限。根 `AGENTS.md`、开发规则、README 和项目 Skill 统一要求记录任务基线并调用该入口，使新任务窗口自动命中。

**Tech Stack:** Node.js 24、Microsoft Testing Platform、PowerShell、pnpm、Git

**Status:** 已于 2026-07-29 完成；本地影响集与 `main` CI 193 项门禁边界已按项目所有者最新决策冻结。

## Global Constraints

- 不共享可变测试数据库，不跳过受影响的迁移恢复用例，不降低 `main` CI canonical 193 项门槛。
- Identity、Tenancy、Outbox、缓存运行对应双库影响集；共享宿主与未知服务端路径运行 Smoke；迁移运行 migrations；测试工具运行 tooling。
- 聚焦结果必须同时发现 SQL Server 与 MySQL，且使用当前源码新构建的 Release 程序集。
- 无法识别的服务端文件必须 fail-safe 进入 Smoke；只有文档、规则和纯客户端改动可以返回 `none`。
- 所有手写脚本注释使用中文，稳定命令和代码标识符使用英文。

---

### Task 1: 风险选择纯函数契约

**Files:**
- Create: `tests/testing/run-affected-integration.test.mjs`
- Create: `scripts/testing/run-affected-integration.mjs`

**Interfaces:**
- Produces: `classifyChangedPaths(paths)`，返回 `{ mode, moduleName, filter, reasons }`。
- Produces: `verifyFocusedDiscovery(tests)`，拒绝零发现、缺少 SQL Server 或缺少 MySQL。

- [ ] **Step 1: 写入失败契约**

测试覆盖：

```js
assert.equal(classifyChangedPaths(['docs/readme.md']).mode, 'none');
assert.equal(
  classifyChangedPaths([
    'src/Modules/Full.NET.Modules.Auditing/Persistence/AuditingQueries.cs'
  ]).mode,
  'focused'
);
assert.equal(
  classifyChangedPaths([
    'src/Modules/Full.NET.Modules.Identity/Security/AccessSessionValidator.cs'
  ]).mode,
  'focused'
);
assert.throws(
  () => verifyFocusedDiscovery([{ type: { typeName: 'AuditingApiMySqlTests' } }]),
  /SQL Server/
);
```

- [ ] **Step 2: 运行 RED**

Run: `node --test tests/testing/run-affected-integration.test.mjs`

Expected: FAIL，原因是 `run-affected-integration.mjs` 或导出函数尚不存在。

- [ ] **Step 3: 实现最小分类与发现校验**

分类器登记模块与共享能力影响集；多个模块组合多个过滤目标，Identity/Tenancy 使用各自双库过滤集，共享路径和未知服务端文件使用 Smoke，文档/客户端返回 `none`。

- [ ] **Step 4: 运行 GREEN**

Run: `node --test tests/testing/run-affected-integration.test.mjs`

Expected: PASS，所有分类与双 Provider 防漏跑契约通过。

### Task 2: Git 任务基线与真实执行入口

**Files:**
- Modify: `scripts/testing/run-affected-integration.mjs`
- Modify: `tests/testing/run-affected-integration.test.mjs`
- Modify: `package.json`

**Interfaces:**
- Produces: `collectChangedPaths({ baseRef })`，合并 merge-base 到 HEAD、暂存、未暂存和非噪声未跟踪文件。
- Produces: `pnpm test:integration:affected:plan -- --base <task-base-sha>`。
- Produces: `pnpm test:integration:affected -- --base <task-base-sha>`。

- [ ] **Step 1: 写 Git 收集与命令参数 RED 契约**

使用临时 Git 仓库验证提交后、暂存、未暂存和未跟踪文件均被发现，并验证 `.tmp/`、`.cache/` 和测试结果工件不会扩大验证范围。

- [ ] **Step 2: 运行 RED**

Run: `node --test tests/testing/run-affected-integration.test.mjs`

Expected: FAIL，原因是 Git 收集或 CLI 参数接口未实现。

- [ ] **Step 3: 实现任务基线收集与执行**

`--plan` 只输出路径、模式、原因和将执行的命令；执行模式先运行：

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --no-restore --nologo
```

聚焦模式再执行 JSON 发现、双 Provider 校验和带精确 `--minimum-expected-tests`、TRX 的测试；Smoke、migrations 和 tooling 使用各自已有入口，禁止调用 full。

- [ ] **Step 4: 运行 GREEN**

Run: `pnpm test:integration:tooling`

Expected: PASS，原有 TRX/分片测试和新增选择器测试全部通过。

### Task 3: 跨任务窗口强制命中

**Files:**
- Modify: `AGENTS.md`
- Modify: `rules/development-quality.md`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `.agents/skills/fullnet-performance-hardening/SKILL.md`
- Modify: `.agents/skills/fullnet-module-delivery/SKILL.md`
- Modify: `tests/governance/integration-test-feedback.test.mjs`
- Modify: `docs/verification/integration-feedback-speed-2026-07-29.md`

**Interfaces:**
- Consumes: `test:integration:affected:plan` 和 `test:integration:affected`。
- Produces: 所有新任务窗口在开始时记录 `git rev-parse HEAD`，完成时以该 SHA 运行受影响测试选择器。

- [ ] **Step 1: 写治理 RED 契约**

治理测试要求 package、根 `AGENTS.md`、开发规则和两个项目 Skill 同时包含受影响测试入口及任务基线约束。

- [ ] **Step 2: 运行 RED**

Run: `node --test tests/governance/integration-test-feedback.test.mjs`

Expected: FAIL，提示缺少 `test:integration:affected` 或任务基线规则。

- [ ] **Step 3: 更新规则、文档与 Skill**

明确任务开始记录基线、先运行 plan 审查影响集、再运行 affected；本地禁止 full，完整 193 项只由 `main` CI 执行。

- [ ] **Step 4: 运行 GREEN**

Run:

```powershell
pnpm test:governance
pnpm test:skills
```

Expected: 全部 PASS。

### Task 4: 新鲜验证与性能证据

**Files:**
- Modify: `docs/verification/integration-feedback-speed-2026-07-29.md`
- Modify: `docs/superpowers/plans/2026-07-28-production-performance-hardening.md`

**Interfaces:**
- Consumes: 当前实现与任务基线。
- Produces: 聚焦墙钟、tooling 结果、未改变隔离语义和 Task 28 进度。

- [ ] **Step 1: 验证计划模式**

Run:

```powershell
pnpm test:integration:affected:plan -- --base 0599205
```

Expected: 测试基础设施改动判定为 `integration-tooling`。

- [ ] **Step 2: 验证聚焦真实运行**

在临时 Git 基线或脚本测试夹具中以 Auditing 单模块路径运行选择器，预期发现并通过 SQL Server/MySQL 共 6 项，生成 TRX。

- [ ] **Step 3: 验证本地影响集**

Run: `pnpm test:integration:affected -- --base 0599205`

Expected: 只执行 Integration tooling 与治理契约；不启动 193 项全量。

- [ ] **Step 4: 完成静态与仓库检查**

Run:

```powershell
pnpm test:integration:tooling
pnpm test:integration:partitions
pnpm test:governance
pnpm test:skills
git diff --check
git status --short --branch
```

Expected: 全部通过；只保留本计划 owned 文件和原有未跟踪本地工件。
