# Workflow Notification Worker Projection E2E Plan

> **For agentic workers:** Execute inline in this session; keep each test and verification step independently reviewable.

**Goal:** Prove on SQL Server and MySQL that the native Worker consumes a Workflow notification event from Outbox and creates the Notification Intent and Inbox message.

**Architecture:** Reuse the migrated test database, native Worker process, MemoryPack serializer, and existing built-in notification template path. Enqueue one Host-scope Workflow event for an active seeded Host user, wait for both the Outbox terminal state and the projected Inbox row, then stop the Worker cleanly. This adds test coverage only and leaves production behavior unchanged.

**Tech Stack:** .NET 10, MSTest, Dapper, MemoryPack, NativeAOT Worker, SQL Server, MySQL.

## Global Constraints

- Preserve the existing unrelated dirty workspace changes.
- Use UUID v7 identifiers and the existing Outbox envelope schema.
- Verify both supported database providers; do not claim production capacity or page-level verification from this worker probe.

---

### Task 1: Add SQL Server and MySQL Worker projection tests

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerSqlServerE2ETests.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerMySqlE2ETests.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerE2EAssertions.cs`
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerWorkflowNotificationProbe.cs`

**Interfaces:** The paired tests call `NativeWorkerE2EAssertions.VerifyWorkflowNotificationProjectionAsync(provider, connectionString, cancellationToken)`. The probe enqueues one `WorkflowTodoAssignedIntegrationEvent` and waits for Outbox processing plus a recipient Inbox row tied to the created Intent.

- [x] Added paired tests first. The first `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore` failed only because `VerifyWorkflowNotificationProjectionAsync` was missing.
- [x] Implemented the assertion and provider-aware probe using an active Host user, `MemoryPackIntegrationEventSerializer`, and the existing Worker process host.
- [x] `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore` succeeded with 0 warnings and 0 errors. `pnpm test:aot:worker:native:e2e` discovered 16 tests including both new cases; Windows marked all 16 Inconclusive because native Worker external-process tests run only on Linux. Linux SQL Server/MySQL execution remains assigned to Worker Native AOT CI.
- [x] `pnpm test:integration:affected:plan -- --snapshot workflow-notification-worker-projection-20260925 --phase slice` selected only the four NativeAOT test files. `git diff --check` passed.
- [ ] Commit the plan and four test files, push to `main`, and verify the resulting Worker Native AOT CI run executes the two new scenarios on Linux.
