# Workflow `notify.cc` Vertical Slice Implementation Plan

> **Execution:** Continue in this task with `superpowers:executing-plans`; use RED-GREEN-REFACTOR checkpoints and stop if an architectural assumption below proves false.

**Goal:** Make `notify.cc` a publishable, executable Workflow node with safe recipient configuration, non-blocking runtime persistence, tenant-scoped “My CC” read APIs, and a usable Vue administration surface.

**Architecture:** Workflow remains the owner of definition versions, execution steps, and `fn_workflow_cc`. A CC node is a synchronous, read-only knowledge projection inside the Workflow transaction: it writes completed CC steps and de-duplicated recipient records, never creates a todo, and never calls Notifications in the transaction. Identity is accessed only through its existing read-only user directory ports for definition-time candidate selection and active-recipient validation. The runtime remains a closed linear plan of `start -> (notify.cc | human.approval)+ -> end`, with at least one approval node.

**Tech Stack:** .NET 10 Minimal APIs, Dapper explicit SQL, SQL Server/MySQL, System.Text.Json source generation, Native AOT static materializers, Vue 3, TypeScript, Element Plus, Vitest, Playwright, MSTest, generated OpenAPI client contracts.

---

## Invariants and acceptance criteria

- `notify.cc` config is closed to `recipientUserIds`: 1–20 unique, non-empty GUIDs. Script, URL, callback, template, and arbitrary payload configuration remain rejected.
- Publishing verifies every configured recipient is an active Identity user before the Workflow transaction claims the draft.
- Starting or approving a workflow executes consecutive CC nodes in graph order, creates a completed `fn_workflow_step` per CC node, and creates at most one `fn_workflow_cc` row per instance/recipient as required by the existing unique key.
- A rejected or cancelled path does not execute downstream CC nodes. CC creation failure rolls back the local Workflow state transition; later Notifications integration remains an asynchronous, non-rollback concern.
- CC recipients can list and mark only their own tenant-scoped records and count as instance participants for instance/detail/log reads.
- All new permissions, routes, response models, SQL identifiers, JSON fields, and client contracts use stable English machine codes. All handwritten backend declarations and key business blocks have the required Chinese XML/comments.
- No database migration is needed: migration 102 already owns the required table, foreign keys, and uniqueness constraint.

### Task 1: Lock compiler and runtime semantics with failing unit tests

**Files:**
- Modify: `tests/Full.NET.UnitTests/Workflow/WorkflowNodeTypeCatalogTests.cs`
- Modify: `tests/Full.NET.UnitTests/Workflow/WorkflowDefinitionCompilerTests.cs`
- Modify: `tests/Full.NET.UnitTests/Workflow/WorkflowRuntimePlanTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowNodeTypeCatalog.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowDefinitionCompiler.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowRuntimePlan.cs`

1. Add RED tests proving `notify.cc` is publishable/executable, valid recipient arrays compile, malformed/empty/duplicate/over-limit/non-GUID recipients fail with `workflow.definition.cc_recipients_invalid`, and gateway remains unavailable.
2. Add RED runtime-plan tests for CC before the first approval, between approvals, after the last approval, consecutive CC nodes, reject-path non-execution, and the still-invalid no-approval/branched topology.
3. Run `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --filter "FullyQualifiedName~WorkflowNodeTypeCatalogTests|FullyQualifiedName~WorkflowDefinitionCompilerTests|FullyQualifiedName~WorkflowRuntimePlanTests"` and record the expected failures.
4. Implement the minimum closed config validation and ordered runtime transition model. Preserve canonical JSON and schema version 1.
5. Re-run the focused tests to GREEN, then run `git diff --check`.

