# Notifications Native AOT Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the Notifications module's Native AOT data, JSON, HTTP, and SignalR paths for both SQL Server and MySQL, then record the capability only after the Linux Native AOT workflow is green.

**Architecture:** Keep Notifications inside its existing module boundary. Replace runtime-shaped Dapper parameters with static dictionaries, register explicit AOT row materializers for the two Notifications query records, and exercise the published native executable through the existing external-process test harness. Reuse the existing source-generated HTTP JSON context and realtime contract instead of introducing reflection fallbacks.

**Tech Stack:** .NET 10, ASP.NET Core Native AOT, Dapper AOT boundary, System.Text.Json source generation, SignalR JSON protocol, MSTest v4 executable runner, Node.js test runners, GitHub Actions, SQL Server, MySQL, Redis.

## Global Constraints

- [ ] Before editing, read `AGENTS.md`, `rules/README.md`, `rules/development-quality.md`, `rules/code-comments.md`, `rules/naming-conventions.md`, `rules/native-aot.md`, and `.agents/skills/fullnet-module-delivery/SKILL.md` completely.
- [ ] Record `git rev-parse HEAD`, `git branch --show-current`, and `git status --short`. Preserve all pre-existing user changes. Because this work spans implementation, CI observation, and evidence updates, run `pnpm test:task:start -- notifications-native-aot-closure` before editing and use that snapshot for every affected-test command.
- [ ] Keep the implementation limited to Notifications and shared Native AOT test infrastructure directly required by this slice. Do not migrate Settings, Jobs, or unrelated modules in this plan.
- [ ] Use test-first changes: establish a failing static or governance gate before each production/infrastructure change, then make the smallest implementation that turns it green.
- [ ] Do not add reflection fallbacks, broad `DynamicDependency` roots, `RequiresUnreferencedCode` suppressions, or serializer options without generated metadata.
- [ ] Keep handwritten code comments in clear Chinese and identifiers in English.
- [ ] Do not mark Notifications as Native AOT verified from Windows discovery, analyzer success, or publish success alone. The evidence threshold is a green Linux `api-native-aot-linux` run executing both SQL Server and MySQL Notifications tests against the published native binary.

---

## Task 1: Add failing Notifications static-binding gates

**Files:**

- Modify: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`
- Inspect: `src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/HostAnnouncementQueryService.cs`
- Inspect: `src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/HostAnnouncementManagementService.cs`
- Inspect: `src/Modules/Full.NET.Modules.Notifications/Features/ManageMyInboxMessages/MyInboxQueryService.cs`
- Inspect: `src/Modules/Full.NET.Modules.Notifications/Features/ManageMyInboxMessages/MyInboxManagementService.cs`
- Inspect: `src/Modules/Full.NET.Modules.Notifications/Features/SendHostInboxMessages/HostInboxMessageService.cs`
- Inspect: `src/Modules/Full.NET.Modules.Notifications/NotificationRealtimeDelivery.cs`

- [ ] Add a test named `NotificationsModule_UsesAotSafeSqlParameters` following the existing Files/Identity static-binding test style. Scan production C# files below `src/Modules/Full.NET.Modules.Notifications` and fail with relative offender paths when a Dapper executor call receives an anonymous `new { ... }` parameter object. Exclude generated files and test projects; do not ban anonymous objects unrelated to SQL execution.
- [ ] Add a test named `NotificationsModule_RegistersAllNativeAotRowMaterializers`. Assert that `NotificationsModule` performs AOT-only registration and that the contributor explicitly registers `AnnouncementRecord` and `InboxMessageRecord`.
- [ ] Add projection-order assertions for `AnnouncementSql.ListHostSqlServer`, `AnnouncementSql.ListHostMySql`, `AnnouncementSql.FindHostById`, `InboxMessageSql.ListForRecipientSqlServer`, `InboxMessageSql.ListForRecipientMySql`, and `InboxMessageSql.FindForRecipientById`. Each record family must expose the same ordered selected columns so one explicit materializer is valid for every query in that family.
- [ ] Run the focused tests and confirm RED:

```powershell
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter "FullyQualifiedName~NotificationsModule_"
```

Expected: failure identifies the current anonymous SQL parameter call sites and the missing Notifications AOT materializer contributor/registration. If the new tests pass before production code changes, strengthen them until they detect those known defects.

## Task 2: Close Notifications Dapper AOT parameters and row materialization

**Files:**

- Modify: `src/Modules/Full.NET.Modules.Notifications/Full.NET.Modules.Notifications.csproj`
- Modify: `src/Modules/Full.NET.Modules.Notifications/NotificationsModule.cs`
- Create: `src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationsDapperAotMaterializerContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/HostAnnouncementQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/HostAnnouncementManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageMyInboxMessages/MyInboxQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageMyInboxMessages/MyInboxManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/SendHostInboxMessages/HostInboxMessageService.cs`
- Modify if required by the Task 1 gate: `src/Modules/Full.NET.Modules.Notifications/NotificationRealtimeDelivery.cs`
- Reference pattern: `src/Modules/Full.NET.Modules.Files/Persistence/FilesDapperAotMaterializerContributor.cs`

- [ ] Add a project reference from Notifications to `src/BuildingBlocks/Full.NET.Data.Dapper/Full.NET.Data.Dapper.csproj`. Do not reference Dapper directly from feature services.
- [ ] Create `NotificationsDapperAotMaterializerContributor` under `#if FULLNET_AOT_COMPILE`, implement `IDapperAotMaterializerContributor`, and register explicit readers for `AnnouncementRecord` and `InboxMessageRecord` through `DapperAotMaterializerRegistrar`.
- [ ] Build readers with `AotDataReaderExtensions`, matching the exact SQL projection order verified in Task 1. Preserve nullable values for `TenantId`, `PublishedAtUtc`, `UpdatedAtUtc`, `UpdatedByUserId`, `ReadAtUtc`, and nullable `CreatedByUserId` where declared by the records.
- [ ] In `NotificationsModule.AddServices`, invoke the contributor registration only under `#if FULLNET_AOT_COMPILE`, following the Files module pattern. Do not register a reflection-based fallback outside AOT compilation.
- [ ] Replace every anonymous SQL parameter object used by Notifications production executor calls with `IReadOnlyDictionary<string, object?>` or a small private helper returning that type. Parameter keys must exactly match the SQL placeholders. Keep request/response DTO construction unchanged.
- [ ] Run the focused architecture tests and confirm GREEN:

