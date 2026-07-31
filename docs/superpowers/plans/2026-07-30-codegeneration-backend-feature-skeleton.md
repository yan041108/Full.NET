# CodeGeneration Backend Feature Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让租户型 CRUD Schema 额外生成可在现有 Full.NET 模块项目中直接编译和显式接入的 Endpoint、应用服务、持久化记录与注册骨架。

**Architecture:** 运行时骨架复用现有生成契约和 SQL，通过 Full.NET 自有查询、命令、事务、租户、时钟和 ID 抽象访问数据。分页计数与列表合并为一次多结果集数据库往返；生成注册扩展只提供显式接入点，不自动修改模块入口、Composition、菜单或客户端路由。当前 Schema 无法区分 HostOnly 与 Global，因此非租户 Schema 不生成运行时骨架。

**Tech Stack:** .NET 10、C#、ASP.NET Core Minimal API、System.Text.Json 源生成、Full.NET Data Abstractions、Dapper 执行边界、MSTest。

## Global Constraints

- 只为 `IsTenantScoped=true` 的 Schema 生成运行时功能骨架。
- SQL 必须声明 `SqlDataScope.TenantRequired` 与 `SqlTenantBinding.CurrentTenantId`。
- 业务代码不得直接引用 Dapper、`DbConnection` 或通用 Repository。
- 分页计数与数据列表必须通过 `IMultiResultQueryExecutor` 在一次数据库往返中读取。
- 写操作必须进入 `ICommandTransaction`，主键使用 `IIdGenerator`，版本冲突使用稳定机器码。
- Endpoint 必须声明精确读写权限，使用标准状态码和 `IApiResultMapper`。
- 生成器不得自动修改模块注册、Composition、菜单、路由或国际化资源。
- 不新增规则或 Skill；测试数量只更新 `eng/testing/test-matrix.json`。

---

### Task 1: Specify the generated backend feature contract

**Files:**
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`
- Modify: `docs/superpowers/plans/2026-07-30-codegeneration-backend-feature-skeleton.md`

**Interfaces:**
- Consumes: `CrudArtifactGenerator.Generate(FullNetCrudSchema)`.
- Produces: `backend/{Entity}Record.g.cs`, `backend/{Entity}Feature.g.cs`, and `backend/{Entity}Endpoint.g.cs` for tenant-scoped schemas.

- [ ] **Step 1: Write failing artifact tests**

Assert the three new paths exist for Catalog/Product and that:

```csharp
StringAssert.Contains(sql, "SqlDataScope.TenantRequired");
StringAssert.Contains(sql, "SqlTenantBinding.CurrentTenantId");
StringAssert.Contains(feature, "IMultiResultQueryExecutor");
StringAssert.Contains(feature, "ICommandTransaction");
StringAssert.Contains(endpoint, "RequireAuthorization");
StringAssert.Contains(endpoint, "AddGeneratedProductFeature");
```

Also assert a valid non-tenant Schema does not emit the three runtime artifacts.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CrudArtifactGeneratorTests"
```

Expected: FAIL because the backend runtime artifacts and SQL statements do not exist.

### Task 2: Generate executable persistence and application code

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudBackendFeatureGenerator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudArtifactGenerator.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`

**Interfaces:**
- Consumes: validated `FullNetCrudSchema`, generated contracts, and existing SQL text.
- Produces: `GenerateRecord`, `GenerateFeature`, `GenerateEndpoint`, plus tenant-scoped `SqlStatement` declarations in `{Entity}Sql`.

- [ ] **Step 1: Generate the persistence record**

Emit an internal positional `{Entity}Record` using the exact C# property names, types and column order from the Schema.

- [ ] **Step 2: Generate tenant-scoped SQL statements**

Add `FindById`, one-round-trip SQL Server/MySQL page statements, and executable insert/update/disable statements. Statement IDs use lowercase snake-case segments and provider suffixes `.sql_server` and `.my_sql`.

- [ ] **Step 3: Generate query and management services**

The query service clamps page values, selects the provider-specific page statement, consumes count then rows from `IMultiResultQueryExecutor`, and maps records to responses. The management service executes create/update/disable inside `ICommandTransaction`, validates generated string length constraints, injects tenant context and ID/time/version values, and resolves not-found versus version-conflict failures.

- [ ] **Step 4: Generate Endpoint and explicit registration**

Emit list/detail/create/update/disable Minimal API mappings, precise permission policies, standard `201/200` responses, JSON source-generation metadata, and `AddGenerated{Entity}Feature`/`MapGenerated{Entity}Feature` extensions.

- [ ] **Step 5: Verify GREEN except fixture drift**

Run the Task 1 command. All behavior assertions must pass; only missing or changed fixture assertions may remain red.

### Task 3: Freeze and compile the generated feature

**Files:**
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/backend/ProductRecord.g.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/backend/ProductFeature.g.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/backend/ProductEndpoint.g.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/backend/ProductContracts.g.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/backend/ProductSql.g.cs`

**Interfaces:**
- Consumes: deterministic Catalog/Product generator output.
- Produces: byte-stable fixtures that are compiled automatically by `Full.NET.UnitTests.csproj`.

- [ ] **Step 1: Update exact generated fixtures**

Persist all backend outputs with LF endings and no BOM.

- [ ] **Step 2: Run focused generator tests**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CrudArtifactGeneratorTests"
```

Expected: all focused tests pass and the generated C# fixture compiles as part of the test assembly.

### Task 4: Verify the affected slice

**Files:**
- Inspect: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/**`
- Inspect: `tests/Full.NET.UnitTests/CodeGeneration/**`
- Modify only if the discovered count increases: `eng/testing/test-matrix.json`

**Interfaces:**
- Consumes: snapshot `codegeneration-backend-feature-skeleton-20260730`.
- Produces: fresh unit, affected dual-provider, naming, build and workspace evidence.

- [ ] **Step 1: Run CodeGeneration and full unit tests**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGeneration"
pnpm test:dotnet:unit
```

- [ ] **Step 2: Run naming and affected slice**

```powershell
pnpm test:naming
pnpm test:integration:affected:plan -- --snapshot codegeneration-backend-feature-skeleton-20260730 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-backend-feature-skeleton-20260730 --phase slice
```

- [ ] **Step 3: Run final static checks**

```powershell
dotnet build src/BuildingBlocks/Full.NET.Data.CodeGeneration/Full.NET.Data.CodeGeneration.csproj -c Release --no-restore
git diff --check
git status --short
```

Expected: focused and unit tests pass, affected CodeGeneration tests cover SQL Server and MySQL, naming passes, build has no warnings or errors, and no unrelated worktree changes are staged or overwritten.
