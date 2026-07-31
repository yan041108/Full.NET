# CodeGeneration Composition 显式接入实施计划

> **执行方式：** 当前共享脏工作区内由主任务直接执行；使用任务快照
> `codegeneration-composition-catalog-apply-20260730`，不创建 worktree、不委派子任务。

**目标：** 增加独立的 `apply-composition-integration` 命令，把显式目标模块的
项目引用和模块构造接入显式 Composition 项目，并以真实 Release 编译、并发复核和
可重入提交保护手写文件。

**边界：**

- 只修改目标 JSON 声明的 `compositionProjectPath` 与
  `compositionCatalogPath`。
- 目标模块后端聚合桥和模块入口必须已经完成；本命令不隐式代替前两条 Apply。
- Composition 项目只增加精确 `ProjectReference`，不改属性、包或其他引用。
- Catalog 只接受唯一标准 `CreateModules() => [ ... ];` 形态，并在列表尾部增加
  `new {ModuleName}Module(),`。
- 注释、字符串、重复声明、无法解析 XML 或非标准目录形态均保守拒绝。
- Composition 和模块候选使用临时 MSBuild 注入执行真实 Release 编译，失败零写入。
- 提交顺序为“项目引用 → Catalog”；项目引用单独存在仍可编译，进程中断后可安全重入。
- Vue/Layui 路由、菜单、权限和页面保持后续独立人工/自动化边界。

## 任务 1：纯编辑器

**新增：**

- `src/Tools/Full.NET.CodeGeneration.Cli/CompositionProjectEditor.cs`
- `src/Tools/Full.NET.CodeGeneration.Cli/CompositionCatalogEditor.cs`
- 对应 CodeGeneration Unit Tests

先建立正常、幂等、诱饵和非标准形态拒绝的失败测试，再实现最小文本插入。

## 任务 2：临时 Composition 编译投影

**新增/修改：**

- `src/Tools/Full.NET.CodeGeneration.Cli/CompositionIntegrationCompilationCommand.cs`
- `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationBuildProjection.cs`
- 对应投影 Unit Tests

投影必须在系统临时目录加入模块 `ProjectReference`、移除真实 Catalog 并注入候选
Catalog；所有 build artifacts 必须定向到临时目录。

## 任务 3：显式命令与可重入提交

**新增/修改：**

- `src/Tools/Full.NET.CodeGeneration.Cli/CompositionIntegrationApplyCommand.cs`
- `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- `src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationPlanCommand.cs`
- CodeGeneration CLI Unit/Integration Tests

提交前必须再次复核模块入口、两个 Composition 原文和候选内容；并发变化或编译失败
不得写入。

## 任务 4：文档和聚焦验证

- 更新 CodeGeneration 路线图和验证记录。
- 仅按新鲜 discovery 更新 `eng/testing/test-matrix.json`。
- 运行 CodeGeneration Unit、Composition Integration、affected inner、
  Release build、`git diff --check` 和状态审计。
