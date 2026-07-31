# CodeGeneration Database Batch Apply Implementation Plan

> **For agentic workers:** Execute this plan inline with test-driven development. Do not create a worktree or dispatch subagents for this shared dirty workspace. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在已验证的数据库多表合并预览之上，增加独立、显式且整批安全的 `apply-database-batch` 命令。

**Architecture:** `preview-database-batch` 保持永久只读并继续拒绝 `--apply`。新的 `apply-database-batch` 复用同一严格映射、单连接多表导入和全局工作区计划；只有合并计划完全无冲突时才交给现有原子工作区存储器，任一冲突都必须整批零写入。

**Tech Stack:** .NET 10、System.Text.Json、ADO.NET、Microsoft.Data.SqlClient、MySqlConnector、MSTest、SQL Server/MySQL Testcontainers。

## Global Constraints

- `preview-database-batch` 继续拒绝 `--apply`，不能因新增写盘命令而改变既有只读契约。
- 批量写盘只能通过独立命令 `apply-database-batch` 显式触发，不提供隐式默认写盘。
- Preview 与 Apply 必须使用同一个严格映射模型、数据库导入实现和合并工作区规划逻辑。
- 所有 Schema 产物必须先合并为一个 `GenerationWritePlan`；任一 `Conflict` 时不得创建 Manifest、锁、临时文件或其他生成产物。
- 无冲突计划复用 `GenerationWorkspaceStore.ApplyAsync` 的锁、二次状态校验、临时文件、恢复证据和 Manifest 最后提交语义。
- 一份成功 Manifest 必须同时拥有整批所有产物；重复执行必须按整批返回 `Unchanged`。
- 连接串仍只能通过 `--connection-env` 间接读取，stderr 不得输出环境变量名、连接串、驱动消息或堆栈。
- 本切片不修改数据库结构、不增加迁移、不扩充规则或 Skill、不运行本地完整 Integration。

---

### Task 1: Add all-or-nothing multi-schema workspace apply

**Files:**

- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CrudGenerationWorkspaceTests.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudGenerationWorkspace.cs`

**Interfaces:**

- Consumes: `PlanAsync(string, IReadOnlyList<FullNetCrudSchema>, CancellationToken)`.
- Produces: `ApplyAsync(string, IReadOnlyList<FullNetCrudSchema>, CancellationToken)`.

- [x] **Step 1: Write the successful batch apply RED**

Add a test that applies Product and Order schemas, then asserts 26 generated artifacts, one Manifest with 26 entries, and a second batch application whose 26 actions are all `Unchanged`.

- [x] **Step 2: Write the conflict zero-write RED**

Pre-create `backend/ProductContracts.g.cs` as handwritten content, apply Product and Order together, and assert `CanApply == false`, the handwritten file is unchanged, no Order artifact exists, no Manifest exists, and the workspace still contains exactly one file.

- [x] **Step 3: Run focused RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CrudGenerationWorkspaceTests" --no-restore
```

Expected: compilation fails because the multi-schema `ApplyAsync` overload does not exist.

- [x] **Step 4: Implement the minimal overload**

Make the single-schema overload delegate to the list overload. The list overload calls the existing multi-schema `PlanAsync`; when `CanApply` is false it returns the plan without calling the store, otherwise it calls `GenerationWorkspaceStore.ApplyAsync`.

- [x] **Step 5: Run focused GREEN**

Repeat Step 3 and require zero failures.

### Task 2: Add an explicit apply command without weakening preview

**Files:**

- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- Rename: `src/Tools/Full.NET.CodeGeneration.Cli/DatabaseBatchPreviewCliOptions.cs` to `DatabaseBatchCliOptions.cs`
- Rename: `src/Tools/Full.NET.CodeGeneration.Cli/DatabaseBatchPreviewCommand.cs` to `DatabaseBatchImportCommand.cs`

**Interfaces:**

- Produces: `apply-database-batch --provider ... --connection-env ... --mapping ... --workspace ...`.
- Preserves: `preview-database-batch` with the same arguments and no `--apply`.
- Produces: shared `DatabaseBatchCliOptions` and `DatabaseBatchImportCommand.ImportAsync(...)`.

