# CodeGeneration Rollback Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 在不开放 Rollback HTTP/API 的前提下，提供一个只接受已验证 checkpoint 的原子工作区逆向执行内核，并在任何人工漂移、过期 checkpoint 或取消窗口中 fail-closed。

**Architecture:** 新增 `GenerationRollbackWorkspace`，先用 checkpoint 的 `AppliedManifest` 捕获并核对当前工作区，再以 checkpoint 的旧内容生成反向 `GenerationWritePlan`，最后复用 `GenerationWorkspaceStore.ApplyAsync` 的锁、暂存、恢复与 Manifest-last 提交。旧状态没有 Manifest 时，逆向结果提交规范空 Manifest；它明确表示“当前无受管产物”，避免另建一套删除 Manifest 的事务协议。本切片不查询数据库、不决定回滚资格，也不增加 Endpoint、权限、迁移或客户端。

**Tech Stack:** .NET 10、现有 Generation Manifest/WritePlanner/WorkspaceStore、MSTest。

## Global Constraints

- 任务快照固定为 `codegeneration-rollback-workspace-20260801`，基线 HEAD 为 `975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`。
- 只接受 `GenerationRollbackCheckpointStore.ReadAsync` 返回的强类型 checkpoint；路径、内容和目标 Manifest 不得由请求或调用方分别拼装。
- 当前磁盘 Manifest 必须逐字等于 checkpoint 的 `AppliedManifest`；任何后续 Apply、人工编辑、文件缺失、大小写别名、链接或摘要漂移均必须在首个写入前阻塞。
- 所有逆向动作继续由 `GenerationWorkspaceStore.ApplyAsync` 执行，不复制锁、recovery、暂存、删除或 Manifest 提交算法。
- checkpoint 的 `PreviousManifest is null` 时，成功结果是 schemaVersion 兼容的空 Manifest，不声称恢复 `.fullnet` 内部文件的字节级缺席状态。
- 不修改 CodeGeneration HTTP/JSON、权限、数据库、迁移、Vue、Layui、OpenAPI、配置或 `eng/testing/test-matrix.json`；测试方法变化仅在所有队列窗口冻结后按 fresh discovery 更新唯一矩阵。
- Files → Admin Task7 → Jobs → Realtime 队列释放 shared .NET/Docker 前，只允许完成本计划与只读审计，不落 RED 或生产代码。

---

### Task 1: Build a reverse plan from verified checkpoint evidence

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationRollbackWorkspace.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWritePlanner.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWorkspaceStore.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/GenerationRollbackWorkspaceTests.cs`

**Interfaces:**
- Produces: `GenerationRollbackWorkspace.PlanAsync(string workspaceRoot, GenerationRollbackCheckpoint checkpoint, CancellationToken cancellationToken = default)` returning `Task<GenerationWritePlan>`.
- Adds internal capture/planner entry points for neutral path sets and desired path/content pairs; existing public artifact capture/planning remains source-compatible and delegates to the same core.

- [x] **Step 1: Write the reverse-plan RED**

  Create a real workspace whose current files and Manifest equal `checkpoint.AppliedManifest`. Cover an update, an artifact created by Apply that must be deleted, and an artifact deleted by Apply that must be recreated; assert the reverse plan targets the exact previous content and Manifest.

- [x] **Step 2: Run the focused RED**

  Run `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~GenerationRollbackWorkspaceTests` and confirm failure is caused only by the absent rollback workspace type.

- [x] **Step 3: Implement the minimal planner**

  Refactor the existing Store capture into one internal path-based core so rollback does not invent a false `GeneratedArtifactKind`. Capture the union of current applied paths and desired previous paths, require the captured Manifest JSON to equal `checkpoint.AppliedManifest.ToJson()`, then delegate path/content classification to a shared internal `GenerationWritePlanner` core. When `PreviousManifest` is null, target `GenerationManifest.Create([])`.

- [x] **Step 4: Add stale-state fail-closed cases**

  Cover later Manifest replacement, current file modification, current file deletion, current path casing alias and reparse point. Every case must return a conflict plan or throw `GenerationWorkspaceConflictException` before creating staged files.

- [x] **Step 5: Re-run planner and existing writer suites**

  Require the new suite plus `GenerationWritePlannerTests`, `GenerationWorkspaceStoreTests` and `GenerationRollbackCheckpointStoreTests` to remain GREEN.

### Task 2: Execute the reverse plan through the existing atomic writer

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationRollbackWorkspace.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/GenerationRollbackWorkspaceTests.cs`

