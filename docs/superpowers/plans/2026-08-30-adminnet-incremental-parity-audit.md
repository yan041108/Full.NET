# Admin.NET Incremental Parity Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 审计 Admin.NET.Pro `3879b035` 到 `3c65392d` 的 28 个提交，并以现有 Full.NET 代码和新鲜证据修正功能对标与能力状态矩阵。

**Architecture:** 以本机同步的 Admin.NET.Pro `v2.1` 为外部功能事实源，只提取能力与行为变化，不复制源码。Full.NET 状态只依据仓库代码、验证记录和既有状态定义调整；没有双库、权限、租户与真实栈证据的能力不得升级为 `Verified`。

**Tech Stack:** Git、Markdown、Node.js governance tests。

## Global Constraints

- 不修改生产代码、SQL、配置、测试矩阵或客户端。
- Admin.NET.Pro 依赖升级和实现重构不得自动转化为 Full.NET 功能缺口。
- 相同能力按 `Covered`、`Gap`、`Deferred/Compatibility`、`No status change` 分类。
- `Build-verified` 不得因本次静态审计升级为 `Verified`。
- 只更新路线图总览与本次验证记录，历史 verification 快照保持不变。

---

### Task 1: 审计 28 个增量提交

**Files:**
- Create: `docs/verification/2026-08-30-adminnet-incremental-parity-audit.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`

**Interfaces:**
- Consumes: Admin.NET.Pro Git range `3879b035791b4603e734c15e7c316e0aeca32f1b..3c65392d8e9c543411b9469a400fe4deee86dc15`。
- Produces: 可追溯的提交分类、Full.NET 覆盖判断与待补能力清单。

- [x] **Step 1: Capture the exact commit range and changed-file inventory.**

Run:

```powershell
git -C G:\wwwroot\github_fork\Admin.NET.Pro log --reverse --format="%H%x09%ad%x09%s" --date=short 3879b035..3c65392d
git -C G:\wwwroot\github_fork\Admin.NET.Pro diff --name-status 3879b035..3c65392d
```

Expected: 28 commits; the changed files group into dependency/runtime fixes, cache/log/user/organization behavior, Workflow persistence, WeChat/OpenAccess/MQTT clients and frontend maintenance.

- [x] **Step 2: Cross-check each behavior group against Full.NET source and verification records.**

Record one disposition per group: `Covered`, `Gap`, `Deferred/Compatibility`, or `No status change`, including exact evidence paths.

- [x] **Step 3: Update the parity baseline and add the incremental-audit decision summary.**

Update `adminnet-feature-parity.md` to baseline `3c65392d`, date `2026-08-30`, and link the new verification record. Do not change a feature status unless Task 2 evidence supports it.

### Task 2: Correct capability matrix status drift

**Files:**
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `docs/superpowers/plans/2026-08-30-adminnet-incremental-parity-audit.md`

**Interfaces:**
- Consumes: Task 1 audit and existing verification records.
- Produces: consistent current status and explicit next delivery order.

- [x] **Step 1: Reconcile contradictory status statements.**

Keep Document at `Build-verified` until fresh WCAG and dual-database real-stack evidence exists. Keep Workflow `Mapped / Spec pending review`. Do not promote Identity, RBAC, CodeGeneration, Organization, Tenancy or Files without fresh evidence.

- [x] **Step 2: Record newly confirmed gaps without overstating delivery.**

Add Identity profile format/uniqueness validation and Observability log-file read control plane as gaps; record health endpoints and login lockout as already covered; retain OpenAccess, MQTT, WeChat and Workflow statuses.

- [x] **Step 3: Run documentation and governance verification.**

Run:

```powershell
pnpm test:governance
git diff --check
git status --short --branch
```

Expected: governance passes, no whitespace errors, and only the five planned Markdown files are modified/created.

## Self-review

- Spec coverage: Task 1 covers the 28-commit audit; Task 2 covers status corrections and next-step ordering.
- Placeholder scan: no TBD/TODO or unspecified implementation action remains.
- Type consistency: this plan changes no runtime type or public contract.

---

### Task 3: Re-audit the refreshed Admin.NET.Pro checkout

**Files:**
- Create: `docs/verification/2026-08-30-adminnet-refresh-incremental-audit.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/superpowers/plans/2026-08-30-adminnet-incremental-parity-audit.md`

**Interfaces:**
- Consumes: refreshed Admin.NET.Pro range `3c65392d8e9c543411b9469a400fe4deee86dc15..09d38bd82603ca23b2e39644376906bd1023a42f`.
- Produces: a second-stage 31-commit disposition and an updated parity baseline without changing runtime code.

- [x] **Step 1: Capture the refreshed range.**

Run:

```powershell
git -C G:\wwwroot\github_fork\Admin.NET.Pro rev-list --count 3c65392d..09d38bd8
git -C G:\wwwroot\github_fork\Admin.NET.Pro diff --stat 3c65392d..09d38bd8
```

Expected: 31 commits and 315 changed files.

- [x] **Step 2: Review capability-bearing changes against Full.NET.**

Classify log streaming, tenant validation, import templates, report execution, MCP, dynamic JSON and Workflow changes. Dependency upgrades, generated clients and frozen `Web_Artd` maintenance do not change Full.NET capability state.

- [x] **Step 3: Update the audit and status documents.**

Update the baseline to `09d38bd8`; add the Excel user-import experience and MCP as explicit gaps; keep Workflow at `Mapped / Spec pending review` while recording that its result notification and immutable draft/version semantics are already in the Spec.

- [x] **Step 4: Verify the documentation-only change.**

Run:

```powershell
pnpm test:governance
git diff --check
git status --short --branch
```

Expected: governance passes, no whitespace errors, and the task snapshot shows only the planned Markdown delta beyond the prior five-file audit change.
