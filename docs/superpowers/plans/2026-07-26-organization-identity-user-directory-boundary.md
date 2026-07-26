# Organization–Identity User Directory Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This plan is executed inline because the user explicitly requested continued autonomous development.

**Goal:** Remove Organization's direct SQL access to `fn_identity_user` while preserving user-unit and user-position response fields and avoiding per-row Identity queries.

**Architecture:** Organization continues to page and filter only its own assignment, unit, and position tables. Identity exposes a dedicated `IHostUserDisplayDirectory` batch contract alongside the unchanged active-user validation directory, and Organization enriches each page through that in-process Port; missing users preserve the former inner-join behavior by being omitted or treated as not found.

**Tech Stack:** .NET 10, C#, Dapper explicit SQL, Microsoft.Extensions.DependencyInjection, MSTest/Microsoft.Testing.Platform, SQL Server, MySQL.

## Global Constraints

- Keep the strengthened modular monolith and existing project topology; add no service, transport, migration, table, cache, or project.
- Preserve HTTP/JSON contracts, permissions, tenant filtering, ordering, page-size limits, active-user validation on writes, and disabled-user display behavior on reads.
- Batch each page's user projection in one Identity query; do not introduce N+1 lookups.
- Identity owns all `fn_identity_user` SQL; Organization owns all assignment, unit, and position SQL.
- Keep SQL parameterized and host-user predicates explicit; handwritten comments remain clear Chinese.
- Preserve the user's unrelated `.cache/` and `.tmp/art-design-pro/` files and leave this tranche uncommitted unless explicitly requested.

---

### Task 1: Establish ownership and contract RED

**Files:**
- Modify: `contracts/architecture/module-table-access-debt.json`
- Create: `tests/Full.NET.UnitTests/Identity/HostUserDirectoryTests.cs`
- Test: `tests/Full.NET.ArchitectureTests/ModuleTableOwnershipTests.cs`

**Interfaces:**
- Consumes: exact cross-module table ownership scanner and existing `IHostUserDirectory`.
- Produces: failing evidence for the removed Organization debt and the absent batch lookup.

- [x] **Step 1: Remove the exact Organization debt entry**

Remove only:

```text
organization -> fn_identity_user @ src/Modules/Full.NET.Modules.Organization/Persistence/OrganizationSql.cs
```

- [x] **Step 2: Run the ownership gate and verify RED**

Run:

```powershell
dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --filter "FullyQualifiedName~Production_module_table_access_is_owned_or_exactly_registered" --minimum-expected-tests 1
```

Expected: one failed test naming `OrganizationSql.cs` and `fn_identity_user`.

- [x] **Step 3: Add the wished-for batch-directory test**

Create a Unit test that calls:

```csharp
Task<IReadOnlyDictionary<Guid, HostUserDirectoryEntry>> FindHostUsersAsync(
    IReadOnlyCollection<Guid> userIds,
    CancellationToken cancellationToken = default);
```

The test supplies duplicate IDs, returns two directory rows from `IQueryExecutor`, and asserts one query plus a dictionary keyed by the two distinct IDs.

- [x] **Step 4: Run the focused Unit build and verify RED**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release
```

Expected: compilation fails because the batch display directory and projection row do not exist.

### Task 2: Implement the Identity-owned batch directory

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/IHostUserDirectory.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Persistence/HostUserDirectoryRecord.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/HostUsers/HostUserDirectory.cs`
- Test: `tests/Full.NET.UnitTests/Identity/HostUserDirectoryTests.cs`

**Interfaces:**
- Consumes: `IQueryExecutor.QueryAsync<T>` and Dapper list expansion through `UserIds`.
- Produces: `IHostUserDisplayDirectory.FindHostUsersAsync(...)` returning existing Host users regardless of active state.

- [x] **Step 1: Extend the public directory contract**

