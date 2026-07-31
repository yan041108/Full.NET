# CodeGeneration Decimal Shape Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Decimal 字段从数据库元数据、严格 JSON、冻结 Schema、生成报告到 SQL Server/MySQL 迁移草案完整保留相同的 precision/scale。

**Architecture:** `FullNetColumn` 增加可空 `NumericPrecision` 与 `NumericScale`，但 Schema 对 Decimal 强制要求两者，对非 Decimal 强制为空。数据库导入器读取两库 `NUMERIC_PRECISION`/`NUMERIC_SCALE`，迁移模板只渲染已验证的 `decimal(p, s)`，不再输出人工占位符。

**Tech Stack:** .NET 10、System.Text.Json、ADO.NET metadata reader、MSTest、SQL Server、MySQL。

## Global Constraints

- Decimal precision 必须为 `1..38`，以 SQL Server 与 MySQL 的共同可移植上限为准。
- Decimal scale 必须为 `0..precision`。
- 非 Decimal 字段禁止携带 NumericPrecision/NumericScale。
- SQL Server 与 MySQL 迁移草案必须渲染完全相同的 `decimal(p, s)`。
- 既有不含 Decimal 的 JSON 与构造调用保持兼容。
- 不修改正式迁移、rules、Skills 或 CI。

---

### Task 1: Freeze Decimal shape in FullNetColumn

**Files:**
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/FullNetCrudSchemaTests.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetColumn.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudSchema.cs`

**Interfaces:**
- Produces: `FullNetColumn.NumericPrecision` and `FullNetColumn.NumericScale` as `int?`.

- [x] **Step 1: Write failing validation tests**

```csharp
new FullNetColumn("Price", "Price", "price", FullNetScalarType.Decimal,
    NumericPrecision: 18, NumericScale: 2);
Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
    columns: columnsWithDecimalWithoutShape));
Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
    columns: columnsWithDecimalScaleGreaterThanPrecision));
```

- [x] **Step 2: Run the focused schema tests and verify RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~FullNetCrudSchemaTests" --no-restore
```

Expected: constructor arguments or properties do not exist.

- [x] **Step 3: Implement the frozen invariant**

Extend the positional record with:

```csharp
int? NumericPrecision = null,
int? NumericScale = null
```

Validate Decimal with `precision is >= 1 and <= 38` and `scale is >= 0 && scale <= precision`; reject both properties for all other scalar types.

- [x] **Step 4: Run focused tests and verify GREEN**

Run the Task 1 command and expect zero failures.

### Task 2: Preserve shape through database import and JSON

**Files:**
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/DatabaseColumnMetadataMapperTests.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/CodeGeneration/DatabaseCrudSchemaImporterIntegrationTests.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseColumnMetadata.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseColumnMetadataMapper.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseCrudSchemaImporter.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CrudSchemaDocument.cs`

**Interfaces:**
- Consumes: Task 1 numeric properties.
- Produces: provider metadata and JSON conversion that preserve `18,2`.

- [x] **Step 1: Write failing mapper, JSON and real database assertions**

```csharp
Assert.AreEqual(18, price.NumericPrecision);
Assert.AreEqual(2, price.NumericScale);
```

The CLI JSON test supplies `"numericPrecision": 18` and `"numericScale": 2`; both real provider import tests assert the same values.

- [x] **Step 2: Run unit RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DatabaseColumnMetadataMapperTests|FullyQualifiedName~CodeGenerationCliTests" --no-restore
```

Expected: numeric metadata is not represented.

- [x] **Step 3: Implement metadata and JSON propagation**

Select `NUMERIC_PRECISION AS NumericPrecision` and `NUMERIC_SCALE AS NumericScale` from both provider catalogs, read them as nullable integers, map them only for Decimal, and pass JSON fields into `FullNetColumn`.

- [x] **Step 4: Run focused unit tests and verify GREEN**

Run the Task 2 unit command and expect zero failures.

### Task 3: Render exact DDL and report metadata

**Files:**
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudMigrationTemplateGenerator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudArtifactGenerator.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/reports/products.generation.json` only if the sample gains numeric fields.

**Interfaces:**
- Consumes: validated `NumericPrecision` and `NumericScale`.
- Produces: exact `decimal(18, 2)` in both migration templates and numeric fields in generation reports.

- [x] **Step 1: Change the existing Decimal template assertion to require exact DDL**

```csharp
StringAssert.Contains(sqlServer, "Price decimal(18, 2) NOT NULL");
StringAssert.Contains(mySql, "Price decimal(18, 2) NOT NULL");
Assert.IsFalse(sqlServer.Contains("precision required", StringComparison.Ordinal));
```

- [x] **Step 2: Run generator RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CrudArtifactGeneratorTests" --no-restore
```

Expected: the old review token remains.

- [x] **Step 3: Render validated values and report them**

Use:

```csharp
$"decimal({column.NumericPrecision}, {column.NumericScale})"
```

and serialize both nullable numeric properties beside `maxLength`.

- [x] **Step 4: Run generator GREEN**

Run the Task 3 command and expect zero failures.

### Task 4: Scoped verification and roadmap

**Files:**
- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`

- [x] **Step 1: Run CodeGeneration and complete unit tests**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGeneration" --no-restore
pnpm test:dotnet:unit
```

- [x] **Step 2: Update only the test matrix if discovery increases**

Record the observed unit total only in `eng/testing/test-matrix.json`.

- [x] **Step 3: Run naming, builds and affected dual-provider tests**

```powershell
pnpm test:naming
dotnet build src/BuildingBlocks/Full.NET.Data.CodeGeneration/Full.NET.Data.CodeGeneration.csproj -c Release --no-restore
dotnet build src/Tools/Full.NET.CodeGeneration.Cli/Full.NET.CodeGeneration.Cli.csproj -c Release --no-restore
pnpm test:integration:affected:plan -- --snapshot codegeneration-decimal-shape-20260730 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-decimal-shape-20260730 --phase slice
```

- [x] **Step 4: Run final repository checks**

```powershell
git diff --check
git status --short --branch
```

Expected: all selected verification passes and unrelated dirty files remain untouched.

## Self-Review

- Spec coverage: invariant, strict JSON, both metadata providers, report, paired DDL and real database assertions are mapped.
- Placeholder scan: no deferred implementation text remains; the feature removes the previous Decimal placeholder.
- Type consistency: every layer uses nullable `int` properties named `NumericPrecision` and `NumericScale`.