### Task 2: Validate design-time recipients through the Identity contract port

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Workflow/Contracts/WorkflowErrorCodes.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/Contracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/WorkflowDefinitionManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Serialization/WorkflowJsonSerializerContext.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/WorkflowModule.cs`
- Modify: `tests/Full.NET.UnitTests/Workflow/WorkflowDefinitionManagementServiceTests.cs`
- Modify: `tests/Full.NET.UnitTests/Workflow/WorkflowManagementContractTests.cs`

1. Add RED service tests proving publish rejects an inactive/missing recipient before draft claim and accepts valid active users. Add endpoint metadata coverage for `GET /api/v1/workflow/definitions/recipient-candidates` under `workflow.definitions.read`.
2. Inject `IHostUserDirectory` into definition publication for active-recipient validation and `IHostUserSelectionDirectory` into a bounded candidate endpoint (page size capped by the Identity port). Do not query Identity tables or join across modules.
3. Add source-generated response metadata and Chinese XML documentation for every new/changed backend declaration and parameter.
4. Run the focused Workflow management tests to GREEN.

### Task 3: Execute and persist CC transitions atomically

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowRecords.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowDapperAotMaterializerContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageInstances/WorkflowInstanceManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyTodos/WorkflowTodoManagementService.cs`
- Add: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowCcTransitionWriter.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/WorkflowModule.cs`
- Modify: `tests/Full.NET.UnitTests/Workflow/WorkflowManagementContractTests.cs`

1. Add RED contract/service tests for start-time and approve-time CC execution, de-duplication across nodes, completed step/log semantics, continuation to the next approval, and completion after a trailing CC.
2. Add provider-neutral explicit SQL for completed CC steps, existing recipient lookup, CC insert, and execution logs. Keep the existing table schema unchanged.
3. Implement a scoped Workflow-owned transition writer shared by instance start and todo approval. It must receive a precompiled transition, never re-evaluate client data, and execute inside the caller’s local transaction.
4. Update instance participant SQL so a scoped CC recipient can read the corresponding instance and logs.
5. Register new Dapper AOT row materializers and DI services; run focused unit/architecture tests and the Dapper/Native AOT governance selectors.

### Task 4: Add tenant-scoped “My CC” API and precise permissions

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Workflow/Contracts/WorkflowPermissions.cs`
- Add: `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyCc/Contracts.cs`
- Add: `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyCc/WorkflowCcManagementService.cs`
- Add: `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyCc/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowRecords.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowDapperAotMaterializerContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Serialization/WorkflowJsonSerializerContext.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/WorkflowAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/WorkflowModule.cs`
- Modify: `tests/Full.NET.UnitTests/Workflow/WorkflowAuthorizationContributorTests.cs`
- Modify: `tests/Full.NET.UnitTests/Workflow/WorkflowManagementContractTests.cs`

1. Add RED tests for `GET /api/v1/workflow/cc/mine` (`workflow.cc.read`) and `POST /api/v1/workflow/cc/{ccId}/read` (`workflow.cc.mark_read`), including actor binding, tenant isolation, idempotent marking, and forbidden cross-user access.
2. Implement SQL Server/MySQL bounded list statements ordered by `CreatedAtUtc DESC, Id DESC`, returning business identity and node context without cross-module joins.
3. Implement the list/mark-read service and endpoint. Mark-read may update only a row matching the trusted current user and `TenantScopeKey`; an already-read own row succeeds idempotently.
4. Register AOT JSON/Dapper metadata, permissions, navigation `/workflow/cc`, and action metadata. Run focused tests to GREEN.

### Task 5: Generate client contracts and expose safe CC recipient editing

**Files:**
- Modify: `ui/admin/src/workflow/workflow-vue3-adapter.ts`
- Modify: `ui/admin/src/workflow/workflow-vue3-adapter.test.ts`
- Modify: `ui/admin/src/workflow/WorkflowVue3Designer.vue`
- Modify: `ui/admin/src/workflow/WorkflowVue3Designer.test.ts`
- Modify: `ui/admin/src/workflow/vendor/workflow-vue3/src/components/addNode.vue`
- Modify: `ui/admin/src/workflow/vendor/workflow-vue3/src/components/nodeWrap.vue`
- Modify: `ui/admin/src/api/workflow-definitions.ts`
- Modify generated files under: `packages/client-contracts/src/generated/`

