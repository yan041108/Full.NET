# CodeGeneration Database Import CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为现有 CodeGeneration CLI 增加安全的 `import-database` 模式，从 SQL Server 或 MySQL 单表元数据生成 CRUD 工作区计划，并继续保持默认只预览、显式 `--apply` 才写盘。

**Architecture:** CLI 保持为 `Full.NET.Data.CodeGeneration` 的薄适配器。新增命令只负责解析显式参数、从指定环境变量读取连接串、创建具体 Provider 连接并调用现有 `DatabaseCrudSchemaImporter`；产物规划、冲突检测和原子写盘继续复用 `CrudGenerationWorkspace`。

**Tech Stack:** .NET 10、ADO.NET、Microsoft.Data.SqlClient、MySqlConnector、MSTest、现有 Full.NET CodeGeneration API

## Global Constraints

- 现有 `--schema <json-file> --workspace <directory> [--apply]` 契约保持兼容。
- 数据库模式固定为 `import-database`；Provider 只接受 `sqlserver` 或 `mysql`。
- 连接串只能通过 `--connection-env <environment-variable>` 间接读取，禁止接受或输出明文连接串。
- Owner、Module、Entity、CLR/API/权限名称、租户作用域和版本语义全部显式提供，不从数据库猜测。
- 默认只调用 `CrudGenerationWorkspace.PlanAsync`；只有显式 `--apply` 才调用 `ApplyAsync`。
- 数据库访问保持只读，继续限定 SQL Server `dbo` 与 MySQL 当前数据库的单表元数据。
- 共享脏工作区不执行暂存或提交，只使用任务快照确定本切片影响集。

---

### Task 1: CLI 参数与敏感信息边界

**Files:**
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- Create: `src/Tools/Full.NET.CodeGeneration.Cli/DatabaseImportCliOptions.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`

**Interfaces:**
- Consumes: 现有 `CodeGenerationCli.RunAsync(...)`。
- Produces: `DatabaseImportCliOptions`，保存 Provider、环境变量名、生成命名、布尔语义和工作区参数。

- [ ] **Step 1: Write argument/security RED**

新增测试，使用完整 `import-database` 参数但不存在的唯一环境变量，断言返回 `64`、工作区不写入，并且错误不包含伪造连接串。再传入禁止的 `--connection-string`，断言返回 `64` 且错误不回显其后的 secret。

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGenerationCliTests"
```

Expected: 新测试因 `import-database` 尚未识别而失败。

- [ ] **Step 3: Implement strict parsing**

命令契约固定为：

```text
fullnet-codegen import-database
  --provider <sqlserver|mysql>
  --connection-env <environment-variable>
  --owner-key <value>
  --module-key <value>
  --entity-key <value>
  --root-namespace <value>
  --clr-type <value>
  --api-resource <value>
  --permission-resource <value>
  --tenant-scoped <true|false>
  --has-version <true|false>
  --workspace <existing-directory>
  [--apply]
```

所有值参数必须恰好出现一次；布尔值只接受 `true`/`false`；缺失环境变量按用法错误返回 `64`。错误输出只说明参数或环境变量缺失，不输出读取到的值。

- [ ] **Step 4: Verify GREEN**

重复 Step 2 命令，要求现有 Schema 模式和新增失败路径全部通过。

### Task 2: 双 Provider 连接与真实 CLI 纵向链路

**Files:**
- Create: `src/Tools/Full.NET.CodeGeneration.Cli/DatabaseCrudImportCommand.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/Full.NET.CodeGeneration.Cli.csproj`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/Properties/AssemblyInfo.cs`
- Modify: `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj`
- Create: `tests/Full.NET.IntegrationTests/CodeGeneration/DatabaseImportCliIntegrationTests.cs`

**Interfaces:**
- Consumes: `DatabaseCrudSchemaImporter.ImportAsync(...)` 和 `CrudGenerationWorkspace.PlanAsync/ApplyAsync(...)`。
- Produces: `DatabaseCrudImportCommand.ImportAsync(DatabaseImportCliOptions options, string connectionString, CancellationToken cancellationToken)`。

- [ ] **Step 1: Write real dual-provider RED**

在 SQL Server 与 MySQL 隔离数据库中分别创建 `acme_catalog_product`，把连接串放入每个测试独有的环境变量，调用公开 `CodeGenerationCli.RunAsync(...)`。断言两种 Provider 都返回 `0`、报告生成动作，且默认预览不会写工作区文件。

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~DatabaseImportCliIntegrationTests"
```

Expected: 测试因连接适配器尚未实现而失败。

- [ ] **Step 3: Implement minimal connection adapter**

`DatabaseCrudImportCommand` 根据封闭 Provider 枚举创建 `SqlConnection` 或 `MySqlConnection`，异步打开后调用现有 importer。连接由命令释放；异常继续由 CLI 统一映射，stderr 只输出稳定分类和异常类型，不输出异常消息、连接串或堆栈。

- [ ] **Step 4: Verify GREEN**

重复 Step 2 命令，要求 SQL Server/MySQL 均通过且无跳过。

### Task 3: Matrix, roadmap, and layered verification

**Files:**
- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`

**Interfaces:**
- Consumes: Tasks 1–2 的稳定命令与测试证据。
- Produces: 唯一测试门槛和如实的 CodeGeneration 能力状态。

- [ ] **Step 1: Update canonical counts and status**

只把实际新增的 Unit/Infrastructure/Full 最低发现数写入 `eng/testing/test-matrix.json`。路线图把“元数据导入 CLI”移入已完成证据，继续保留整库扫描、模块/页面模板和完整业务纵向生成作为开放项。

- [ ] **Step 2: Run layered verification**

```powershell
pnpm test:dotnet:unit
pnpm test:naming
pnpm test:integration:affected:plan -- --snapshot codegeneration-database-import-cli-20260730 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-database-import-cli-20260730 --phase slice
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-build
dotnet build Full.NET.slnx -c Release --no-restore
git diff --check
```

Expected: Unit、Naming、CodeGeneration 双库影响集、Architecture、Release 构建和静态检查全部通过；完整 Integration 仍只留给 `main` CI。

## Self-Review

- Spec coverage: 向后兼容、连接串间接读取、显式语义、默认预览、显式写盘、双 Provider、真实数据库和敏感信息边界均有对应测试与实现任务。
- Placeholder scan: 无 TBD、TODO、模糊错误处理或未定义步骤。
- Type consistency: CLI 参数转换为现有 `DatabaseCrudImportOptions`，importer 继续返回 `FullNetCrudSchema`，工作区只消费该现有类型。