```powershell
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter "FullyQualifiedName~NotificationsModule_"
```

Expected: all Notifications static-binding tests pass.

- [ ] Build the module under the AOT analysis property:

```powershell
dotnet build src/Modules/Full.NET.Modules.Notifications/Full.NET.Modules.Notifications.csproj -c Release -p:FullNetAotAnalysis=true --nologo
```

Expected: build succeeds with zero Native AOT/trimming warnings introduced by Notifications.

- [ ] Commit the green production closure:

```powershell
git add tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs src/Modules/Full.NET.Modules.Notifications
git commit -m "fix: close Notifications Native AOT data paths"
```

## Task 3: Establish a failing governance contract for the Notifications native gate

**Files:**

- Modify: `tests/governance/native-aot-publish.test.mjs`
- Inspect: `eng/testing/test-matrix.json`
- Inspect: `package.json`
- Inspect: `.github/workflows/api-native-aot-linux.yml`
- Inspect: `scripts/testing/run-native-aot-s3-e2e.mjs`

- [ ] Add governance assertions requiring all of the following exact integration points:
  - `eng/testing/test-matrix.json` contains `nativeAotNotificationsIntegration` with project `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj`, filter `FullyQualifiedName~NativeApiNotifications`, minimum `2`, and timeout `45m`.
  - `package.json` exposes `test:aot:native:notifications:e2e` mapped to `node scripts/testing/run-native-aot-notifications-e2e.mjs`.
  - `.github/workflows/api-native-aot-linux.yml` runs that command after the general Native AOT external-process E2E and before provider-specific gates.
  - The new runner writes `Full.NET.IntegrationTests-native-aot-notifications.trx` below `artifacts/native-aot/linux-x64/test-results` and enforces the matrix minimum on Linux.
  - The general `nativeAotIntegration.filter` excludes `NativeApiNotifications`, preventing duplicate execution.
- [ ] Add the Notifications runner to the existing artifact-path/TRX governance table in `native-aot-publish.test.mjs`.
- [ ] Run the governance file and confirm RED because the runner, matrix entry, package script, and workflow step do not exist yet:

```powershell
node --test tests/governance/native-aot-publish.test.mjs
```

Expected: the new Notifications governance assertions fail for the missing wiring.

## Task 4: Add SQL Server/MySQL Notifications tests against the native executable

**Files:**

- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiNotificationsE2EAssertions.cs`
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiNotificationsSqlServerE2ETests.cs`
- Create: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiNotificationsMySqlE2ETests.cs`
- Modify if extraction removes duplication: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiSignalRJsonE2ETests.cs`
- Inspect: `tests/Full.NET.IntegrationTests/Notifications/NotificationsHostAnnouncementAssertions.cs`
- Inspect: `tests/Full.NET.IntegrationTests/Notifications/NotificationsInboxMessageAssertions.cs`
- Inspect: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiE2EAssertions.cs`
- Inspect: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiSignalRJsonE2ETests.cs`

