# CodeGeneration Module Backend Apply Implementation Plan

> **For agentic workers:** Execute this plan inline with test-driven development. Do not create a worktree or dispatch subagents for this shared dirty workspace. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 增加独立的 `apply-module-integration` 命令，在真实 Release 编译通过后，把当前实体的后端生成产物原子写入显式目标模块。

**Architecture:** 模块工作区以模块项目目录为根，当前实体只拥有 `Generated/{ClrTypeName}/*.g.cs`；同一模块清单中其他实体的已拥有、未修改产物必须原样保留，缺失、被修改或非模块后端路径一律失败关闭。命令先生成无副作用写盘计划，冲突时立即退出；无冲突时通过临时 MSBuild 投影移除磁盘中的当前实体源文件并注入候选源文件执行真实 Release 编译，成功后才进入既有 `GenerationWorkspaceStore` 的锁、二次校验、原子替换和 Manifest 最后提交。

**Tech Stack:** .NET 10、MSBuild、System.Diagnostics.Process、严格 UTF-8、Manifest/SHA-256、MSTest。

## Global Constraints

- 必须显式提供 `--schema`、`--repository` 和 `--target`；Schema 根命名空间必须匹配目标模块。
- 写盘只能通过独立命令 `apply-module-integration` 触发；`plan-module-integration` 与 `validate-module-integration` 继续拒绝 `--apply`。
- 只写 `GeneratedArtifactKind.Backend`，目标固定为模块目录下 `Generated/{ClrTypeName}/*.g.cs`。
- 不修改模块项目、模块入口、Composition、Vue/Layui 路由、迁移、报告、客户端或测试模板。
- 同一模块的单一 Manifest 必须保留其他实体已拥有且摘要未变化的后端产物；不得静默接管人工修改或非模块后端路径。
- 已知冲突必须在真实编译前退出且零写入；编译失败、取消或运行器异常不得创建模块产物、Manifest、锁或临时文件。
- 临时编译必须从目标项目的 `Compile` 集合移除当前实体已有和陈旧源文件，再注入候选后端与 DI/Endpoint 探针，确保重复 Apply 不产生重复类型。
- 编译成功后仍必须由工作区存储器复验所有文件和 Manifest，关闭编译与提交之间的并发修改窗口。
- 冲突或编译失败返回 `2`，CLI 用法错误返回 `64`，运行器异常返回 `1`。
- 本地只运行 CodeGeneration 聚焦影响集；完整 Integration 保留给 main CI。

---

### Task 1: Build an entity-scoped backend workspace plan

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationBackendWorkspace.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/ModuleIntegrationBackendWorkspaceTests.cs`

**Interfaces:**

- Produces: `ModuleIntegrationBackendWorkspace.CreateArtifacts(FullNetCrudSchema)`.
- Produces: `ModuleIntegrationBackendWorkspace.PlanAsync(string moduleRoot, FullNetCrudSchema, CancellationToken)`.

- [x] **Step 1: Write workspace planning RED**

Assert that Product maps exactly five backend files below `Generated/Product/`, excludes all client/template/report artifacts, and produces no writes while planning.

- [x] **Step 2: Write multi-entity ownership RED**

Apply Product through the existing store, then plan Order. Require all Product actions to remain `Unchanged`, all Order actions to be `Create`, and a modified Product file or a previous non-backend Manifest entry to fail without changing disk.

- [x] **Step 3: Run focused RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~ModuleIntegrationBackendWorkspaceTests" --no-restore
```

Expected: compilation fails because `ModuleIntegrationBackendWorkspace` does not exist.

- [x] **Step 4: Implement the minimal scoped planner**

Map only backend artifacts into the entity directory, capture desired and previous owned files, preserve only unchanged `Generated/{Entity}/*.g.cs` entries outside the current entity, and delegate final classification to `GenerationWritePlanner`.

- [x] **Step 5: Run focused GREEN**

Repeat Step 3 and require zero failures.

### Task 2: Make temporary compilation replacement-aware

**Files:**

- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationBuildProjection.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationCompilationCommand.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/ModuleIntegrationBuildProjectionTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/CodeGeneration/ModuleIntegrationCompilationTests.cs`

**Interfaces:**

- Extends: projection creation with explicit absolute source paths to remove from the target project.
- Extends: compilation validation with the current entity workspace action paths.

- [x] **Step 1: Write replacement RED**

Require the temporary `.targets` to remove all supplied current-entity module sources before including candidate sources, while preserving the target-project condition.

- [x] **Step 2: Write repeated validation RED**

Create the current entity generated files in a temporary compilable module and run validation twice; require both builds to succeed without repository `bin/obj` writes or temporary directory leaks.

- [x] **Step 3: Run RED**

Run the focused projection and module compilation tests. Expected: duplicate generated types or missing `Compile Remove` entries.

- [x] **Step 4: Implement replacement-aware targets**

Normalize and deduplicate fully qualified removal paths, emit deterministic `Compile Remove` items before candidate `Compile Include` items, and use the module workspace mapping as the default removal set.

- [x] **Step 5: Run GREEN**

Repeat Step 3 and require zero failures.

### Task 3: Add explicit compile-gated module Apply

**Files:**

- Create: `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationBackendApplyCommand.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationPlanCommand.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`
- Create: `tests/Full.NET.IntegrationTests/CodeGeneration/ModuleIntegrationBackendApplyTests.cs`

**Interfaces:**

- Produces: `apply-module-integration --schema <json-file> --repository <existing-directory> --target <json-file>`.
- Produces: plan actions for the current entity and the stable `Validated ModuleCompilation` success line.

- [x] **Step 1: Write CLI routing RED**

Assert the independent command is recognized, rejects a trailing `--apply`, reports a missing module project without writes, and leaves existing plan/validate behavior unchanged.

- [x] **Step 2: Write real Apply RED**

Use a temporary module with real Full.NET project references. Require first Apply to compile then create five sources and one Manifest, second Apply to return five `Unchanged` actions, a handwritten conflict to remain untouched, and an under-referenced module to fail compilation with zero repository writes.

- [x] **Step 3: Run RED**

Run focused CLI Unit and Apply Integration tests. Expected: the command is unknown.

- [x] **Step 4: Implement compile-gated Apply**

Plan first; return conflicts without compiling. For an applicable plan, compile the candidate with current-scope removal paths, return sanitized diagnostics on failure, and call `GenerationWorkspaceStore.ApplyAsync` only after successful compilation.

- [x] **Step 5: Run GREEN**

Repeat Step 3 and require zero failures.

### Task 4: Close the slice with focused evidence

**Files:**

- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: this plan

- [x] **Step 1: Update canonical counts and capability wording**

Use fresh discovery for Unit and Integration counts. Describe only backend entity-scoped Apply as complete; automatic module entry, Composition and dual-route edits remain open.

- [x] **Step 2: Run final verification**

Run complete CodeGeneration Unit/Integration, complete Unit, Naming, both Release builds, Integration partition discovery, governance, and the snapshot-selected plan/affected set when it remains within the task boundary.

- [x] **Step 3: Run static checks**

Run `git diff --check`, inspect scoped status/branch, scan new files for placeholders and absolute paths, and confirm no command temporary directory remains.

- [x] **Step 4: Record governance conclusions**

Do not update rules or Skills unless implementation reveals a repeated failure category, rule conflict or genuine reusable workflow gap.

## Self-Review

- Spec coverage: entity-scoped ownership, multi-entity preservation, conflict zero-write, replacement-aware real compilation, repeated Apply, atomic Manifest commit and permanent manual runtime/client hookup all map to explicit tests.
- Placeholder scan: no deferred implementation marker or undefined interface remains.
- Type consistency: the BuildingBlocks planner returns `GenerationWritePlan`; the CLI compiler consumes current-scope action paths; the Apply command passes the same plan to `GenerationWorkspaceStore`.

## Verification Evidence

- TDD RED：实体工作区因缺少 `ModuleIntegrationBackendWorkspace` 编译失败；Apply Unit 返回未知命令 `64`；Apply Integration 两条均因未知命令失败。
- CodeGeneration Unit：`138/138`；完整 Unit：`653/653`。
- CodeGeneration Integration：`14/14`，包含真实临时模块首次 Apply、幂等重入、人工冲突、编译失败零写入、已有实体源码替换编译与取消清理。
- Integration 分片发现：SQL Server API `38`、MySQL API `38`、迁移 `70`、基础设施 `77`，合计 `223`，无遗漏或重复。
- Naming：`23/23`；Governance：`16/16`。
- `Full.NET.Data.CodeGeneration` 与 `Full.NET.CodeGeneration.Cli` Release 构建均为 `0` 警告、`0` 错误。
- 任务快照被并发的 Jobs、Notifications、Realtime 与 Worker 配置污染，inner/slice 预计 8/12 分钟；未运行范围外集合，改用本切片完整 CodeGeneration Unit/Integration、分片发现和治理门禁。
- 规则演进未命中；现有 `fullnet-module-delivery` 已覆盖该工作流，没有形成新的 Skill 契约缺口。
