# CodeGeneration Database Schema Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 从已打开的 SQL Server 或 MySQL 连接只读导入一个默认 Schema 表，并转换为经过 Naming Profile 和 CRUD 不变量校验的 `FullNetCrudSchema`。

**Architecture:** `Full.NET.Data.CodeGeneration` 只依赖 BCL `DbConnection`，不引用具体数据库驱动或 Dapper。调用方显式提供所有稳定逻辑名称；导入器参数化读取列和主键元数据，执行封闭类型映射，再复用 `FullNetCrudSchema.CreateProject` 完成最终校验。

**Tech Stack:** .NET 10、ADO.NET、SQL Server `INFORMATION_SCHEMA`、MySQL `INFORMATION_SCHEMA`、MSTest、Testcontainers。

## Global Constraints

- 数据库访问严格只读，不生成或执行 DDL/DML。
- 首个切片只支持 SQL Server `dbo` 与 MySQL 当前数据库，不扫描整库。
- 表名由 `OwnerKey + ModuleKey + EntityKey` 计算，禁止接受任意 SQL 标识符。
- 主键必须精确为单列 `Id`；租户和版本不变量继续由现有 Schema 校验。
- 只支持现有 `FullNetScalarType` 可无损表达的类型；未知或歧义类型失败关闭。
- 不记录连接串，不在异常中暴露数据库地址或凭据。
- 本切片不增加 CLI 子命令、页面模板、迁移或模块项目。

---

### Task 1: Provider-neutral metadata mapper

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseMetadataProvider.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseCrudImportOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseColumnMetadata.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseColumnMetadataMapper.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/DatabaseColumnMetadataMapperTests.cs`

**Interfaces:**
- Produces: `DatabaseMetadataProvider.SqlServer|MySql`.
- Produces: `DatabaseCrudImportOptions(...)`，保存全部显式命名与 `IsTenantScoped`、`HasVersion`。
- Produces: `DatabaseColumnMetadataMapper.Map(DatabaseMetadataProvider provider, IReadOnlyList<DatabaseColumnMetadata> columns)`.

- [ ] **Step 1: Write mapper RED**

测试 SQL Server `uniqueidentifier/nvarchar/bit/bigint/datetimeoffset` 与 MySQL `binary(16)/varchar/tinyint(1)/bigint/datetime` 映射到相同列集合，并覆盖可空性、字符串长度和 ordinal 排序。

```csharp
var columns = DatabaseColumnMetadataMapper.Map(
    DatabaseMetadataProvider.MySql,
    [
        new("Id", "binary", "binary(16)", false, 16, 1),
        new("Name", "varchar", "varchar(200)", false, 200, 2),
        new("IsActive", "tinyint", "tinyint(1)", false, null, 3),
    ]);

Assert.AreEqual(FullNetScalarType.Uuid, columns[0].ScalarType);
Assert.AreEqual(FullNetScalarType.Boolean, columns[2].ScalarType);
```

同时断言 MySQL `binary(32)`、普通 `tinyint`、SQL Server `varbinary`、无界或超出 `Int32` 的字符串长度明确抛出 `NotSupportedException`。

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DatabaseColumnMetadataMapperTests"
```

Expected: 编译失败，因为 metadata 类型与 mapper 尚不存在。

- [ ] **Step 3: Implement the minimal mapper**

按 Provider 使用 ordinal、大小写不敏感的封闭映射：

```text
SQL Server: uniqueidentifier, varchar/nvarchar/char/nchar, int, bigint, bit,
            datetime/datetime2/datetimeoffset, decimal/numeric
MySQL:      binary(16), varchar/char, int/integer, bigint, tinyint(1),
            datetime/timestamp, decimal/numeric
```

CLR/JSON 名称只在数据库列已经符合 Naming Profile 时按 `Name -> Name/name` 的确定性首字符规则产生；最终合法性仍由 `FullNetCrudSchema` 校验。

- [ ] **Step 4: Verify GREEN**

Run Task 1 Step 2 command and require all mapper tests to pass.

### Task 2: Read table metadata and produce a validated schema

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseCrudSchemaImporter.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseCrudSchemaAssembler.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/DatabaseCrudSchemaAssemblerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj`
- Create: `tests/Full.NET.IntegrationTests/CodeGeneration/DatabaseCrudSchemaImporterIntegrationTests.cs`
- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`

**Interfaces:**
- Produces: `DatabaseCrudSchemaImporter.ImportAsync(DbConnection connection, DatabaseMetadataProvider provider, DatabaseCrudImportOptions options, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write assembler RED**

直接使用 metadata 记录验证 assembler 保持列顺序、要求单列 `Id` 主键，并将 mapper 结果交给 `FullNetCrudSchema.CreateProject`。覆盖表不存在、复合主键和无主键。ADO 参数化查询与取消传播由真实双库测试验证，避免大型假 `DbConnection` 只测试替身。

- [ ] **Step 2: Verify importer RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DatabaseCrudSchemaAssemblerTests"
```

Expected: 编译失败，因为 assembler 尚不存在。

- [ ] **Step 3: Implement parameterized metadata reads**

assembler 只接受 metadata 和主键记录并返回验证后的 Schema。importer 的列查询固定投影 `ColumnName/DataType/ColumnType/IsNullable/MaxLength/OrdinalPosition`；主键查询固定投影 `ColumnName/OrdinalPosition`。SQL Server 固定 `TABLE_SCHEMA = 'dbo'`，MySQL 固定 `TABLE_SCHEMA = DATABASE()`，两者只通过参数 `@TableName` 接收计算后的表名。

- [ ] **Step 4: Verify importer GREEN**

运行 Task 2 Step 2 命令并要求全部通过。

- [ ] **Step 5: Add real dual-provider integration RED/GREEN**

在 SQL Server/MySQL 隔离数据库中各创建同构的 `acme_catalog_product` 测试表，调用同一 importer，并断言列顺序、类型、可空性、长度、租户/版本和稳定权限码一致。测试 DDL 仅存在于隔离测试数据库，不进入迁移。

Run:

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~DatabaseCrudSchemaImporterIntegrationTests"
```

Expected: SQL Server 与 MySQL 场景均通过且无跳过。

- [ ] **Step 6: Close the slice**

只更新 `eng/testing/test-matrix.json` 中实际增加的 Unit/Integration 最低发现数。路线图记录“provider-neutral 单表元数据导入 API”，继续保留 CLI 暴露、整库扫描和页面模板为开放项。

Run:

```powershell
pnpm test:naming
pnpm test:integration:affected:plan -- --snapshot codegeneration-database-schema-import-20260730 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-database-schema-import-20260730 --phase slice
dotnet build Full.NET.slnx -c Release --no-restore
git diff --check
git status --short --branch
```

Expected: Naming、双库影响集、Release 构建与静态检查通过；完整 Integration 仍只由 `main` CI 执行。

## Self-Review

- Spec coverage: 只读、单表、默认 Schema、精确主键、双库类型映射、命名复用、真实双库验证和敏感信息边界均有对应任务。
- Placeholder scan: 无 TBD、TODO、通配 SQL 或未定义的“适当处理”步骤。
- Type consistency: mapper 输出 `FullNetColumn`；importer 使用同一 Provider/Options 并只返回现有 `FullNetCrudSchema`。
