# CodeGeneration Module Compile Validation Implementation Plan

> **For agentic workers:** Execute this plan inline with test-driven development. Do not create a worktree or dispatch subagents for this shared dirty workspace. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不修改真实模块、Composition 或客户端路由的前提下，把生成后端临时注入显式目标模块并执行真实 Release 编译验证。

**Architecture:** `validate-module-integration` 复用严格 Schema 和模块接入目标 JSON，生成后端 `.g.cs` 与只存在于临时目录的 DI/Endpoint 编译探针，再通过临时 MSBuild `.targets` 注入目标模块。`dotnet build --artifacts-path` 将恢复、中间文件和输出全部定向到命令拥有的系统临时目录；命令结束后清理该目录，仓库只读。现有 `plan-module-integration` 不改变且继续拒绝 `--apply`。

**Tech Stack:** .NET 10、MSBuild、System.Diagnostics.Process、System.Xml.Linq、MSTest。

## Global Constraints

- 必须显式提供 `--schema`、`--repository` 和 `--target`；不得根据命名空间猜测模块项目。
- 只编译 `GeneratedArtifactKind.Backend`，不得把客户端、报告、迁移或测试模板注入模块项目。
- 临时探针必须同时调用 `AddGenerated{Entity}Feature` 与 `MapGenerated{Entity}Feature`，验证 DI 和 Endpoint 扩展可从模块命名空间接入。
- 禁止修改目标 `.csproj`、模块入口、Composition、路由、Manifest、仓库 `bin/obj` 或生成工作区。
- 构建恢复、中间文件和输出必须通过 `--artifacts-path` 写入命令拥有的系统临时目录。
- 成功只输出稳定的模块项目相对路径；失败只输出去除仓库/临时绝对路径后的有限编译诊断。
- 取消时必须终止构建进程树；成功、失败和取消都必须清理本次创建的临时目录。
- 编译失败返回 `2`，CLI 用法错误返回 `64`，运行器异常返回 `1`；验证命令不接受 `--apply`。
- 本切片不修改数据库、迁移、模块、Composition、客户端、规则或 Skill，不运行本地完整 Integration。

---

### Task 1: Build the deterministic temporary projection

**Files:**

- Create: `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationBuildProjection.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/ModuleIntegrationBuildProjectionTests.cs`

**Interfaces:**

- Produces: `ModuleIntegrationBuildProjection.Create(FullNetCrudSchema, string moduleProjectFullPath, string projectionRoot)`.
- Produces: backend projected files, a compile probe and the injection `.targets` text.

- [x] **Step 1: Write the projection RED**

Assert that Product creates exactly the backend artifacts plus one probe, excludes every client/report/template artifact, links files under `Generated/`, and emits a target-project condition without writing the supplied projection directory.

- [x] **Step 2: Run focused RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~ModuleIntegrationBuildProjectionTests" --no-restore
```

Expected: compilation fails because `ModuleIntegrationBuildProjection` does not exist.

- [x] **Step 3: Implement the pure projection**

Use `CrudArtifactGenerator.Generate(schema)` and select only `GeneratedArtifactKind.Backend`. Add one deterministic probe in `schema.RootNamespace` that imports `.Generated` and invokes both generated extension methods. Build the MSBuild XML with `XDocument`; do not access the file system.

- [x] **Step 4: Run focused GREEN**

Repeat Step 2 and require zero failures.

### Task 2: Execute an isolated real module build

**Files:**

- Create: `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationCompilationCommand.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`
- Create: `tests/Full.NET.IntegrationTests/CodeGeneration/ModuleIntegrationCompilationTests.cs`

**Interfaces:**

- Produces: `validate-module-integration --schema <json-file> --repository <existing-directory> --target <json-file>`.
- Produces: `ModuleIntegrationCompilationResult` with success and bounded sanitized diagnostics.
- Preserves: all `plan-module-integration` output and rejection behavior.

- [x] **Step 1: Write CLI routing RED**

Assert the new command is recognized, rejects `--apply`, and reports a missing target module project without creating repository files.

- [x] **Step 2: Write real compiler RED**

Add one Integration test targeting `Full.NET.Modules.Settings` and require a successful isolated Release build with unchanged target files. Add one temporary under-referenced module and require exit `2`, a compiler diagnostic code, no absolute repository/temp path, and no surviving command-owned temporary directory.

- [x] **Step 3: Run focused RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGenerationCliTests.Validate_module_integration" --no-restore
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~ModuleIntegrationCompilationTests" --no-restore
```

Expected: the command is unknown.

- [x] **Step 4: Implement isolated compilation**

Create a uniquely named directory below `Path.GetTempPath()`, write the projection using strict UTF-8 without BOM, invoke `dotnet build` with Release, `--artifacts-path`, the custom targets property and target project property, capture stdout/stderr asynchronously, sanitize at most 20 diagnostic lines, and clean up in `finally`. Kill the process tree when cancellation interrupts the wait.

- [x] **Step 5: Run focused GREEN**

Repeat Step 3 and require Unit plus real compiler Integration tests to pass.

### Task 3: Close the slice with focused evidence

**Files:**

- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: this plan

- [x] **Step 1: Update canonical counts and capability wording**

Use fresh discovery to update only the Unit, infrastructure and full minima in the test matrix. Describe the new capability as isolated compile validation, not automatic repository application.

- [x] **Step 2: Run final verification**

Run CodeGeneration, complete Unit, Naming, both CodeGeneration Release builds, Integration partition coverage, and the snapshot-selected inner/slice impact set.

- [x] **Step 3: Run static checks**

Run `git diff --check`, inspect scoped status and branch, and scan the new files for placeholders or leaked absolute paths.

- [x] **Step 4: Record governance conclusions**

No rules or Skills are changed unless implementation evidence reveals a repeated failure category, rule conflict or real Skill contract gap.

## Self-Review

- Spec coverage: isolated projection, backend-only injection, DI/Endpoint probe, real Release compiler, temporary artifacts, cleanup, cancellation, sanitized failure output and permanent preview-only behavior all map to explicit tests.
- Placeholder scan: no deferred implementation marker or undefined interface remains.
- Type consistency: CLI, projection, compiler result and tests use the same Schema and `ModuleIntegrationTarget` contract introduced by the preceding slice.

## Verification Evidence

- CodeGeneration Unit：`133/133`；完整 Unit：`633/633`。
- CodeGeneration Integration：`11/11`，其中模块临时编译成功、依赖缺失和取消清理场景为 `3/3`。
- Integration 分片发现：SQL Server API `38`、MySQL API `38`、迁移 `70`、基础设施 `74`，合计 `220`，无遗漏或重复。
- `Full.NET.Data.CodeGeneration` 与 `Full.NET.CodeGeneration.Cli` Release 构建均为 `0` 警告、`0` 错误；治理测试 `16/16`。
- `git diff --check` 通过；新增文件未发现 TODO、FIXME、未实现占位或本机绝对路径；命令临时目录无残留。
- Naming 为 `22/23`：唯一失败来自共享工作区范围外的 `037_JobsRetryScheduling.sql`，共 5 个 `FNSQL003 unsupported_ddl`。
- 任务快照被并发的 Jobs、037 迁移、Realtime 与 Worker 变更污染，inner/slice 分别估算 38/40 分钟；未运行该范围外慢集合，改为运行本切片完整 CodeGeneration Integration 影响集。完整 Integration 继续只由 main CI 分片执行。
- 规则演进未命中；本次能力已被现有 `fullnet-module-delivery` 覆盖，没有形成新的 Skill 契约缺口，未新增或修改规则与 Skill。
