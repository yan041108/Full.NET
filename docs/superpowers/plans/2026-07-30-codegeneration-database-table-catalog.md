# CodeGeneration Database Table Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 CodeGeneration 增加只读的 SQL Server/MySQL 基础表目录扫描命令，让开发者在单表导入前确定性查看当前数据库中的候选物理表。

**Architecture:** `Full.NET.Data.CodeGeneration` 新增只依赖 `DbConnection` 的 provider-neutral 表目录读取器，固定扫描 SQL Server `dbo` 或 MySQL 当前数据库中的 `BASE TABLE`。CLI 新增 `list-database-tables` 薄适配命令，只从环境变量取得连接串并输出按 ordinal 排序的表名，不推断模块、实体、作用域或生成契约，也不触发工作区规划/写盘。

**Tech Stack:** .NET 10、ADO.NET、Microsoft.Data.SqlClient、MySqlConnector、MSTest、SQL Server/MySQL Testcontainers。

## Global Constraints

- 数据库访问严格只读，只查询 `INFORMATION_SCHEMA.TABLES`。
- SQL Server 固定扫描 `dbo`；MySQL 固定扫描 `DATABASE()`。
- 只返回 `BASE TABLE`，排除视图。
- 输出格式固定为每行 `Table <physical-name>`，并按 `StringComparer.Ordinal` 排序。
- 连接串只通过 `--connection-env` 间接读取，stderr 不输出连接串、驱动消息或堆栈。
- 命令不接受 `--workspace` 或 `--apply`，不创建、修改或删除任何工作区文件。
- 本切片不推断有歧义的 `{owner}_{module}_{entity}` 分段，不进行批量 CRUD 生成。
- 不修改正式迁移、规则、Skills 或 CI；完整 Integration 仍只由 `main` CI 执行。

---

### Task 1: Freeze the CLI catalog contract

**Files:**
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- Create: `src/Tools/Full.NET.CodeGeneration.Cli/DatabaseCatalogCliOptions.cs`

**Interfaces:**
- Produces: `DatabaseCatalogCliOptions(DatabaseMetadataProvider Provider, string ConnectionEnvironmentVariable)`.
- Extends: `CodeGenerationCli.RunAsync(...)` with `list-database-tables`.

- [x] **Step 1: Write the missing-environment RED**

```csharp
var exitCode = await CodeGenerationCli.RunAsync(
    [
        "list-database-tables",
        "--provider",
        "sqlserver",
        "--connection-env",
        missingEnvironmentVariable,
    ],
    output,
    error);

Assert.AreEqual(64, exitCode);
StringAssert.Contains(error.ToString(), "--connection-env 指向的环境变量不存在或为空。");
```

Also pass `--workspace` and assert a usage error, proving the read-only command cannot enter generation planning.

- [x] **Step 2: Run the focused unit test and verify RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGenerationCliTests" --no-restore
```

Expected: the command is rejected as an unknown argument rather than reaching the missing-environment boundary.

- [x] **Step 3: Implement strict parsing**

Add the usage contract:

```text
fullnet-codegen list-database-tables --provider <sqlserver|mysql>
  --connection-env <environment-variable>
```

Parse each value exactly once, reject every other option, reuse the existing closed provider parser, and represent this mode separately from `DatabaseImportCliOptions`.

- [x] **Step 4: Run the focused unit test and verify GREEN**

Repeat Task 1 Step 2 and require zero failures.

### Task 2: Read a deterministic provider-neutral table catalog

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseTableCatalogReader.cs`
- Create: `src/Tools/Full.NET.CodeGeneration.Cli/DatabaseTableCatalogCommand.cs`
- Create: `tests/Full.NET.IntegrationTests/CodeGeneration/DatabaseTableCatalogCliIntegrationTests.cs`

**Interfaces:**
- Produces: `DatabaseTableCatalogReader.ListAsync(DbConnection connection, DatabaseMetadataProvider provider, CancellationToken cancellationToken = default)`.
- Produces: `DatabaseTableCatalogCommand.ListAsync(DatabaseCatalogCliOptions options, string connectionString, CancellationToken cancellationToken)`.

- [x] **Step 1: Write real SQL Server/MySQL RED**

In each isolated database create `acme_sales_order`, `acme_catalog_product`, and a view over one table. Invoke the CLI and assert:

```text
Table acme_catalog_product
Table acme_sales_order
```

The view must not appear, stderr must be empty, and the exit code must be `0`.

- [x] **Step 2: Run the dual-provider test and verify RED**

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~DatabaseTableCatalogCliIntegrationTests" --no-restore
```

Expected: compilation fails because the catalog command and reader do not exist.

- [x] **Step 3: Implement the read-only reader and adapter**

Use fixed SQL:

```sql
SELECT TABLE_NAME AS TableName
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = 'dbo' AND TABLE_TYPE = 'BASE TABLE'
```

and:

```sql
SELECT TABLE_NAME AS TableName
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
```

Read names through `DbDataReader`, de-duplicate with `StringComparer.Ordinal`, and return ordinal-sorted results. The CLI adapter owns and disposes the concrete provider connection; cancellation propagates, while other driver failures are wrapped so stderr receives only a stable failure type.

- [x] **Step 4: Run focused unit and dual-provider GREEN**

Run the Task 1 and Task 2 commands and require all selected tests to pass with both providers discovered.

### Task 3: Close the catalog slice

**Files:**
- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`

**Interfaces:**
- Consumes: the stable command and fresh test discovery totals.
- Produces: truthful roadmap evidence that table catalog scanning exists while bulk schema generation remains open.

- [x] **Step 1: Update canonical evidence**

Update only the observed test minimums in `eng/testing/test-matrix.json`. Record that provider-neutral base-table catalog listing is implemented; keep semantic mapping, per-table overrides, batch preview/apply, visual management, and automatic module hookup as open work.

- [x] **Step 2: Run layered verification**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGeneration" --no-restore
pnpm test:dotnet:unit
pnpm test:naming
dotnet build src/BuildingBlocks/Full.NET.Data.CodeGeneration/Full.NET.Data.CodeGeneration.csproj -c Release --no-restore
dotnet build src/Tools/Full.NET.CodeGeneration.Cli/Full.NET.CodeGeneration.Cli.csproj -c Release --no-restore
pnpm test:integration:affected:plan -- --snapshot codegeneration-database-scan-20260730 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-database-scan-20260730 --phase slice
git diff --check
git status --short --branch
```

Expected: CodeGeneration、完整 Unit、Naming、Release 构建和受影响的 SQL Server/MySQL 目录测试全部通过；完整 Integration 不在本地执行。

## Self-Review

- Spec coverage: 只读、默认 Schema、仅基础表、稳定排序、双 Provider、环境变量连接串、无工作区写入与真实数据库验证均有对应任务。
- Placeholder scan: 无 TBD、TODO、模糊异常处理或未定义接口。
- Type consistency: 两层均复用 `DatabaseMetadataProvider`；内核返回 `IReadOnlyList<string>`，CLI 只格式化输出，不引入额外 DTO。
