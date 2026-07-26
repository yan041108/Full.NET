# Tenant SQL Binding Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task when it is executed in a separate session.

**Goal:** Upgrade tenant SQL from text-only scanning to explicit, controlled metadata so tenant context injection and scope validation are deterministic and architecture-testable without losing the existing missing-parameter defense.

**Architecture:** Extend `SqlStatement` with a tenant-binding declaration. `SqlScopeGuard` validates the binding against `SqlDataScope` and still rejects a tenant Statement that omits `@TenantId`, while `DapperSqlExecutor` injects the trusted current tenant only when the binding explicitly requests it. Production architecture tests enumerate every official module statement and reject inconsistent metadata.

**Tech Stack:** .NET 10, MSTest, Dapper, SQL Server, MySQL.

---

## Task 1: Establish failing scope and architecture tests

**Files:**

- Modify: `tests/Full.NET.UnitTests/Data/SqlScopeGuardTests.cs`
- Create: `tests/Full.NET.ArchitectureTests/SqlDataScopeRulesTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj`

**Steps:**

1. Update the scope guard tests to require `SqlTenantBinding.CurrentTenantId` for `TenantRequired` statements.
2. Verify `Global` and `HostOnly` statements reject tenant bindings.
3. Add an architecture test that reflects all static `SqlStatement` declarations and verifies scope/binding consistency.
4. Include Notifications and Jobs in the SQL-specific assembly catalog without broadening unrelated public-contract scans.
5. Run the focused unit test and confirm compilation fails because `SqlTenantBinding` does not exist yet.

## Task 2: Add tenant-binding metadata to the Dapper boundary

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/SqlTenantBinding.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions/SqlStatement.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/SqlScopeGuard.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperSqlExecutor.cs`

**Steps:**

1. Add `SqlTenantBinding` with `None` and `CurrentTenantId`.
2. Extend `SqlStatement` with a required binding value while retaining the existing three-argument constructor and deconstruction compatibility.
3. Make scope/binding validation authoritative in `SqlScopeGuard` while retaining `@TenantId` presence validation as defense in depth.
4. Drive trusted tenant parameter injection from `TenantBinding`, not `SqlDataScope`.
5. Run the focused unit tests and confirm the boundary behavior passes.

## Task 3: Migrate official tenant-scoped SQL declarations

**Files:**

- Modify: `src/Modules/Full.NET.Modules.Organization/Persistence/OrganizationSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Persistence/PositionSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs`

**Steps:**

1. Add `SqlTenantBinding.CurrentTenantId` to every `TenantRequired` statement.
2. Run the new architecture test and inspect any remaining offenders.
3. Run the full architecture suite and confirm all official production assemblies satisfy the metadata rule.

## Task 4: Verify both providers and the complete integration surface

**Files:**

- Modify only if a test exposes an implementation defect.

**Steps:**

1. Run focused SQL Server and MySQL organization/position tenant tests.
2. Run the full integration suite because the shared Dapper execution boundary changed.
3. Run Release build, Unit, Compatibility, Architecture, Naming, and Skill suites.
4. Run `git diff --check` and inspect `git status`.

## Task 5: Synchronize architecture and verification records

**Files:**

- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Modify: `docs/architecture/fullnet-architecture-roadmap.md`
- Create: `docs/verification/tenant-sql-binding-metadata-2026-07-26.md`

**Steps:**

1. Raise the Architecture threshold from 43 to 44 while keeping Unit 366, Compatibility 7, Integration 172, Naming 23, and Skill 52.
2. Record the semantic tenant-binding gate and the exact commands/results.
3. Update the roadmap so the remaining SQL scope work is limited to the exact `Global` statement catalog.
4. Perform the required rule-evolution and skill-evolution reviews.
5. Request an independent architecture-level code review and resolve any Critical or Important findings before handoff.