- [x] **Step 1: Write the CLI routing RED**

Keep the existing test proving `preview-database-batch ... --apply` returns usage error. Add a test invoking `apply-database-batch` with a missing connection environment variable and assert the failure reaches the existing `--connection-env` boundary without writing the workspace.

- [x] **Step 2: Run focused RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGenerationCliTests" --no-restore
```

Expected: `apply-database-batch` is rejected as an unknown command instead of reaching connection validation.

- [x] **Step 3: Implement shared strict parsing**

Route both command names through one parser receiving the intended apply mode. Reject every option outside `--provider`, `--connection-env`, `--mapping`, and `--workspace`; do not accept a separate `--apply` switch on either command. Store the explicit mode in `CliOptions.Apply`.

- [x] **Step 4: Reuse one import and planning path**

Rename the batch options/import types to remove preview-only naming. In the batch execution branch choose `CrudGenerationWorkspace.PlanAsync` for preview and `ApplyAsync` for apply, while preserving the existing conflict and UTF-8 error handling.

- [x] **Step 5: Run focused GREEN**

Repeat Step 2 and require zero failures, including the old preview rejection test.

### Task 3: Verify real dual-provider preview and apply

**Files:**

- Rename: `tests/Full.NET.IntegrationTests/CodeGeneration/DatabaseBatchPreviewCliIntegrationTests.cs` to `DatabaseBatchCliIntegrationTests.cs`

**Interfaces:**

- Consumes: the same two-table strict mapping and isolated SQL Server/MySQL databases.
- Verifies: preview zero writes followed by explicit apply and idempotent re-apply.

- [x] **Step 1: Extend both provider tests before implementation**

For each provider, run preview and assert zero files; then run `apply-database-batch`, assert 26 `Create` actions, 26 Manifest entries and all generated files; run apply again and assert 26 `Unchanged` actions.

- [x] **Step 2: Run dual-provider RED**

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~DatabaseBatchCliIntegrationTests" --no-restore
```

Expected before Task 2 implementation: apply returns usage error because the command is unknown.

- [x] **Step 3: Run dual-provider GREEN**

Repeat Step 2 after Task 2 and require SQL Server and MySQL to pass.

### Task 4: Close the slice with affected verification

**Files:**

- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/superpowers/plans/2026-07-30-codegeneration-database-batch-apply.md`

- [x] **Step 1: Update canonical evidence**

Set the Unit minimum to the fresh discovered total. Keep Integration totals unchanged because existing two provider tests are extended rather than duplicated. Update the two roadmap entries to distinguish completed explicit batch Apply from still-open visual management and automatic module hookup.

- [x] **Step 2: Run focused and project verification**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGeneration" --no-restore
pnpm test:dotnet:unit
pnpm test:naming
dotnet build src/BuildingBlocks/Full.NET.Data.CodeGeneration/Full.NET.Data.CodeGeneration.csproj -c Release --no-restore
dotnet build src/Tools/Full.NET.CodeGeneration.Cli/Full.NET.CodeGeneration.Cli.csproj -c Release --no-restore
```

- [x] **Step 3: Run the task snapshot impact set**

```powershell
pnpm test:integration:affected:plan -- --snapshot codegeneration-database-batch-apply-20260730 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-database-batch-apply-20260730 --phase slice
```

- [x] **Step 4: Run static workspace checks**

```powershell
git diff --check
git status --short --branch
```

- [x] **Step 5: Record governance conclusions**

No repeated failure category, rule conflict, or project Skill gap appeared; neither rules nor Skills were changed.

## Self-Review

- Spec coverage: separate explicit Apply command, permanent preview-only contract, merged plan, conflict zero-write, single Manifest, idempotent re-apply, dual Provider evidence and focused local verification all map to concrete tests.
- Placeholder scan: no TODO, TBD, vague exception handling, undefined interface, or deferred implementation step remains.
- Type consistency: both CLI modes consume `DatabaseBatchCliOptions`; the import adapter returns `IReadOnlyList<FullNetCrudSchema>`; both workspace paths consume the same list and differ only at `PlanAsync` versus `ApplyAsync`.
