# Identity–Organization Data Scope Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. This task is being executed inline in the current session because the user already authorized continued development.

**Goal:** Remove Identity's direct SQL access to `fn_organization_unit` and `fn_organization_user_unit` without changing data-scope behavior or introducing a network boundary.

**Architecture:** Identity keeps ownership of role-scope resolution, union semantics, and the `fn_identity_role_data_scope_unit` custom-scope predicate. A consumer-owned Identity Contracts port delegates only the Organization-owned `self`, `organization`, and `organization_subtree` SQL fragments to an in-process adapter in the Organization module.

**Tech Stack:** .NET 10, C#, Dapper SQL statements, Microsoft.Extensions.DependencyInjection, MSTest/Microsoft.Testing.Platform, SQL Server, MySQL.

## Global Constraints

- Keep Full.NET 1.0 as a strengthened modular monolith; add no service, transport, migration, table, or project.
- Preserve existing public HTTP, JSON, permission, tenant, and data-scope semantics.
- Identity may reference only its own table names; Organization owns both Organization table predicates.
- SQL remains parameterized and must preserve `@TenantId`, `@DataScopeUserId`, and custom-role parameter semantics.
- Handwritten comments and XML documentation are in clear Chinese.
- Preserve unrelated workspace changes and leave this slice uncommitted unless the user explicitly requests a commit.

---

### Task 1: Establish the ownership-boundary RED

**Files:**
- Modify: `contracts/architecture/module-table-access-debt.json`
- Test: `tests/Full.NET.ArchitectureTests/ModuleTableOwnershipTests.cs`

**Interfaces:**
- Consumes: the existing exact debt registry and production-source scanner.
- Produces: a failing gate that identifies the two current Identity-to-Organization table accesses.

- [x] **Step 1: Remove the two exact debt entries**

Remove only the entries for:

```text
identity -> fn_organization_unit @ src/Modules/Full.NET.Modules.Identity/DataScope/RoleDataScopeProjection.cs
identity -> fn_organization_user_unit @ src/Modules/Full.NET.Modules.Identity/DataScope/RoleDataScopeProjection.cs
```

- [x] **Step 2: Run the ownership test and verify RED**

Run:

```powershell
dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --filter "FullyQualifiedName~Production_module_table_access_is_owned_or_exactly_registered" --minimum-expected-tests 1
```

Expected: one failed test listing both unregistered accesses from `RoleDataScopeProjection.cs`.

### Task 2: Add the consumer-owned port and Organization adapter

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/UserDataScopeContracts.cs`
- Create: `src/Modules/Full.NET.Modules.Organization/DataScope/IdentityOrganizationDataScopeSqlProjection.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/OrganizationModule.cs`

**Interfaces:**
- Consumes: `RoleDataScopeKinds` and `DataScopeSqlFilter`.
- Produces: `IIdentityOrganizationDataScopeSqlProjection.BuildOrganizationUnitFilter(string dataScopeKind, string unitIdColumn, Guid currentUserId)`.

- [x] **Step 1: Define the minimal Identity-side port**

Add a public interface in Identity Contracts:

```csharp
public interface IIdentityOrganizationDataScopeSqlProjection
{
    DataScopeSqlFilter BuildOrganizationUnitFilter(
        string dataScopeKind,
        string unitIdColumn,
        Guid currentUserId);
}
```

Its contract accepts only the three Organization-backed restricted kinds and rejects unsupported kinds.

- [x] **Step 2: Implement Organization-owned SQL projection**

Create an internal stateless adapter that:

- uses `fn_organization_user_unit` for `self` and primary-unit selection;
- uses `fn_organization_unit` only for the recursive subtree;
- keeps tenant and active-state predicates;
- returns the current user as `DataScopeUserId`;
- throws `ArgumentException` for unsupported kinds.

- [x] **Step 3: Register the adapter**

Register the adapter as:

```csharp
services.TryAddSingleton<
    IIdentityOrganizationDataScopeSqlProjection,
    IdentityOrganizationDataScopeSqlProjection>();
```

### Task 3: Make Identity compose through the port

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/DataScope/RoleDataScopeProjection.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/DataScope/DataScopeSqlFilterBuilder.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `tests/Full.NET.UnitTests/Identity/RoleDataScopeProjectionTests.cs`
- Verify: `tests/Full.NET.UnitTests/Modularity/FullNetModuleCatalogTests.cs`

**Interfaces:**
- Consumes: `IIdentityOrganizationDataScopeSqlProjection`.
- Produces: unchanged `IDataScopeSqlFilterBuilder` behavior and a resolvable API module graph.

- [x] **Step 1: Adapt projection tests to the desired dependency**

Use a recording fake port. Verify that `self` delegates the kind, target column, and current user, while `custom`, unrestricted, empty-role, and union behavior remain owned by Identity.

- [x] **Step 2: Convert the projection to an injected instance**

For `self`, `organization`, and `organization_subtree`, call the port and wrap its result in the internal fragment used by union composition. Keep `all`, `custom`, unsupported-kind rejection, unique custom role parameter names, and deny-all behavior unchanged.

- [x] **Step 3: Wire the builder**

Inject `RoleDataScopeProjection` into `DataScopeSqlFilterBuilder`, register both as singletons in `IdentityModule`, and assert that the API profile contains the port registration.

- [x] **Step 4: Run focused Unit and Architecture tests**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~RoleDataScopeProjectionTests|FullyQualifiedName~IdentityOrganizationDataScopeSqlProjectionTests|FullyQualifiedName~FullNetModuleCatalogTests" --minimum-expected-tests 12
dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 43
```

Expected: focused Unit tests and all 43 Architecture tests pass; the debt registry contains five entries.

### Task 4: Verify both database providers and document evidence

**Files:**
- Create: `docs/verification/identity-organization-data-scope-port-2026-07-26.md`
- Modify only if test discovery changes: `README.md`, `docs/development/getting-started.md`, `.github/workflows/ci.yml`, `.agents/skills/fullnet-module-delivery/references/delivery-map.md`, `docs/verification/test-threshold-audit-2026-07-19.md`

**Interfaces:**
- Consumes: the complete in-process module graph and real SQL Server/MySQL test databases.
- Produces: fresh dual-provider evidence and repository checks.

- [x] **Step 1: Run focused dual-provider integration tests**

Run the SQL Server and MySQL methods matching:

```text
Tenant_unit_data_scope_filtering_follows_contract
```

Expected: 2/2 total, one pass per provider.

- [x] **Step 2: Run canonical verification**

Run:

```powershell
pnpm test:naming
dotnet build Full.NET.slnx -c Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 364
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 43
git diff --check
git status --short --branch
```

Expected: all commands pass with canonical counts `364/7/43/172`, and unrelated workspace files remain untouched.

- [x] **Step 3: Record verification and governance reviews**

Document exact commands and results, then perform `rules/rule-evolution.md` followed by `rules/skill-evolution.md`. Update governance artifacts only if their evidence thresholds are met.
