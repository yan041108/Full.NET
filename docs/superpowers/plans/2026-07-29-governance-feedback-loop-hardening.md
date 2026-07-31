# Governance Feedback Loop Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Full.NET 的日常验证改为功能优先的分层反馈环，并消除测试门槛、规则复盘和 Skill 候选的重复维护。

**Architecture:** affected selector 以 Git 任务快照隔离任务开始前的脏工作区，通过 `inner`、`slice`、`merge` 三阶段选择验证时机，并将多个模块过滤器合并为一次测试进程。测试数量和分片配置集中到一个机器清单，规则与 Skill 只引用稳定命令；治理演进改为证据触发，普通功能不再机械产生永久文档。

**Tech Stack:** Node.js 24、Microsoft Testing Platform、Git、pnpm、GitHub Actions、Markdown

**Status:** 已于 2026-07-29 完成实现与复核；完整 Integration 继续由 `main` CI 执行。

## Global Constraints

- 保留 SQL Server/MySQL 双 Provider、安全、租户、事务、Outbox 和迁移的 fail-safe 验证。
- 本地入口不得执行完整 Integration；完整集合只由 `main` CI 互斥分片执行。
- 不覆盖任务开始前的业务改动，不清理用户工作区和本地性能工件。
- 行为变更必须先建立失败测试，脚本注释使用中文。

---

### Task 1: 任务快照与路径分类

**Files:**
- Modify: `tests/testing/run-affected-integration.test.mjs`
- Modify: `scripts/testing/run-affected-integration.mjs`
- Modify: `package.json`

**Interfaces:**
- Produces: `createTaskSnapshot({ cwd, id })`，把任务开始时的基线和脏文件哈希保存到 `.git/fullnet-task-snapshots/`。
- Produces: `collectChangedPaths({ snapshotId, cwd })`，排除任务开始前未改变的脏文件。
- Produces: `pnpm test:task:start` 与 `--snapshot <id>`。

- [x] **Step 1: 写入快照、`App_Data` 排除和 API assertion 模块映射失败测试**
- [x] **Step 2: 运行 `node --test tests/testing/run-affected-integration.test.mjs`，确认因接口或行为缺失失败**
- [x] **Step 3: 实现最小快照存取、哈希比较与路径分类**
- [x] **Step 4: 重跑测试并确认通过**

### Task 2: 分层阶段与聚焦目标合并

**Files:**
- Modify: `tests/testing/run-affected-integration.test.mjs`
- Modify: `scripts/testing/run-affected-integration.mjs`

**Interfaces:**
- Produces: `--phase inner|slice|merge`，默认 `slice`。
- Produces: `combineFilterTargets(targets, discoveries)`，按 UID 去重并生成一个 OR 过滤器。
- Produces: 计划输出中的目标集合和测试矩阵静态耗时预算。

- [x] **Step 1: 写入阶段选择、迁移分级、UID 去重和预算失败测试**
- [x] **Step 2: 运行 selector 测试确认 RED**
- [x] **Step 3: 实现 inner 高风险即时门禁、slice affected 和 merge smoke 组合**
- [x] **Step 4: 重跑 selector 与 Integration tooling 测试确认 GREEN**

### Task 3: 测试矩阵唯一事实源

**Files:**
- Create: `eng/testing/test-matrix.json`
- Create: `scripts/testing/run-dotnet-test-suite.mjs`
- Create: `tests/testing/test-matrix.test.mjs`
- Modify: `scripts/testing/run-integration-shard.mjs`
- Modify: `package.json`
- Modify: `.github/workflows/ci.yml`
- Modify: `tests/governance/agents-rules-consistency.test.mjs`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`

**Interfaces:**
- Produces: canonical suite、Integration 分片、最小发现数和超时的 JSON 清单。
- Produces: `pnpm test:dotnet:unit|compatibility|architecture`。

- [x] **Step 1: 写入清单结构、分片覆盖和治理唯一来源失败测试**
- [x] **Step 2: 运行 testing/governance 测试确认 RED**
- [x] **Step 3: 让测试脚本和 CI 读取清单，文档只保留稳定命令**
- [x] **Step 4: 重跑 testing/governance 测试确认 GREEN**

### Task 4: 治理触发与文档预算

**Files:**
- Modify: `AGENTS.md`
- Modify: `rules/README.md`
- Modify: `rules/development-quality.md`
- Modify: `rules/rule-evolution.md`
- Modify: `rules/skill-evolution.md`
- Modify: `tests/governance/agents-rules-consistency.test.mjs`
- Modify: `tests/governance/integration-test-feedback.test.mjs`
- Modify: `.agents/skills/fullnet-performance-hardening/SKILL.md`
- Modify: `.agents/skills/fullnet-performance-hardening/references/performance-map.md`

**Interfaces:**
- Produces: inner/slice/merge/main 四层门禁。
- Produces: Spec、Plan、Verification 和规则/Skill 演进的触发门槛。
- Produces: 无触发时的一行治理结论，不再更新候选计数或测试门槛审计。

- [x] **Step 1: 写入分层反馈、唯一清单和触发式演进治理失败测试**
- [x] **Step 2: 运行 governance/skills 测试确认 RED**
- [x] **Step 3: 收敛入口规则、演进规则和性能 Skill**
- [x] **Step 4: 重跑 governance/skills 测试确认 GREEN**

### Task 5: 新鲜验证

**Files:**
- Modify: `docs/superpowers/plans/2026-07-29-governance-feedback-loop-hardening.md`

**Interfaces:**
- Consumes: 任务基线 `975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`。
- Produces: 新鲜测试、affected plan、静态检查和未验证项记录。

- [x] **Step 1: 运行 `pnpm test:integration:tooling`、`pnpm test:governance`、`pnpm test:skills`**
- [x] **Step 2: 运行 `pnpm test:integration:partitions` 验证清单分片**
- [x] **Step 3: 用任务快照运行 inner/slice plan，确认不包含既有业务脏改动和 `App_Data`**
- [x] **Step 4: 运行 `git diff --check`、`git status --short --branch` 并完成规则/Skill 触发复盘**
