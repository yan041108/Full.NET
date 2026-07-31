# CodeGeneration 双管理端路由显式接入实施计划

> **For agentic workers:** 本计划由当前主任务在共享脏工作区内按 TDD 逐项执行；
> 不创建 worktree、不委派子任务。任务快照为
> `codegeneration-dual-admin-route-apply-20260730`。

**Goal:** 增加独立的 `apply-client-route-integration` 命令，在调用方显式声明且
Vue View、Layui controller 已存在时，安全、幂等地接入双管理端本地路由白名单。

**Architecture:** 模块接入目标新增可选 `clientRoute` 描述符，缺失时继续保持人工复核。
纯内存编辑器只接受当前仓库的标准 Vue `routes` 数组与 Layui
`createLayuiRouteControllerDefinitions` Map 形态；Apply 命令要求后端、模块入口和
Composition 已完成，在双路由候选、适配文件与并发复核通过后原子提交两份手写路由文件。

**Tech Stack:** .NET 10、System.Text.Json、确定性文本编辑、MSTest、Vue Router、
Layui ESM controller registry。

## Global Constraints

- 不从 Schema 猜测组件路径、controller export、菜单、权限码或翻译键。
- 动态导航仍只能映射到这两份本地静态白名单；服务端授权语义不变。
- `clientRoute` 是可选兼容扩展；旧目标 JSON 不得被迫补字段。
- 所有路径必须是仓库内可移植相对路径，路由和名称必须是稳定小写机器码。
- Apply 只修改显式 `vueRouterPath` 与 `layuiRouterPath`，失败必须零写入或恢复首文件。
- 不修改当前真实 Vue/Layui 路由文件；验证使用临时仓库夹具。

---

### Task 1: 显式客户端路由契约

**Files:**

- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationTarget.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationTargetDocument.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/ModuleIntegrationPlannerTests.cs`

**Interfaces:**

- Produces: `ModuleClientRouteTarget.Create(...)`
- Produces: `ModuleIntegrationTarget.ClientRoute`
- Consumes: `GenerationArtifactPath.Validate(...)`

- [x] 先增加有效可选描述符、非法路由、非法 export 与不安全路径测试并观察 RED。
- [x] 实现严格 JSON 映射和稳定机器码/路径校验。
- [x] 复跑目标契约测试并确认 GREEN。

### Task 2: 双端纯内存编辑器与规划状态

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ClientRouteIntegrationEditors.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/ClientRouteIntegrationEditorTests.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationPlanner.cs`

**Interfaces:**

- Produces: `VueRouteIntegrationEditor.Edit(...)`
- Produces: `LayuiRouteIntegrationEditor.Edit(...)`
- Produces: `ClientRouteIntegrationEditResult`

- [x] 先覆盖标准插入、二次幂等、注释/字符串诱饵、重复 route/name 和非标准形态拒绝。
- [x] 运行测试，确认因编辑器缺失而 RED。
- [x] 实现最小编辑器；只按路由文件与适配文件的仓库相对路径计算静态相对 import。
- [x] 让规划器在无描述符时保持 `ManualReview`，适配文件缺失时 `Blocked`，
  可接入时 `ChangeRequired`，精确接入后 `Satisfied`。
- [x] 复跑编辑器和规划器测试并确认 GREEN。

### Task 3: 可恢复双文件 Apply 与 CLI

**Files:**

- Create: `src/Tools/Full.NET.CodeGeneration.Cli/ClientRouteIntegrationApplyCommand.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationPlanCommand.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/CodeGeneration/ModuleIntegrationBackendApplyTests.cs`

**Interfaces:**

- Produces: CLI `apply-client-route-integration`
- Requires: `apply-module-integration` →
  `apply-module-entry-integration` → `apply-composition-integration`

- [x] 先增加缺失描述符 Unit 与完整四阶段 Integration 测试并观察 RED。
- [x] 实现前置接线复核、适配文件存在/导出校验、候选结构验证和双端锁内复核。
- [x] 先 staging 两文件，再按 Vue → Layui 提交；第二文件失败必须回滚 Vue，
  回滚失败必须保留 recovery。
- [x] CLI 输出每个路由文件的 `Update`/`Unchanged`，二次运行不得重写。
- [x] 复跑 CodeGeneration Unit 与聚焦 Integration 并确认 GREEN。

### Task 4: 文档和分层验证

**Files:**

- Create: `docs/verification/codegeneration-dual-admin-route-apply-2026-07-30.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify only if discovery changes:
  `eng/testing/test-matrix.json`

- [x] 记录显式描述符、安全边界、失败恢复和仍开放的可视化页面/菜单能力。
- [x] 运行任务快照 affected inner，不在本地升级到完整 Integration。
- [x] 运行 Vue typecheck、Layui build、Release solution build、全量 Unit、
  测试发现、矩阵契约和 `git diff --check`。
- [x] 只按新鲜 discovery 更新测试矩阵；检查规则/Skill 演进触发条件。