**Interfaces:**
- Produces: `GenerationRollbackWorkspace.RestoreAsync(string workspaceRoot, GenerationRollbackCheckpoint checkpoint, CancellationToken cancellationToken = default)` returning `Task<GenerationWritePlan>`.
- Preserves: existing `GenerationWorkspaceStore` cancellation, recovery and conflict semantics.

- [x] **Step 1: Write successful restore REDs**

  Prove a mixed create/update/delete Apply can be restored byte-for-byte for all previously owned content. Prove first-Apply rollback deletes all generated artifacts and leaves a valid empty Manifest.

- [x] **Step 2: Write ordering and cancellation REDs**

  Prove stale checkpoint and modified workspace cause zero mutation；取消在进入 Restore 前或规划期间发生时也必须零写入。继续运行现有 writer cancellation suites，证明首个不可逆提交后的取消仍由唯一 `GenerationWorkspaceStore` 完成自一致 Manifest；不要为 Rollback 复制测试钩子或提交算法。

- [x] **Step 3: Implement minimal execution**

  Call `PlanAsync`; if the plan is conflicted, return it without writing. Otherwise call only `GenerationWorkspaceStore.ApplyAsync` and return the applied plan. Do not delete checkpoint evidence or mutate any database record.

- [x] **Step 4: Re-run the complete Generation workspace focus**

  Run rollback workspace, checkpoint, planner, generation workspace and workspace store Unit suites; require zero failures and zero build warnings/errors.

### Task 3: Close the internal foundation without claiming product Rollback

**Files:**
- Modify: `docs/verification/codegeneration-apply-rollback-checkpoint-2026-08-01.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: this plan checklist only after fresh evidence exists.

**Interfaces:**
- Produces no public runtime API.
- Leaves database authority, rollback run state, permission, endpoint, dual-admin clients, retention and cross-instance recovery open.

- [x] **Step 1: Inspect the affected Integration plan**

  Run `pnpm test:integration:affected:plan -- --snapshot codegeneration-rollback-workspace-20260801 --phase inner`; verify the selector never schedules full Integration locally.

- [x] **Step 2: Run final affected verification after the queue freezes**

  Run focused Unit and naming gates, then one fresh affected inner during development and one slice after final file freeze. Do not run complete Unit or complete Integration locally.

- [x] **Step 3: Record the exact capability boundary**

  State only that an internal verified-checkpoint rollback executor exists. Keep overall capability `Build-verified` and explicitly list absent database success authority, Rollback API/permission/UI, retention, multi-instance scheduling and production rollout.

- [x] **Step 4: Final audit and review**

  Run fresh Unit discovery if methods changed, update only `eng/testing/test-matrix.json`, run `git diff --check`, verify runner/Docker residual zero, and obtain an independent security/recovery review with no open Critical/Important findings.

## Acceptance Checklist

- [x] Reverse planning requires the current Manifest to equal the checkpoint Applied Manifest.
- [x] Update/create/delete reversal restores all previously owned content without overwriting user drift.
- [x] A first-Apply rollback produces no managed artifacts and a documented canonical empty Manifest.
- [x] Existing workspace locking, staging, deletion recovery, cancellation and Manifest-last semantics are reused unchanged.
- [x] No database authority, public API, permission, client, migration or cleanup behavior is introduced.
- [x] Documentation distinguishes “internal rollback executor” from “product Rollback delivered”.
