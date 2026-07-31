# CodeGeneration Apply Rollback Checkpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不开放 Rollback API 的前提下，让每次 Host Apply 在写入工作区前持久化一个不可覆盖、可校验的本地逆向检查点，为后续受控回滚提供完整旧内容证据。

**Architecture:** 检查点保存在服务器配置工作区的 `.fullnet/codegeneration-rollback-checkpoints/{applyRunId:N}`，只记录 Apply 前的受管 Manifest、对应旧内容和计划提交的 Manifest；目录先在同级临时路径完整写入，再以同卷目录重命名发布，既有运行 ID 永不覆盖。Host Apply 先生成无冲突计划、写检查点，再调用既有 `GenerationWorkspaceStore.ApplyAsync`；数据库中 `succeeded` 的 Apply 仍是未来允许回滚的唯一权威，本切片不增加回滚端点、权限、迁移、客户端或自动清理。

**Tech Stack:** .NET 10、System.Text.Json、现有 Generation Manifest/Workspace Store、MSTest、NSubstitute。

## Global Constraints

- 任务快照固定为 `codegeneration-apply-rollback-checkpoint-20260801`，基线 HEAD 为 `975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`。
- 检查点根目录只能从已校验的 `CodeGeneration:Apply:WorkspaceRoot` 派生；请求、模板和运行记录不得选择路径。
- 检查点只保存上一版 Manifest 实际拥有的文件；缺失、摘要漂移、大小写别名、链接或损坏内容必须在 Apply 写盘前 fail-closed。
- 同一 `applyRunId` 的检查点不得覆盖、合并或静默复用。
- 不修改数据库、Vue、Layui、OpenAPI 或公共 HTTP 契约；不占用 047。测试方法变化只允许依据共享工作区 fresh discovery 更新唯一矩阵 `eng/testing/test-matrix.json`。
- 本地只运行任务影响集，完整集合继续保留给 `main` CI。

---

### Task 1: Define and persist an immutable rollback checkpoint

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationRollbackCheckpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationRollbackCheckpointStore.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/GenerationRollbackCheckpointStoreTests.cs`

**Interfaces:**
- Produces: `GenerationRollbackCheckpoint(Guid ApplyRunId, GenerationManifest AppliedManifest, GenerationManifest? PreviousManifest, IReadOnlyDictionary<string,string> PreviousContents)`.
- Produces: `GenerationRollbackCheckpointStore.CreateAsync(string workspaceRoot, Guid applyRunId, GenerationWritePlan plan, CancellationToken cancellationToken = default)`.
- Produces: `GenerationRollbackCheckpointStore.ReadAsync(string workspaceRoot, Guid applyRunId, CancellationToken cancellationToken = default)`.

- [x] **Step 1: Write the failing persistence test**

  Create a real temporary workspace with a previous manifest and owned file, plan an update, call `CreateAsync`, then assert `ReadAsync` returns the exact apply ID, old manifest, new manifest and byte-equivalent old content.

- [x] **Step 2: Run the focused RED**

  Run `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~GenerationRollbackCheckpointStoreTests` and confirm compilation fails only because the checkpoint types are absent.

- [x] **Step 3: Implement the minimal atomic store**

  Validate `plan.CanApply`, read only `plan.PreviousManifest.Artifacts`, verify every current file SHA-256, serialize stable metadata with System.Text.Json, write content blobs by SHA-256 under a task-owned temporary directory, and publish with `Directory.Move`. `ReadAsync` must repeat path, schema, digest and content verification before returning the checkpoint.

- [x] **Step 4: Add fail-closed RED/GREEN cases**

  Cover duplicate `applyRunId`, missing previous owned file, modified previous content, malformed metadata and reparse/casing conflicts. Every failure must leave an existing final checkpoint unchanged and must not publish a partial final directory.

- [x] **Step 5: Run the checkpoint suite GREEN**

  Re-run the focused command and require all discovered checkpoint tests to pass with zero build warnings/errors.

### Task 2: Put checkpoint publication before Host Apply workspace mutation

**Files:**
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationApplyService.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationApplyServiceTests.cs`

**Interfaces:**
- Consumes: `GenerationRollbackCheckpointStore.CreateAsync(...)` from Task 1.
- Preserves: existing `CodeGenerationRunApplyRequest/Response`, permission, error codes and SQL state transitions.

- [x] **Step 1: Write ordering and failure REDs**

  Extend the success test to load the checkpoint for `ApplyRunId`. Add a checkpoint-publication failure case proving the run becomes failed and the workspace remains unchanged. Extend insert/conflict cases to prove no final checkpoint is published when no workspace mutation is eligible.

- [x] **Step 2: Run the Apply service RED**

  Run `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~CodeGenerationApplyServiceTests|FullyQualifiedName~CodeGenerationRunServiceTests"` and confirm failure is caused by missing checkpoint orchestration.

- [x] **Step 3: Implement the minimal orchestration**

  Replace the one-step `CrudGenerationWorkspace.ApplyAsync` call with `PlanAsync`; for a conflict, retain the existing stable failure. For an eligible plan, publish the checkpoint with the already allocated `runId`, then call `GenerationWorkspaceStore.ApplyAsync`. Map known checkpoint IO/path/content failures to the existing path-safe `codegen.run.apply_failed` response and preserve guarded single-row terminal updates.

- [x] **Step 4: Re-run Apply and workspace suites GREEN**

  Run the Apply/Run focus plus `GenerationRollbackCheckpointStoreTests`, `CrudGenerationWorkspaceTests`, `GenerationWorkspaceStoreTests`; require zero failures.

### Task 3: Close the foundation slice without claiming Rollback delivery

**Files:**
- Create: `docs/verification/codegeneration-apply-rollback-checkpoint-2026-08-01.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: this plan checklist only after fresh evidence exists.

**Interfaces:**
- Produces no new runtime API.
- Leaves Rollback UI/API, checkpoint retention, multi-instance execution and production rollout open.

- [x] **Step 1: Run focused static and build gates**

  Run focused Unit, `pnpm test:naming`, CodeGeneration API dual-provider affected tests selected by the task snapshot, and Release builds required by the affected selector.

- [x] **Step 2: Run affected inner and slice**

  Inspect `pnpm test:integration:affected:plan --snapshot codegeneration-apply-rollback-checkpoint-20260801 --phase inner`, then run `inner`; after files freeze, repeat with `--phase slice`. Do not run full Integration locally.

- [x] **Step 3: Record truthful evidence**

  State that Apply now emits durable local rollback evidence, but keep Rollback API/permission/client/retention and cross-instance recovery unimplemented. Do not mark the overall capability `Verified`.

- [x] **Step 4: Final audit**

  Run `git diff --check`, task-scoped status review, fresh discovery only if test methods changed, and runner/Docker residual checks. Update only `eng/testing/test-matrix.json` from fresh shared-workspace discovery after all writers freeze.

## Acceptance Checklist

- [x] An eligible Apply publishes its immutable checkpoint before the first workspace mutation.
- [x] The checkpoint contains the exact previous owned manifest/content and the exact planned next manifest.
- [x] Missing, modified, malformed, linked or duplicate checkpoint state fails closed without overwriting evidence.
- [x] Existing Apply HTTP, permission, response and database state semantics remain compatible.
- [x] No Rollback API/UI, migration number or automatic cleanup is introduced in this foundation slice.
- [x] Documentation distinguishes “rollback evidence available” from “rollback delivered”.
