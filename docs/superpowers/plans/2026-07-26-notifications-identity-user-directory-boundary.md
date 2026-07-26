# Notifications–Identity User Directory Boundary Implementation Plan

> **Execution:** This plan is executed inline because the user requested continued autonomous development.

**Goal:** Remove Notifications' direct SQL access to `fn_identity_user` while preserving active Host recipient validation and the inbox message API contract.

**Architecture:** Notifications keeps ownership of inbox-message persistence and validates recipients through the existing in-process `IHostUserDirectory` consumer Port. Identity remains the sole owner of Host-user SQL; no transport, cache, migration, table, project, or public API is added.

**Tech Stack:** .NET 10, C#, Dapper explicit SQL, MSTest/Microsoft.Testing.Platform, SQL Server, MySQL.

## Global Constraints

- Keep the strengthened modular monolith and current module dependency `Notifications -> Identity.Contracts`.
- Preserve title/content validation, active Host-user semantics, transaction boundaries, realtime publishing, HTTP/JSON contracts, permissions, and error codes.
- Do not add per-row lookups or a generic repository; the send path needs one recipient-directory lookup.
- Preserve unrelated `.cache/` and `.tmp/art-design-pro/` files and leave the tranche uncommitted unless explicitly requested.

### Task 1: Establish the ownership RED

**Files:**
- Modify: `contracts/architecture/module-table-access-debt.json`
- Test: `tests/Full.NET.ArchitectureTests/ModuleTableOwnershipTests.cs`

- [x] Remove only the exact Notifications debt entry for `fn_identity_user` in `InboxMessageSql.cs`.
- [x] Run the focused ownership gate and capture the expected failure naming that file and table.

### Task 2: Route recipient validation through Identity

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Notifications/Persistence/InboxMessageSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/SendHostInboxMessages/HostInboxMessageService.cs`

- [x] Delete `notifications.host_inbox_recipient_exists`.
- [x] Inject `IHostUserDirectory` and call `FindActiveHostUserAsync` before inserting the message.
- [x] Preserve the existing not-found result for absent, tenant, or disabled recipients.
- [x] Build and run the focused Architecture gate to verify the debt registry now contains three exact entries.

### Task 3: Verify both providers and record evidence

**Files:**
- Create: `docs/verification/notifications-identity-user-directory-boundary-2026-07-26.md`

- [x] Run the Notifications API scenario against SQL Server and MySQL.
- [x] Run Release build, canonical Unit/Compatibility/Architecture suites, naming and project-Skill governance.
- [x] Run `git diff --check`, inspect status and branch, and record rule/Skill evolution reviews.
- [x] Keep canonical counts unchanged at `365/7/43/172`; run the full Integration suite only if focused evidence or shared composition checks expose broader risk.
