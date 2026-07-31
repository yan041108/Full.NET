# CodeGeneration Database Batch Preview Implementation Plan

> **执行方式：** 当前任务在同一工作区内按测试先行逐步实现；不创建工作树、不启动子代理。

**目标：** 在已有数据库表目录和单表导入能力之上，增加“显式逐表映射、一次连接导入、合并工作区预览”的批量命令，使开发者可以先审查整批生成影响，再决定后续是否逐项应用。

**架构：** CLI 读取严格 JSON 映射文件，映射文件只保存业务语义，不保存连接串或工作区路径。命令通过一个数据库连接依次导入显式列出的物理表，再把所有 Schema 的生成产物合并为一个工作区计划。合并计划统一计算现有文件和旧清单，避免逐表规划时把同批其他表的产物误判为陈旧文件。

**技术栈：** .NET 10、System.Text.Json、ADO.NET、Microsoft.Data.SqlClient、MySqlConnector、MSTest、SQL Server/MySQL Testcontainers。

## 约束

- 命令固定为只读预览，不接受 `--apply`，不写入生成工作区。
- 连接串只通过 `--connection-env` 指向的环境变量读取，映射文件中禁止保存连接信息。
- 每张表必须显式提供 Owner、Module、Entity、根命名空间、CLR 类型、API 资源、权限资源、数据作用域和版本语义。
- 不从带下划线的物理表名推断 `{owner}_{module}_{entity}`，不自动猜测租户或版本语义。
- 所有 Schema 的产物必须合并后只调用一次工作区规划。
- 重复物理表映射、重复生成路径、未知 JSON 字段、整数枚举和空映射必须失败。
- 本切片不增加批量 Apply、不修改正式迁移、不扩充规则或 Skill、不运行本地完整 Integration。

## Task 1：冻结合并预览的工作区语义

**文件：**

- 修改：`tests/Full.NET.UnitTests/CodeGeneration/CrudGenerationWorkspaceTests.cs`
- 修改：`src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudGenerationWorkspace.cs`

- [x] 先写两个 Schema 合并后产生 26 个 Create 且工作区零写入的失败测试。
- [x] 写重复输出路径在统一规划阶段被拒绝的失败测试。
- [x] 增加多 Schema `PlanAsync`，单 Schema 重载委托给它；不增加批量 `ApplyAsync`。
- [x] 运行 `CrudGenerationWorkspaceTests` 并确认通过。

## Task 2：冻结严格映射和 CLI 只读契约

**文件：**

- 新增：`src/Tools/Full.NET.CodeGeneration.Cli/DatabaseBatchPreviewCliOptions.cs`
- 新增：`src/Tools/Full.NET.CodeGeneration.Cli/DatabaseBatchMappingDocument.cs`
- 修改：`src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- 修改：`tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`

- [x] 先写空表集合、未知 JSON 字段和 `--apply` 被拒绝的失败测试。
- [x] 实现 `preview-database-batch` 的严格参数解析和 Usage。
- [x] 实现无 BOM、未知成员拒绝、大小写敏感、字符串枚举且禁止整数枚举的映射加载。
- [x] 验证显式作用域和物理表唯一性，并保持错误输出不泄露环境变量名或连接串。
- [x] 运行 `CodeGenerationCliTests` 并确认通过。

## Task 3：一次连接导入并生成统一计划

**文件：**

- 新增：`src/Tools/Full.NET.CodeGeneration.Cli/DatabaseBatchPreviewCommand.cs`
- 修改：`src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`

- [x] 命令按 Provider 创建并只打开一个连接。
- [x] 依次调用现有 `DatabaseCrudSchemaImporter` 导入显式映射。
- [x] 将导入结果一次性交给多 Schema `PlanAsync`，输出统一排序的计划并返回既有退出码。
- [x] 保持取消传播，其他驱动异常只向 CLI 暴露稳定错误边界。

## Task 4：双数据库真实验证

**文件：**

- 新增：`tests/Full.NET.IntegrationTests/CodeGeneration/DatabaseBatchPreviewCliIntegrationTests.cs`

- [x] SQL Server 和 MySQL 各创建两张满足不变量的基础表。
- [x] 使用同一映射文件导入两张表，断言两套产物均出现在预览中。
- [x] 断言退出码为 0、stderr 为空、工作区保持空目录。
- [x] 运行该双 Provider 测试并确认通过。

## Task 5：影响集收口

- [x] 运行 CodeGeneration 聚焦单元测试。
- [x] 运行 CLI/Data.CodeGeneration Release 构建。
- [x] 使用快照 `codegeneration-database-batch-preview-20260730` 运行 inner 选择计划和 slice 影响集。
- [x] 根据新鲜发现数量更新唯一测试矩阵；只在能力真实变化时更新路线图。
- [x] 运行 `git diff --check`、`git status --short --branch`，确认未混入无关变更。
- [x] 规则演进结论：未出现新的重复失败类别或规则冲突，不更新规则。
- [x] Skill 演进结论：未暴露项目 Skill 的真实缺口，不更新 Skill。
