# Full.NET 文档产物分层治理实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 保存综合架构评估，并让后续所有会话按统一规则区分评估记录、已批准规格、重大 ADR 与实施计划。

**Architecture:** 根 `AGENTS.md` 提供所有任务都会读取的强制入口，`rules/development-quality.md` 维护详细分类、状态流转和冲突处理规则。评估报告只记录当前证据与建议，不自动修改已批准架构基线。

**Approved decision:** [`docs/architecture/adr/ADR-0001-document-artifact-governance.md`](../../architecture/adr/ADR-0001-document-artifact-governance.md)

**Tech Stack:** Markdown、Git 结构化检查、PowerShell/`rg`

## Global Constraints

- 保留用户对 `docs/roadmap/capability-status.md` 的已有修改。
- 不修改代码、SQL、配置、公共契约或数据库对象。
- 不把评估建议描述为已批准规格或已实现能力。
- 本任务不创建 Git 提交。

---

### Task 1: 保存评估并建立双层文档治理规则

**Files:**
- Create: `docs/verification/architecture-assessment-2026-07-18.md`
- Create: `docs/architecture/adr/ADR-0001-document-artifact-governance.md`
- Modify: `AGENTS.md`
- Modify: `rules/development-quality.md`
- Test: 结构化 Markdown、路径、占位符与 Git 差异检查

**Interfaces:**
- Consumes: 当前总体架构规格、架构硬化规格、能力状态矩阵和本次综合评估结论。
- Produces: 后续会话可从根规则发现并执行的文档分层契约。

- [x] **Step 1: 建立变更前结构检查**

运行：

```powershell
Test-Path docs/verification/architecture-assessment-2026-07-18.md
rg -n "文档产物分层|docs/architecture/adr" AGENTS.md rules/development-quality.md
```

预期：报告路径不存在，根规则和详细规则中尚未同时出现分层契约。

- [x] **Step 2: 保存架构评估报告**

创建报告并明确写入日期、类型、状态、证据基线、建议结论、三方案比较、目标架构、拆分门禁和未验证项。报告状态必须为“建议稿”，并声明其不自动覆盖已批准规格。

- [x] **Step 3: 增加根入口与详细规则**

在 `AGENTS.md` 的开始前检查中增加文档分层入口；在 `rules/development-quality.md` 第 12 节增加路径职责、禁止混用、状态流转、Spec/ADR 更新条件、Plan 与 Verification 的证据边界。

- [x] **Step 4: 执行结构化验证**

运行：

```powershell
rg -n "状态：建议稿|不自动覆盖|强化型模块化单体" docs/verification/architecture-assessment-2026-07-18.md
rg -n "docs/verification|docs/superpowers/specs|docs/architecture/adr|docs/superpowers/plans" AGENTS.md rules/development-quality.md
rg -n "T[B]D|T[O]DO|待[补]|占[位]" docs/verification/architecture-assessment-2026-07-18.md docs/architecture/adr/ADR-0001-document-artifact-governance.md
git diff --check
git status --short
```

预期：必需状态和四类路径均可检索；没有占位符或空白错误；`git status` 只新增/修改本任务文件和用户原有差异。

- [x] **Step 5: 完成规则与 Skill 复盘**

读取 `rules/rule-evolution.md` 和 `rules/skill-evolution.md`，记录本次属于用户明确确认的长期文档治理决策；除本次规则更新外，不制造重复规则或新 Skill。
