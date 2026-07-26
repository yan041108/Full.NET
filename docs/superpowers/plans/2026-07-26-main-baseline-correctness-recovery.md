# Main Baseline Correctness Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the 2026-07-26 `main` branch to a buildable, testable Full.NET baseline after the API Key, Jobs, Files, Notifications, Auditing, Realtime and Settings delivery wave.

**Architecture:** Keep the reinforced modular monolith and existing module topology. Fix defects at their owning module boundary, keep authentication bootstrap queries explicitly global but row-filtered, keep irreversible side effects outside database transactions, and make security decisions depend on trusted server-side permission snapshots. Governance and documentation must describe the verified implementation rather than the intended implementation.

**Tech Stack:** .NET 10, ASP.NET Core authentication/authorization, Dapper, SQL Server 2022, MySQL 8, SignalR, TypeScript, pnpm 10.26, Vitest, Playwright, Microsoft.Testing.Platform.

## Global Constraints

- Do not create a branch or worktree; the repository owner explicitly authorized work on `main`.
- Preserve the existing untracked `.cache/` and `.tmp/` directories.
- Use Dapper through Full.NET executors and preserve SQL Server/MySQL semantic parity.
- Establish a failing regression test before each production behavior change.
- Keep external messaging and physical blob deletion outside database transactions.
- Do not grant an API Key permissions beyond both the operator and target user's effective permissions.
- Keep handwritten source comments in clear Chinese.
- Update the four canonical test-count locations whenever discovered test counts change.

---

### Task 1: API Key Authentication and Delegation Ceiling

**Files:**
- Modify: `tests/Full.NET.UnitTests/Data/HostCatalogSqlScopeTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Identity/IdentityApiKeyAssertions.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/ApiKeySql.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostApiKeys/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostApiKeys/HostApiKeyManagementService.cs`

**Interfaces:**
- Consumes: `IPermissionSnapshotReader.GetAsync(Guid, CancellationToken)` and authenticated operator identity.
- Produces: API Key authentication queries that can run before principal creation and API Key permissions bounded by operator and target user snapshots.

- [x] Add an architecture assertion that authentication lookup and last-used update use `SqlDataScope.Global` with explicit Host user predicates.
- [x] Extend the dual-database API Key integration test with a non-super-administrator operator that cannot mint a Key containing permissions absent from either effective snapshot.
- [x] Run the focused Unit/Integration tests and confirm failure for the current Host-only SQL and unrestricted permission normalization.
- [x] Change only the bootstrap authentication statements to `Global`, retain explicit `ScopeKey='host'` and `TenantId IS NULL` filters, and pass the trusted operator identity into the management service.
- [x] Intersect requested permissions with operator and target snapshots; return a stable validation/forbidden error when the request exceeds either ceiling.
- [x] Re-run the focused Unit and SQL Server/MySQL integration tests.

### Task 2: Recoverable Job Leases

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsHostDefinitionAssertions.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs`

**Interfaces:**
- Consumes: `JobExecutionStatuses.Pending`, `JobExecutionStatuses.Running`, lease expiry and current UTC time.
- Produces: SQL Server/MySQL acquire statements that reclaim expired running executions while excluding unexpired leases.

- [x] Add a dual-database regression scenario that leaves an execution in `Running` with an expired lease and invokes the worker runner.
- [x] Run both provider tests and confirm the execution remains stuck.
- [x] Update both acquire statements to select pending work or running work whose lease expired, while retaining provider-specific locking.
- [x] Re-run both provider tests and assert one successful reclaim without duplicate completion.

### Task 3: Commit-Then-Side-Effect Boundaries

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/SendHostInboxMessages/HostInboxMessageService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/HostAnnouncementManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageMyInboxMessages/MyInboxManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Features/ManageHostFiles/HostFileManagementService.cs`
- Verify: existing Notifications/Files dual-provider Integration scenarios

**Interfaces:**
- Consumes: committed `Result<T>` and `IRealtimePublisher`/`IHostFileBlobStorage`.
- Produces: realtime notifications and physical deletion only after `ICommandTransaction.ExecuteAsync` has returned.

- [x] Trace every external side effect reachable from the affected transaction callbacks and preserve the red audit evidence.
- [x] Return the committed notification result from the transaction callback, then publish outside the callback without reversing a successful commit on publisher failure.
- [x] Return the committed file result and storage key from the transaction callback, then perform best-effort blob deletion after commit.
- [x] Re-run the Notifications and Files SQL Server/MySQL integration scenarios (**4/4**).

