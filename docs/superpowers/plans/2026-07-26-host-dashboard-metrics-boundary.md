# Host Dashboard Metrics Boundary Implementation Plan

> **Execution:** This plan is executed inline because the user requested continued autonomous development.

**Goal:** Eliminate the final three registered cross-module table reads by moving tenant and auditing dashboard metrics behind owner-implemented, consumer-defined in-process Ports.

**Architecture:** Identity keeps the Host dashboard HTTP contract, authorization and its own online-session metric. Identity.Contracts defines optional tenant and audit metric readers; Tenancy and Auditing implement them with owned SQL. Identity aggregates the registered readers and returns zero/empty contributions in reduced module profiles. This preserves the strengthened modular monolith without a Reporting project, network boundary or dependency cycle.

**Tech Stack:** .NET 10, C#, Dapper explicit SQL, Microsoft.Extensions.DependencyInjection enumerable registrations, MSTest/Microsoft.Testing.Platform, SQL Server, MySQL.

## Global Constraints

- Preserve the Host dashboard HTTP/JSON contract, permission, UTC-day boundary, five-item activity limit and live metric semantics.
- Identity must not reference Tenancy or Auditing production projects; both producers already depend on Identity.Contracts.
- Each producer owns all SQL for its tables. Do not introduce a generic repository, dynamic table name, cache, migration, transport or project.
- Reduced module profiles may omit a producer and must return zero/empty metrics rather than fail service resolution.
- Preserve unrelated workspace files and leave all work uncommitted unless explicitly requested.

### Task 1: Establish the final ownership RED

**Files:**
- Modify: `contracts/architecture/module-table-access-debt.json`
- Test: `tests/Full.NET.ArchitectureTests/ModuleTableOwnershipTests.cs`

- [x] Remove the three remaining exact debt entries for `HostDashboardSql.cs`.
- [x] Run the focused ownership gate and capture failures for Tenancy tenant, Auditing access-log and Auditing operation-log tables.

### Task 2: Define and implement owner metric readers

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/HostDashboardContracts.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/HostDashboard/HostDashboardTenantMetricsReader.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`
- Reuse: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/HostDashboard/HostDashboardAuditMetricsReader.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/Persistence/HostDashboardAuditSql.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/Persistence/HostDashboardAuditRecords.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/AuditingModule.cs`

- [x] Add separate tenant and audit reader contracts plus the minimal audit metric projection.
- [x] Implement active-tenant count with the existing Tenancy-owned statement.
- [x] Implement today request/error metrics and provider-specific recent activity paging in Auditing.
- [x] Register each reader as an enumerable scoped contribution.

### Task 3: Make Identity aggregate only Ports and owned SQL

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/GetHostDashboardSummary/HostDashboardQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/HostDashboardSql.cs`
- Delete: `src/Modules/Full.NET.Modules.Identity/Persistence/HostDashboardActivityRecord.cs`
- Create: `tests/Full.NET.UnitTests/Identity/HostDashboardQueryServiceTests.cs`

- [x] Keep only the active Host session statement in Identity SQL.
- [x] Aggregate at most one registered tenant reader and audit reader; use zero/empty values when a producer is absent.
- [x] Preserve response fields and activity order/limit.
- [x] Cover registered-owner aggregation and missing-reader fallback in one focused Unit regression.
- [x] Run Release build and Architecture **43/43**; verify the exact debt registry is empty.

### Task 4: Verify both providers and governance

**Files:**
- Create: `docs/verification/host-dashboard-metrics-boundary-2026-07-26.md`

- [x] Run the dashboard API scenario against SQL Server and MySQL.
- [x] Run canonical Unit **366**, Compatibility **7**, Architecture **43**, Naming **23** and Skill **52** gates.
- [x] Perform architecture review and resolve its actionable findings.
- [x] Perform rule/Skill evolution reviews, `git diff --check`, status and branch checks.
- [x] Keep Integration threshold **172**; run full Integration only if focused or composition verification exposes broader risk.
