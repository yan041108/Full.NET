# CodeGeneration Module Integration Read-Only Plan

> **For agentic workers:** Execute this plan inline with test-driven development. Do not create a worktree or dispatch subagents for this shared dirty workspace.

**Goal:** 为已经生成的 CRUD 产物增加独立的模块接入只读规划，明确展示后端落位、模块服务/Endpoint 注册、Composition 项目/目录以及 Vue/Layui 路由影响，且绝不修改仓库。

**Architecture:** 新命令 `plan-module-integration` 读取严格 Schema、仓库根目录和严格接入目标 JSON。文件系统适配器只捕获目标文件是否存在及严格 UTF-8 文本，纯规划器据此输出稳定排序的 `Satisfied`、`ChangeRequired`、`ManualReview` 或 `Blocked` 项。客户端路由只给出人工复核项；本切片不做模糊文本合并、不生成保护区注释，也不提供 Apply。

**Tech Stack:** .NET 10、System.Text.Json、MSTest。

## Constraints

- 接入目标的模块项目、模块入口、Composition 项目/目录和双管理端路由必须使用仓库相对路径显式声明。
- 所有路径必须拒绝绝对路径、父目录穿越、反斜杠和不可移植大小写别名。
- 规划期间不得创建、修改或删除任何仓库文件，也不得产生 Manifest、锁或临时文件。
- 模块项目和入口缺失时输出 `Blocked`，不得假定新模块的依赖、Contracts 拆分或宿主 Profile。
- SDK 风格模块项目默认包含同目录下的 `.g.cs`；规划器展示生成后端的目标目录，但不自动写入。
- 服务和 Endpoint 注册分别检查 `AddGenerated{Entity}Feature` 与 `MapGenerated{Entity}Feature`。
- Composition 项目引用与模块目录注册只做精确、保守检测；不能确认时输出 `ChangeRequired`。
- Vue/Layui 路由始终输出 `ManualReview`，因为权限、菜单、翻译、页面组件和动态导航不能由资源名可靠推导。
- 本切片不修改数据库、迁移、Composition、模块入口、客户端路由、规则或 Skill，不运行本地完整 Integration。

### Task 1: Freeze the pure integration planning contract

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationTarget.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationSnapshot.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationPlan.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationPlanner.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/ModuleIntegrationPlannerTests.cs`

- [x] Write RED tests for an existing module with missing feature registration, an already integrated feature, and missing module files.
- [x] Require stable ordering and exact statuses for backend placement, module project, service registration, Endpoint registration, Composition project, Composition catalog, Vue route and Layui route.
- [x] Implement the smallest pure planner; do not access the file system from the planner.
- [x] Run focused tests and require GREEN.

### Task 2: Add a strict read-only CLI adapter

**Files:**

- Create: `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationTargetDocument.cs`
- Create: `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationPlanCommand.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`

- [x] Write RED tests for valid planning with zero repository writes, unknown target JSON members, unsafe paths, missing targets, and rejection of `--apply`.
- [x] Add `plan-module-integration --schema <json-file> --repository <existing-directory> --target <json-file>`.
- [x] Load target JSON as strict UTF-8 without BOM, reject unknown properties and validate all repository-relative paths.
- [x] Capture only the explicitly named files and print one stable line per plan item.
- [x] Run CLI-focused tests and require GREEN.

### Task 3: Close the slice with focused verification

**Files:**

- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: this plan

- [x] Update the canonical Unit minimum from fresh output and describe module integration as read-only planning, not automatic application.
- [x] Run CodeGeneration tests, complete Unit, Naming, two Release builds and the snapshot-selected affected set.
- [x] Run `git diff --check` and inspect branch/status without staging.
- [x] Record rule/Skill evolution conclusions; update neither unless a real gap is found.

## Verification Evidence

- `dotnet test ... --filter "FullyQualifiedName~CodeGeneration"`：129/129 通过。
- `pnpm test:dotnet:unit`：629/629 通过，构建 0 警告、0 错误。
- `pnpm test:naming`：23/23 通过。
- CodeGeneration 与 CLI 两个 Release 构建：均为 0 警告、0 错误。
- 快照 `codegeneration-module-integration-plan-20260730` 的 inner 与 slice：Integration 工具链 38/38、治理 16/16、CodeGeneration 双 Provider 影响集 8/8，分片覆盖 215 项无遗漏或重复。
- 完整 Integration 未在本地运行，按仓库策略保留给 `main` CI。
- 未出现重复失败类别、规则冲突或项目 Skill 缺口；本切片未修改规则或 Skill。
