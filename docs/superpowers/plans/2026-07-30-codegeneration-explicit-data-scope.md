# CodeGeneration Explicit Data Scope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 CRUD Schema 显式表达 `TenantRequired`、`HostOnly`、`Global` 或兼容期 `Unspecified`，并为三种已确认作用域生成安全的后端运行时骨架。

**Architecture:** 在 CodeGeneration Schema 内新增不依赖运行时数据项目的 `FullNetCrudDataScope`，旧 `isTenantScoped`/`--tenant-scoped` 输入继续映射到 `TenantRequired` 或 `Unspecified`，避免把历史 `false` 猜成 Host/Global。显式 Host/Global 输入生成对应 `SqlDataScope`、无租户绑定的 SQL Statement，并按作用域生成写入前上下文保护；`Unspecified` 继续只生成契约、SQL 文本和客户端。

**Tech Stack:** .NET 10、C#、System.Text.Json、ASP.NET Core Minimal API、Full.NET Data Abstractions、MSTest

## Global Constraints

- 旧 `CreateProject(... bool isTenantScoped, ...)`、JSON `isTenantScoped` 和 `--tenant-scoped` 必须保持兼容。
- 历史 `isTenantScoped=false` 必须映射到 `Unspecified`，不得自动解释为 `HostOnly` 或 `Global`。
- `TenantRequired` 必须要求规范 `TenantId`；显式 `HostOnly`/`Global` 必须拒绝 `TenantId`。
- 只有 `Unspecified` 不生成运行时 Endpoint、Feature、Record 和可执行 `SqlStatement`。
- Tenant Statement 使用 `SqlTenantBinding.CurrentTenantId`；Host/Global Statement 使用 `SqlTenantBinding.None`。
- Host 写入必须在命令执行前验证 Host 上下文；Tenant 写入继续验证 Tenant 上下文；Global 不注入 `ICurrentTenant`。
- 不自动修改模块入口、Composition、菜单、客户端路由、迁移或项目引用。
- 不新增规则或 Skill；测试数量只更新 `eng/testing/test-matrix.json`。

---

### Task 1: Add the compatible Schema data-scope contract

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudDataScope.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudSchema.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/FullNetCrudSchemaTests.cs`

**Interfaces:**
- Produces: `FullNetCrudDataScope.Unspecified|TenantRequired|HostOnly|Global`.
- Preserves: existing `CreateProject(... bool isTenantScoped, ...)`.
- Adds: `CreateProject(... FullNetCrudDataScope dataScope, ...)` and `FullNetCrudSchema.DataScope`.

- [ ] Write tests proving legacy `true/false` map to `TenantRequired/Unspecified`, explicit Host/Global are preserved, and Host/Global reject `TenantId`.
- [ ] Run the focused Schema tests and confirm RED because the enum, overload and property do not exist.
- [ ] Add the enum, overload and column invariants while keeping `IsTenantScoped` as a derived compatibility property.
- [ ] Run the focused Schema tests and confirm GREEN.

### Task 2: Carry explicit scope through JSON and database import CLI

**Files:**
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CrudSchemaDocument.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/DatabaseImportCliOptions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseCrudImportOptions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseCrudSchemaAssembler.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/DatabaseCrudSchemaAssemblerTests.cs`
- Modify: `samples/codegeneration/catalog-product.schema.json`

**Interfaces:**
- JSON accepts either legacy `isTenantScoped` or new `dataScope`; conflicting or missing scope input fails closed.
- `import-database` accepts either legacy `--tenant-scoped <true|false>` or `--data-scope <tenant|host|global>`; passing both fails with usage exit code `64`.

- [ ] Add failing JSON, parser and assembler tests for explicit Host/Global, legacy compatibility, missing scope and conflicting scope.
- [ ] Run CodeGeneration CLI/assembler tests and confirm RED for the missing data-scope path.
- [ ] Implement strict scope resolution and update the sample to the explicit `dataScope` form.
- [ ] Run the focused tests and confirm GREEN.

### Task 3: Generate runtime backend code for every explicit scope

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudArtifactGenerator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudBackendFeatureGenerator.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/reports/products.generation.json`

**Interfaces:**
- Tenant emits `SqlDataScope.TenantRequired` plus `CurrentTenantId`.
- Host emits `SqlDataScope.HostOnly`, `None`, and `EnsureHostContext`.
- Global emits `SqlDataScope.Global`, `None`, and no current-tenant dependency.
- Unspecified preserves the current no-runtime behavior.

- [ ] Add failing artifact tests for HostOnly, Global and Unspecified output.
- [ ] Run `CrudArtifactGeneratorTests` and confirm RED for missing Host/Global runtime artifacts.
- [ ] Generalize statement generation, context guards and artifact selection without changing tenant fixture output except the report’s explicit scope field.
- [ ] Run the focused tests and confirm GREEN, including compiled tenant fixtures.

### Task 4: Verify the affected slice

**Files:**
- Inspect: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/**`
- Inspect: `src/Tools/Full.NET.CodeGeneration.Cli/**`
- Inspect: `tests/Full.NET.UnitTests/CodeGeneration/**`
- Modify if the discovered count increases: `eng/testing/test-matrix.json`

**Interfaces:**
- Consumes snapshot: `codegeneration-explicit-data-scope-20260730`.
- Produces fresh CodeGeneration, unit, naming and SQL Server/MySQL affected-test evidence.

- [ ] Run `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGeneration" --no-restore`.
- [ ] Run `pnpm test:dotnet:unit` and update only the centralized matrix minimum when required.
- [ ] Run `pnpm test:naming`.
- [ ] Run `pnpm test:integration:affected:plan -- --snapshot codegeneration-explicit-data-scope-20260730 --phase inner`.
- [ ] Run `pnpm test:integration:affected -- --snapshot codegeneration-explicit-data-scope-20260730 --phase slice`.
- [ ] Run the CodeGeneration Release build, `git diff --check`, branch and scoped status checks.
