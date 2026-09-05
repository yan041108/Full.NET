# Workflow Built-in Notification Templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure the four Workflow lifecycle events can create inbox notifications without requiring administrators to pre-create templates.

**Architecture:** Notifications owns a closed catalog of four safe inbox templates. The Workflow event consumer ensures a missing template and its immutable first version are created and published atomically in the trusted event tenant scope, then invokes the existing Intent pipeline. Existing published templates are reused, while an existing unpublished template fails closed and is never overwritten.

**Tech Stack:** .NET 10, Dapper abstractions, SQL Server/MySQL-compatible parameterized SQL, MSTest, NSubstitute.

## Global Constraints

- Keep all writes inside Notifications-owned tables and the Envelope-derived tenant scope.
- Do not seed from API/Worker startup and do not query Tenancy-owned tables.
- Use UUID v7 through `IIdGenerator`; stable template keys remain unchanged.
- Add Chinese XML documentation and nearby Chinese comments for business invariants.
- Defer page-level acceptance; verify the backend slice and governance only.

---

### Task 1: Add the built-in Workflow template catalog and atomic provisioner

**Files:**
- Create: `src/Modules/Full.NET.Modules.Notifications/Features/ProjectWorkflowNotifications/WorkflowNotificationTemplateCatalog.cs`
- Create: `src/Modules/Full.NET.Modules.Notifications/Features/ProjectWorkflowNotifications/WorkflowNotificationTemplateProvisioner.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationPlatformSql.cs`
- Test: `tests/Full.NET.UnitTests/Notifications/WorkflowNotificationTemplateProvisionerTests.cs`

**Interfaces:**
- Consumes: `NotificationInboxScope`, `IQueryExecutor`, `ICommandExecutor`, `ICommandTransaction`, `IClock`, `IIdGenerator`.
- Produces: `Task EnsurePublishedAsync(NotificationInboxScope scope, Guid actorUserId, string templateKey, CancellationToken cancellationToken)`.

- [x] **Step 1: Write failing tests** for missing-template creation, published-template reuse, and unpublished-template failure closure.
- [x] **Step 2: Run the focused tests** and confirm failure is caused by the absent catalog/provisioner.
- [x] **Step 3: Implement the closed catalog and atomic provisioner** using normalized template content, one local transaction, and tenant-bound insert SQL.
- [x] **Step 4: Run the focused tests** and confirm all scenarios pass.

### Task 2: Connect provisioning to Workflow event projection

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ProjectWorkflowNotifications/WorkflowNotificationProjectionService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/NotificationsModule.cs`
- Modify: `tests/Full.NET.UnitTests/Notifications/NotificationsModuleRegistrationTests.cs`

**Interfaces:**
- Consumes: `WorkflowNotificationTemplateProvisioner.EnsurePublishedAsync(...)`.
- Produces: Workflow event projection that guarantees its template before creating an Intent.

- [x] **Step 1: Add a failing registration/projection test** that requires the new provisioner.
- [x] **Step 2: Run the focused test** and confirm the missing behavior.
- [x] **Step 3: Register and invoke the provisioner** before the existing Intent service.
- [x] **Step 4: Run Notifications-focused tests** and confirm the projection remains idempotent.

### Task 3: Verify and document the slice

**Files:**
- Modify: `eng/testing/test-matrix.json`
- Modify: relevant Notifications/Workflow roadmap and verification documents identified by repository search.
- Create: `docs/verification/2026-09-05-workflow-built-in-notification-templates.md`

- [x] **Step 1: Update the unit-test discovery floor** by the exact number of newly added tests.
- [x] **Step 2: Run focused unit tests, Release builds, naming, AOT analyzer, architecture, integration partition, and governance checks.**
- [x] **Step 3: Record fresh evidence and remaining Worker/database-heavy validation** without marking page acceptance complete.
- [x] **Step 4: Run `git diff --check` and inspect final status.**