- [ ] Add two `[TestClass]`, `[DoNotParallelize]` fixtures, one per provider. Each test must call `NativeApiArtifactLocator.RequireArtifact()`, create an isolated database through `SharedDatabaseFixture`, bootstrap it with `NativeApiDatabaseBootstrap`, and start the published executable through `NativeApiProcessHost`.
- [ ] In the shared assertion flow, obtain Redis through `SharedDatabaseFixture.GetRedisConnectionStringAsync()` and start the native host with both `Realtime:RedisBackplaneConnectionString` and `ConnectionStrings:redis` settings.
- [ ] Login through `NativeApiE2EAssertions.LoginAsync`, connect to `/hubs/notifications` using the SignalR JSON client and long polling, and subscribe to `ReceiveMessageAsync` as `RealtimeMessage`. If helper extraction is required, extract only the reusable connection/message-wait code; do not create a new serialization protocol.
- [ ] Exercise the announcement lifecycle with the existing contracts and exact endpoints:
  - `POST /api/v1/notifications/host-announcements` with `CreateHostAnnouncementRequest` and expect `201 Created`.
  - `PUT /api/v1/notifications/host-announcements/{id}` with `UpdateHostAnnouncementRequest` and expect `200 OK` plus incremented version.
  - `POST /api/v1/notifications/host-announcements/{id}/publish` with `PublishHostAnnouncementRequest` and expect `200 OK`, published status, and a matching announcement realtime message.
  - `GET /api/v1/notifications/host-announcements?page=1&pageSize=20` and assert the created id is present.
- [ ] Exercise the self-recipient inbox lifecycle:
  - Resolve the logged-in administrator id through `GET /api/v1/me`.
  - `POST /api/v1/notifications/host-inbox-messages` with `SendHostInboxMessageRequest(adminUserId, title, content)` and expect `201 Created` plus a matching inbox realtime message.
  - `GET /api/v1/notifications/my-inbox-messages?page=1&pageSize=20` and assert the message is present and unread.
  - `GET /api/v1/notifications/my-inbox-messages/unread-count` and assert the count is at least one.
  - `POST /api/v1/notifications/my-inbox-messages/{id}/read` and expect `200 OK`.
  - Query unread count again and assert it decreased by one; also wait for the corresponding read-state realtime message.