Add the batch signature above on a dedicated `IHostUserDisplayDirectory` and document that it returns existing Host users, including disabled users, so read projections preserve historical display semantics. The existing `IHostUserDirectory` remains unchanged and active-only for write validation.

- [x] **Step 2: Add the Identity SQL and minimal row**

Add statement `identity.list_host_users_by_ids`:

```sql
SELECT Id, Username, DisplayName
FROM fn_identity_user
WHERE Id IN @UserIds
  AND ScopeKey = 'host'
  AND TenantId IS NULL
```

Use `SqlDataScope.Global`, because callers may hold tenant context while the SQL itself explicitly restricts Host rows.

- [x] **Step 3: Implement one-query batch lookup**

Validate the collection, return an empty dictionary without querying when it is empty, deduplicate IDs before execution, and convert returned rows to `HostUserDirectoryEntry` keyed by ID.

- [x] **Step 4: Run the focused test and verify GREEN**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~HostUserDirectoryTests" --minimum-expected-tests 1
```

Expected: the batch-directory test passes.

### Task 3: Make Organization query only owned tables

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Organization/Persistence/OrganizationSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Persistence/OrganizationUserUnitRecord.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Persistence/OrganizationUserPositionRecord.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserUnits/TenantUserUnitQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserPositions/TenantUserPositionQueryService.cs`
- Modify: `tests/Full.NET.IntegrationTests/Organization/OrganizationUserUnitManagementAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Organization/OrganizationUserPositionManagementAssertions.cs`

**Interfaces:**
- Consumes: `IHostUserDisplayDirectory.FindHostUsersAsync(...)`.
- Produces: unchanged `OrganizationUserUnitResponse` and `OrganizationUserPositionResponse`.

- [x] **Step 1: Strengthen existing API assertions**

Assert that create/update responses still contain `Username == "admin"` and the seeded administrator display name. These assertions pass on the old join and protect behavior during the refactor.

- [x] **Step 2: Remove Identity joins from Organization SQL**

For both providers and both assignment types, select only assignment and owned unit/position columns. Apply the same change to the two find-by-ID statements. Do not change count, filter, order, page, or tenant clauses.

- [x] **Step 3: Enrich rows through one batch call**

Inject `IHostUserDisplayDirectory` into both query services. For list operations, request distinct page user IDs once, omit rows whose Identity entry is absent, and map names from the dictionary. For get-by-ID operations, use the same batch method with one ID and return the existing not-found error when no Identity row exists.

- [x] **Step 4: Run focused Unit and Architecture gates**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~HostUserDirectoryTests|FullyQualifiedName~FullNetModuleCatalogTests" --minimum-expected-tests 5
dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 43
```

Expected: all focused tests and 43 Architecture tests pass; the debt registry contains four entries and Organization contains no `fn_identity_user`.

### Task 4: Verify both providers and record evidence

**Files:**
- Create: `docs/verification/organization-identity-user-directory-boundary-2026-07-26.md`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`

**Interfaces:**
- Consumes: the complete API module graph and real SQL Server/MySQL databases.
- Produces: fresh dual-provider evidence and canonical count `365/7/43/172`.

- [x] **Step 1: Run the four affected dual-provider scenarios**

Run SQL Server and MySQL methods for:

```text
Tenant_user_unit_management_follows_contract
Tenant_user_position_management_follows_contract
```

Expected: **4/4**, with names supplied through the Identity directory.

- [x] **Step 2: Run canonical verification**

Run:

```powershell
pnpm test:naming
pnpm test:skills
dotnet build Full.NET.slnx -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 365
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 43
pnpm test:integration:full
git diff --check
git status --short --branch
```

Expected: all commands pass; full Integration remains **172/172** and unrelated workspace files remain untouched.

- [x] **Step 3: Record governance reviews**

Write exact RED/GREEN, dual-provider, full-suite, debt-count, rule-review, and Skill-review evidence. Update governance artifacts only when their evidence thresholds are met.