1. Add RED adapter tests for closed `recipientUserIds` round trips and unsafe/invalid config rejection; add component tests proving the server-enabled CC entry appears and selected candidates change the emitted draft.
2. Add the recipient candidate selector drawer to the wrapper component. Keep `nodeUserList` as display-only/transient Workflow-Vue3 state; persist only validated GUIDs in `recipientUserIds`.
3. Run the OpenAPI snapshot/client generation command from repository scripts and accept only deterministic generated changes for the new endpoints/models.
4. Run targeted Vitest and contract-coverage checks.

### Task 6: Deliver the Vue “My CC” page

**Files:**
- Add: `ui/admin/src/api/workflow-cc.ts`
- Add: `ui/admin/src/api/workflow-cc.test.ts`
- Add: `ui/admin/src/views/WorkflowCcView.vue`
- Add: `ui/admin/src/views/WorkflowCcView.test.ts`
- Modify: `ui/admin/src/router/index.ts`
- Modify: `ui/admin/src/navigation/catalog.ts`
- Modify: `ui/admin/src/navigation/catalog.test.ts`
- Modify: relevant locale files under `ui/admin/src/i18n/`

1. Add RED API/view/router/navigation tests for list, unread styling, idempotent mark-read, instance identity display, loading/error/empty states, and permission-hidden action.
2. Implement a responsive table/list page at `/workflow/cc`; use generated client operations and `PermissionGate` for mark-read.
3. Keep the first slice read-only: do not add approval actions, Notifications Inbox coupling, or client-side recipient authorization.
4. Run targeted Vitest, typecheck, ESLint, and the production admin build/bundle budget.

### Task 7: Prove dual-provider and Native AOT closure

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Workflow/WorkflowRuntimeApiAssertions.cs`
- Modify as needed: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiWorkflowE2EAssertions.cs`
- Modify as needed: Workflow integration/OpenAPI assertions and `eng/testing/test-matrix.json` only if a selector entry is genuinely new.

1. Extend shared SQL Server/MySQL assertions to publish a definition containing CC-before/middle/trailing cases, start/approve it, list and mark the recipient’s CC, verify de-duplication, verify a non-recipient is forbidden, and verify reject skips downstream CC.
2. Add/extend Native AOT assertions for CC endpoint serialization and Dapper materialization.
3. Run local inner verification:
   - `pnpm test:integration:affected:plan -- --snapshot workflow-notify-cc-20260904 --phase inner`
   - the selectors reported by that plan that do not require Docker/Testcontainers
   - `dotnet build Full.NET.slnx --no-restore`
   - `pnpm test:inner -- --snapshot workflow-notify-cc-20260904 --plan`
   - `git diff --check`
4. Do not run local full real-stack suites. After commit/push authorization already established in this task history, push the exact commit and verify GitHub Actions SQL Server, MySQL, API Native AOT, Worker Native AOT, build/test, client, and integration gate results. Distinguish known unrelated real-stack debt from regressions introduced by this slice.

### Task 8: Update authoritative status and close the slice

**Files:**
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/verification/2026-08-31-workflow-first-slice-closeout.md`
- Add: `docs/verification/2026-09-04-workflow-notify-cc-verification.md`
- Modify: `docs/superpowers/plans/2026-09-04-vform3-esm-source-adaptation.md`
- Modify: `docs/verification/2026-08-30-admin-form-designer-module.md`

1. Record only fresh evidence: exact commit, local commands, GitHub Actions run URLs/results, SQL Server/MySQL CC assertions, and Native AOT closure.
2. Close the previous VForm3 plan’s pending Actions checkbox using run `33880652055` and the successful AOT runs, while explicitly noting unrelated broad real-stack failures.
3. Mark `notify.cc` complete but leave `gateway.exclusive`, durable recovery, Notifications integration-event projection, and capacity certification open.
4. Run `git status --short --branch`, `git diff --check`, rule-evolution review, and skill-evolution review before the final commit/push.