### Task 4: Auditing and Files Production Hardening

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Auditing/AuditingExceptionLogAssertions.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Middleware/ExceptionLogMiddleware.cs`
- Create: `tests/Full.NET.UnitTests/Files/LocalFileStorageOptionsValidatorTests.cs`
- Create: `src/Modules/Full.NET.Modules.Files/Storage/LocalFileStorageOptionsValidator.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/FilesModule.cs`
- Create: `docs/operations/files-local-storage.md`

**Interfaces:**
- Produces: bounded non-sensitive exception summaries and startup validation for `Files:Local:RootPath`/`MaxUploadBytes`.

- [x] Extend the dual-provider exception-log integration scenario to require a stable safe message and no raw stack trace, and preserve its red result.
- [x] Store a stable safe message and no raw stack trace; keep detailed diagnostics in protected structured application logging.
- [x] Add a focused validator test and register `ValidateOnStart` for blank roots, non-positive upload limits and invalid paths.
- [x] Document environment variables, filesystem ownership, backup and orphan-cleanup requirements.
- [x] Re-run focused Auditing SQL Server/MySQL integration and Release build.

### Task 5: Restore Automated Quality Gates

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Abstractions/Results/CommonErrorCodes.cs`
- Modify: all exact resources/tests/contracts referencing `hosting.rate_limited`
- Modify: `packages/client-contracts/src/settings-enum-catalogs.ts`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `package.json`
- Modify: `pnpm-lock.yaml`
- Modify: `tests/e2e/admin-parity/tests/shell-parity.spec.mjs`

**Interfaces:**
- Produces: three-segment rate-limit code, strict TypeScript runtime guard, passing Skill contract, reviewed dependency graph and deterministic E2E fixtures.

- [x] Run the current Architecture, client build, Skill, audit and focused E2E commands and preserve their failing evidence.
- [x] Normalize the unreleased rate-limit code consistently to `hosting.rate_limit.exceeded`.
- [x] Require `typeof value.memberCount === 'number'` before integer/range checks.
- [x] Add the missing runtime-assertion guidance to the module delivery map.
- [x] Resolve the nested PostCSS advisories with a precise override and update the affected dependency chain; evaluate brace-expansion without weakening audit policy.
- [x] Add current Notifications/Jobs/Auditing/API-Key permissions to the authenticated E2E fixture and make dialog/field selectors uniquely scoped.
- [x] Re-run every formerly red gate.

### Task 6: Governance and Documentation Synchronization

**Files:**
- Modify: `rules/development-quality.md`
- Modify: `rules/rule-evolution.md`
- Modify: `rules/skill-evolution.md`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Modify: affected 2026-07-26 verification records

**Interfaces:**
- Produces: enforceable pre-authentication SQL-scope rule, current Realtime state, honest capability evidence and synchronized test thresholds.

- [x] Upgrade the repeated Host-catalog SQL-scope candidate into a mandatory rule with the new architecture assertion as verification.
- [x] Mark the Realtime Skill candidate as implemented/consumed evidence rather than waiting for first use.
- [x] Replace stale RBAC/Realtime statements and obsolete `128`/`349/7/38/156` current thresholds.
- [x] Discover fresh test counts and update README, CI, getting-started and the delivery map to the same minimum.
- [x] Record this recovery task in verification documentation without rewriting historical evidence as if it never occurred.

### Task 7: Full Verification and Commit

**Files:**
- Verify all modified files.

**Interfaces:**
- Produces: a committed `main` baseline with reproducible evidence.

- [x] Diagnose the full Integration failures caused by `Through008/009/010` runners admitting migrations 012+; replace exclusion lists with explicit upper bounds and upgrade the repeated rule candidate.
- [x] Run Release solution build.
- [x] Run Unit, Compatibility, Architecture and the full SQL Server/MySQL Integration test assemblies with synchronized minimum counts.
- [x] Run naming, SQL safety, OpenAPI, governance, workspace, Skill, client tests, client builds, dependency audit and mock E2E.
- [x] Run link/placeholder review, `git diff --check`, `git status` and rules/Skills retrospectives.
- [x] Review the complete diff for unrelated changes and commit all task changes together after every required gate is green.

### Task 8: Full-suite Regression Recovery

**Files:**
- Modify: Host Identity endpoint OpenAPI metadata and related dual-provider assertions.
- Modify: role data-scope request/service/client contracts.
- Modify: Auditing insert SQL scopes and focused Unit coverage.
- Modify: Host dashboard table reference and cache-consistency Outbox assertions.

**Interfaces:**
- Produces: executable Host-only role administration with explicit target-tenant validation, audit writes valid in every request context, correct dashboard activity data, and event-specific Outbox retry evidence.

- [x] Add concrete response metadata for Host users, roles and menus so OpenAPI exposes their success schemas.
- [x] Keep role mutation Host-only and carry an explicit target tenant for `custom` organization-unit validation.
- [x] Change only audit inserts to `SqlDataScope.Global`; keep all audit queries `HostOnly`.
- [x] Replace the retired dashboard table reference and make the Outbox failure assertion target the provisioned event instead of unrelated pending messages.
- [x] Align integration sessions with login-rate and session-version semantics.
- [x] Re-run the focused SQL Server/MySQL regressions and the complete gates before closing Task 7.
