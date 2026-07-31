# CodeGeneration Migration And Integration Test Templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为作用域明确的 CRUD Schema 生成 SQL Server/MySQL 成对建表模板和最小数据库集成测试模板，缩短从生成骨架到正式模块迁移的人工路径。

**Architecture:** 新增独立的 `CrudMigrationTemplateGenerator`，只负责从冻结 Schema 渲染不可直接被 DbUp 自动发现的 `templates/` 草案。`CrudArtifactGenerator` 负责在 `DataScope` 明确时组合三项新产物；迁移编号、正式目录落位和恢复语义仍由模块开发者在评审后确认，生成器不猜测全仓库序列。

**Tech Stack:** .NET 10、C# raw string、MSTest、Dapper、SQL Server、MySQL、现有 Naming Profile。

## Global Constraints

- SQL Server 与 MySQL 模板必须成对生成，并使用相同表、列、主键和索引名称。
- SQL Server UUID 使用 `uniqueidentifier`；MySQL UUID 使用 `BINARY(16)`。
- SQL Server 主键必须显式声明 `CLUSTERED` 或 `NONCLUSTERED`。
- 租户表必须生成 `(TenantId, Id)` 分页索引；Host/Global 不生成租户列或租户索引。
- `Unspecified` 作用域不得生成可执行数据库或集成测试骨架。
- 模板位于 `templates/`，不得自动写入正式 DbUp 迁移目录或猜测迁移编号。
- 不修改 rules、Skills、CI 或完整 Integration 门禁。

---

### Task 1: Migration template contract

**Files:**
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudMigrationTemplateGenerator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudArtifactGenerator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GeneratedArtifact.cs`

**Interfaces:**
- Consumes: `FullNetCrudSchema`, `FullNetColumn`, `DatabaseObjectNameBuilder.Build(string)`.
- Produces: `GenerateSqlServer(FullNetCrudSchema)`, `GenerateMySql(FullNetCrudSchema)` and `GenerateIntegrationTest(FullNetCrudSchema)`.

- [ ] **Step 1: Write the failing artifact and DDL tests**

```csharp
CollectionAssert.Contains(paths, "templates/migrations/SqlServer/CreateProduct.sql.template");
CollectionAssert.Contains(paths, "templates/migrations/MySql/CreateProduct.sql.template");
StringAssert.Contains(sqlServer, "Id uniqueidentifier NOT NULL");
StringAssert.Contains(mySql, "Id BINARY(16) NOT NULL");
StringAssert.Contains(sqlServer, "PRIMARY KEY NONCLUSTERED (Id)");
StringAssert.Contains(sqlServer, "ON dbo.acme_catalog_product(TenantId, Id)");
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CrudArtifactGeneratorTests" --no-restore
```

Expected: the new template paths are absent.

- [ ] **Step 3: Implement deterministic paired DDL rendering**

Map logical types explicitly:

```text
Uuid        -> uniqueidentifier / BINARY(16)
String      -> nvarchar(MaxLength) / varchar(MaxLength)
Int32       -> int / int
Int64       -> bigint / bigint
Boolean     -> bit / boolean
DateTimeUtc -> datetimeoffset(7) / datetime(6)
Decimal     -> visible precision-and-scale review token in the non-executable template
```

Use `DatabaseObjectNameBuilder` for `PK_` and `IX_` names. Tenant SQL Server templates use a nonclustered primary key plus a clustered `(TenantId, Id)` index; Host/Global templates use a clustered primary key.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 1 test command and expect all `CrudArtifactGeneratorTests` to pass.

### Task 2: Minimal integration test template

**Files:**
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudMigrationTemplateGenerator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudArtifactGenerator.cs`

**Interfaces:**
- Consumes: the provider-paired DDL contract from Task 1.
- Produces: `templates/tests/ProductMigrationIntegrationTests.cs.template`.

- [ ] **Step 1: Write the failing integration-template test**

```csharp
StringAssert.Contains(template, "CreateSqlServerDatabaseAsync()");
StringAssert.Contains(template, "CreateMySqlDatabaseAsync()");
StringAssert.Contains(template, "TABLE_NAME = 'acme_catalog_product'");
StringAssert.Contains(template, "Assert.AreEqual(7,");
```

- [ ] **Step 2: Run the focused test and verify RED**

Run the Task 1 test command and expect the integration template path to be absent.

- [ ] **Step 3: Generate a compilable copy template**

Generate an MSTest class that uses the existing `SharedDatabaseFixture`, opens each provider connection, and checks table existence plus exact column count. The template must tell the adopter to assign a real migration number and remove the `.template` suffix; it must not execute or copy files itself.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 1 test command and expect all focused tests to pass.

### Task 3: Fixtures, report and scoped verification

**Files:**
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/templates/migrations/SqlServer/CreateProduct.sql.template`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/templates/migrations/MySql/CreateProduct.sql.template`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/templates/tests/ProductMigrationIntegrationTests.cs.template`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/reports/products.generation.json`
- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`

**Interfaces:**
- Consumes: the complete artifact set from Tasks 1-2.
- Produces: byte-stable fixtures and truthful roadmap status.

- [ ] **Step 1: Update fixture expectations and generated report**

The report records:

```json
{
  "migrationTemplateGenerated": true,
  "integrationTestTemplateGenerated": true
}
```

- [ ] **Step 2: Run focused and complete unit tests**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGeneration" --no-restore
pnpm test:dotnet:unit
```

Expected: zero failures; update only `eng/testing/test-matrix.json` if the discovered unit count increases.

- [ ] **Step 3: Run naming and affected dual-provider verification**

```powershell
pnpm test:naming
pnpm test:integration:affected:plan -- --snapshot codegeneration-migration-test-templates-20260730 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-migration-test-templates-20260730 --phase slice
```

Expected: naming and the selected CodeGeneration dual-provider set pass.

- [ ] **Step 4: Run final repository checks**

```powershell
dotnet build src/BuildingBlocks/Full.NET.Data.CodeGeneration/Full.NET.Data.CodeGeneration.csproj -c Release --no-restore
dotnet build src/Tools/Full.NET.CodeGeneration.Cli/Full.NET.CodeGeneration.Cli.csproj -c Release --no-restore
git diff --check
git status --short --branch
```

Expected: builds and diff check succeed; existing unrelated dirty files remain untouched.

## Self-Review

- Spec coverage: provider pairing, UUID storage, tenant index, explicit scope gating, non-executable paths, integration scaffold and focused verification all map to Tasks 1-3.
- Placeholder scan: the plan contains no deferred implementation steps; the Decimal review token is an intentional safety feature of a `.template` artifact because current Schema lacks precision/scale.
- Type consistency: all three generator methods consume `FullNetCrudSchema`; artifact paths and test expectations use the same `CreateProduct` and `ProductMigrationIntegrationTests` names.
