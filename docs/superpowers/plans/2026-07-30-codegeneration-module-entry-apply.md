# CodeGeneration 模块入口显式接线实施计划

> **执行方式：** 当前共享脏工作区内由主任务直接执行；沿用任务快照
> `codegeneration-module-entry-apply-20260730`，不创建 worktree、不委派子任务。

**目标：** 增加独立的模块入口接线命令，在不猜测 Composition 或前端路由的前提下，把已经生成并受清单保护的模块聚合桥显式接入 `IFullNetModule`。

**安全边界：**

- 只修改目标 JSON 明确声明的 `moduleEntryPointPath`。
- 只接受文件作用域命名空间、唯一且块体形式的 `AddServices` 与 `MapEndpoints`。
- 注释、字符串、表达式体、重复或缺失方法均不得被猜测性改写。
- 写盘前先用临时源码替换真实入口执行目标模块 Release 编译；失败时零写入。
- 入口文件不纳入生成器 Manifest，避免生成器取得手写模块入口的长期所有权。
- 使用独立命令触发写盘；已有 plan、validate、backend apply 命令语义不变。

## 任务 1：建立纯源码改写契约

**文件：**

- 新增：`src/Tools/Full.NET.CodeGeneration.Cli/ModuleEntryIntegrationEditor.cs`
- 新增：`tests/Full.NET.UnitTests/CodeGeneration/ModuleEntryIntegrationEditorTests.cs`

**步骤：**

1. 先写失败测试，覆盖正常块体、幂等、注释/字符串诱饵和不安全语法拒绝。
2. 实现保守词法扫描和精确插入。
3. 只返回内存计划，不访问文件系统。

## 任务 2：增加候选入口编译投影

**文件：**

- 修改：`src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationBuildProjection.cs`
- 修改：`src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationCompilationCommand.cs`
- 修改：`tests/Full.NET.UnitTests/CodeGeneration/ModuleIntegrationBuildProjectionTests.cs`

**步骤：**

1. 允许投影显式替换一个手写入口候选，同时继续引用模块中已存在的生成产物。
2. 确认原入口被移除、候选入口被加入、编译产物仍完全位于临时目录。

## 任务 3：实现显式写盘命令

**文件：**

- 新增：`src/Tools/Full.NET.CodeGeneration.Cli/ModuleEntryIntegrationApplyCommand.cs`
- 修改：`src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- 修改：`src/Tools/Full.NET.CodeGeneration.Cli/ModuleIntegrationPlanCommand.cs`
- 修改：`tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`
- 新增：`tests/Full.NET.IntegrationTests/CodeGeneration/ModuleEntryIntegrationApplyTests.cs`

**步骤：**

1. 增加 `apply-module-entry-integration --schema --repository --target`。
2. 校验生成聚合桥仍受 Manifest 所有且内容未漂移。
3. 对入口候选执行 Release 编译门禁。
4. 在仓库锁、内容哈希复核和同目录临时文件保护下替换入口。
5. 验证首次写入、二次幂等、失败零写入和无仓库 `bin/obj` 污染。

## 任务 4：收敛文档与验证

**文件：**

- 修改：相关 CodeGeneration README/路线图状态
- 仅在发现数真实变化时修改：`eng/testing/test-matrix.json`

**验证：**

1. 定向 Unit 与 Integration。
2. CodeGeneration Unit/Integration。
3. `dotnet build Full.NET.slnx -c Release --no-restore`。
4. 任务快照 affected inner。
5. `git diff --check`、`git status --short --branch`。