- [ ] Use unique titles so assertions remain isolated. Bound every realtime wait to 15 seconds and include the native process log path in assertion failures.
- [ ] Stop the native host gracefully and call `host.AssertNoFatalMarkersInLogs()` in both provider flows.
- [ ] Build the integration test executable and run discovery on the new filter:

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo
dotnet artifacts/bin/Full.NET.IntegrationTests/release/Full.NET.IntegrationTests.dll --list-tests json --no-ansi --filter "FullyQualifiedName~NativeApiNotifications"
```

Expected on Windows: exactly two or more Notifications tests are discovered. Execution may be skipped/inconclusive without the Linux native artifact; discovery is not runtime verification.

## Task 5: Wire the dedicated Notifications runner and turn governance green

**Files:**

- Create: `scripts/testing/run-native-aot-notifications-e2e.mjs`
- Modify: `eng/testing/test-matrix.json`
- Modify: `package.json`
- Modify: `.github/workflows/api-native-aot-linux.yml`
- Modify: `tests/governance/native-aot-publish.test.mjs`

- [ ] Copy the structure of `run-native-aot-s3-e2e.mjs`, selecting `matrix.nativeAotNotificationsIntegration`. Preserve direct execution of `matrix.integration.assembly`, Linux minimum enforcement, non-Linux discovery enforcement, timeout, results directory, and inherited test output.
- [ ] Name the TRX `Full.NET.IntegrationTests-native-aot-notifications.trx` and keep results below `artifacts/native-aot/linux-x64/test-results`.
- [ ] Add `nativeAotNotificationsIntegration` to the matrix with minimum `2` and timeout `45m`. Exclude `NativeApiNotifications` from the general Native AOT filter so the tests execute only in the dedicated step.
- [ ] Add `test:aot:native:notifications:e2e` to `package.json`.
- [ ] Add `Run Native AOT Notifications E2E` to `.github/workflows/api-native-aot-linux.yml` immediately after `Run Native AOT external-process E2E` and before S3/Kafka provider steps.
- [ ] Run the governance and non-Linux discovery gates:

```powershell
node --test tests/governance/native-aot-publish.test.mjs
pnpm test:aot:native:notifications:e2e
```

Expected on Windows: governance passes; the runner discovers at least two tests and completes with the permitted skipped/inconclusive policy. Do not describe this as Native AOT runtime success.

- [ ] Commit the green E2E and CI wiring:

```powershell
git add tests/Full.NET.IntegrationTests/NativeAot scripts/testing/run-native-aot-notifications-e2e.mjs eng/testing/test-matrix.json package.json .github/workflows/api-native-aot-linux.yml tests/governance/native-aot-publish.test.mjs
git commit -m "test: add Notifications Native AOT E2E gate"
```

## Task 6: Run local merge-candidate verification and push code

**Files:**

- Verify only; do not update capability documents in this task.

- [ ] Run the architecture and governance suites:

```powershell
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --nologo
pnpm test:governance
```

- [ ] Run the Native AOT analyzer and Linux publish gates available from the repository scripts:

```powershell
pnpm test:aot:analyzers
pnpm test:aot:publish:linux
```

Expected: all commands exit `0`; the publish manifest identifies the native executable. If Docker/Linux prerequisites are unavailable, report the exact blocked command and rely on GitHub Actions for that gate without claiming it passed locally.

- [ ] Run the merge-phase affected set using the task snapshot created before editing:

```powershell
pnpm test:integration:affected -- --snapshot notifications-native-aot-closure --phase merge
```

- [ ] Check repository integrity:

```powershell
git diff --check
git status --short
git log -2 --oneline
```

- [ ] Push the code commits to the authorized branch and record the resulting commit SHA:

```powershell
git push origin HEAD
```

## Task 7: Require green GitHub Actions evidence before updating status

**Files:**

- No source changes until the workflow is conclusively green.

- [ ] Open the `api-native-aot-linux` run triggered by the pushed commit. Confirm it is for the exact code SHA from Task 6, not an older run.
- [ ] Wait until all steps finish. Specifically verify `Run Native AOT Notifications E2E` passes and its log reports at least two executed tests, covering SQL Server and MySQL.
- [ ] Confirm the uploaded test-results artifact contains `Full.NET.IntegrationTests-native-aot-notifications.trx` and native process logs, and that the publish manifest artifact belongs to the same run.
- [ ] If the workflow fails, download logs/artifacts, reproduce the smallest failing gate, add or strengthen a regression test, fix it, rerun local proportional verification, commit, push, and repeat this task. Do not update capability status while any required step is red or cancelled.

## Task 8: Record the verified Notifications capability

**Files:**

- Create: `docs/verification/api-native-aot-notifications-2026-08-25.md`
- Modify: `docs/verification/api-native-aot-publish-2026-08-23.md`
- Modify: `docs/roadmap/capability-status.md`

- [ ] Create a focused verification record containing the exact commit SHA, workflow run URL/id, run conclusion, UTC/Asia-Shanghai verification time, SQL Server test name/result, MySQL test name/result, TRX artifact name, publish manifest artifact name, and any remaining scope exclusions.
- [ ] Update the publish verification record with a Notifications row that links to the focused record. Preserve prior evidence rather than replacing it.
- [ ] Update `capability-status.md` narrowly: state that the Notifications HTTP/JSON/SignalR slice is verified on the published linux-x64 Native AOT executable for SQL Server and MySQL. Do not generalize this result to Settings, Jobs, all modules, capacity, or production readiness.
- [ ] Run documentation/governance verification:

```powershell
pnpm test:governance
git diff --check
```

- [ ] Commit and push the evidence-only update:

```powershell
git add docs/verification/api-native-aot-notifications-2026-08-25.md docs/verification/api-native-aot-publish-2026-08-23.md docs/roadmap/capability-status.md
git commit -m "docs: record Notifications Native AOT verification"
git push origin HEAD
```

- [ ] Report the code commit SHA, evidence commit SHA, exact GitHub Actions run URL, every verification command with exit result, and one remaining limitation: Settings and Jobs still require their own Native AOT closure slices.

## Completion Criteria

- [ ] Notifications has no runtime-shaped anonymous Dapper parameter objects in production executor calls.
- [ ] `AnnouncementRecord` and `InboxMessageRecord` use explicit Native AOT materializers whose column order is guarded by architecture tests.
- [ ] The published Native AOT executable completes announcement, inbox, generated JSON, and SignalR flows against both SQL Server and MySQL.
- [ ] The dedicated Notifications gate is enforced by matrix, runner, package script, workflow, minimum discovery/execution count, TRX upload, and governance tests.
- [ ] Capability documentation cites the exact green GitHub Actions run and does not overstate unverified modules or production capacity.
- [ ] Rule-evolution review is recorded in the handoff. Expected result: no new rule candidate unless implementation exposes a genuinely new failure class not already covered by `rules/native-aot.md`.
