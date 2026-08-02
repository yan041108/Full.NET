# Vue Action Authorization Hierarchy + W4–W5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 先补齐角色授权页真实的“模块/页面/操作”三级目录，再清零 Files、Notifications、Jobs、CodeGeneration、SerialNumbers 与 Document 的剩余粗粒度操作权限，使 Vue 每个受保护业务操作可独立授权、无权限不进入 DOM，并由对应 Endpoint 以同一权限码失败关闭。

**Architecture:** 延续代码拥有的 Authorization Catalog 与“页面读取权限 + 页面动作权限”模型。每个资源作为独立纵向切片，包含权限常量、Contributor 动作、Endpoint、存量角色/API Key 双库迁移、Vue、Integration/E2E、验证记录；需要新页面的 Jobs Schedules、CodeGeneration Templates 和 SerialNumbers 使用独立导航与独立页面读取权限。Layui 保持冻结，不参与任何任务。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Dapper、DbUp、SQL Server、MySQL、System.Text.Json、Vue 3、TypeScript、Element Plus、Pinia、MSTest、Vitest、Playwright。

## Global Constraints

- 执行前读取根 `AGENTS.md`、`rules/development-quality.md`、`rules/code-comments.md`、`rules/naming-conventions.md`、`rules/client-frontend.md` 与 `.agents/skills/fullnet-module-delivery/SKILL.md`。
- 整个计划启动前先从最新 `main` 创建 program 快照 `admin-action-w4-w5-program-20260803`；每个任务再创建独立 task 快照，task 快照不得复用，最终 `merge` 必须使用 program 快照覆盖全部任务。
- `ui/admin` 是唯一后台交付线；`ui/admin-layui/**`、Layui 测试/E2E/生成模板必须零修改，并运行 `node --test tests/governance/layui-freeze.test.mjs`。
- 每个调用受保护 API、读取敏感内容或产生业务副作用的 Vue 操作必须有独立稳定权限码；取消、关闭、分页等纯本地操作不建权限。
- 每个 Action 必须关联存在的页面 Navigation，权限作用域必须与页面相交；角色写入必须同时包含页面读取权限。
- 每个 Endpoint 绑定对应精确权限；直接 API 调用无权时返回 `403` 与 `authorization.permission_denied`。
- 存量迁移必须同时处理 `fn_identity_role_permission` 与 `fn_identity_api_key.PermissionsJson`，可从半完成状态重跑，SQL Server/MySQL 成对交付。
- 071–080 是本计划的候选序号，不是永久占位。每个任务开始前检查双 Provider；任一已占用时停止并重新协调，禁止改写已存在迁移。
- 每个任务先运行 RED，再写最小实现；完成时运行 affected `inner` 与 `slice`、`pnpm test:naming`、`pnpm test:governance`、`git diff --check`。
- 测试数量只更新 `eng/testing/test-matrix.json`，且只能根据任务末尾 fresh discovery 更新。

---

### Task 0: 补齐模块/页面/操作三级授权树（无迁移）

**Files:**
- Create: `src/Modules/Full.NET.Modules.Identity.Contracts/AuthorizationModuleDefinition.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/IAuthorizationCatalogContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/AuthorizationTreeContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/AuditingAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/CodeGenerationAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/DocumentAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/FilesAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/JobsAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/NotificationsAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/OrganizationAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.SerialNumbers/SerialNumbersAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/SettingsAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenancyAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Authorization/AuthorizationCatalog.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/GetAuthorizationTree/AuthorizationTreeProjector.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Serialization/IdentityJsonSerializerContext.cs`
- Modify: `packages/client-contracts/src/authorization-tree.ts`
- Modify: `packages/client-contracts/tests/authorization-tree.test.ts`
- Modify: `ui/admin/src/auth/authorization-tree-selection.ts`
- Create: `ui/admin/src/auth/authorization-tree-selection.test.ts`
- Modify: `ui/admin/src/views/RolesView.vue`
- Modify: `ui/admin/src/views/RolesView.test.ts`
- Modify: `tests/Full.NET.UnitTests/Identity/AuthorizationCatalogTests.cs`
- Modify: `tests/Full.NET.UnitTests/Identity/AuthorizationTreeProjectorTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/OpenApiAuthorizationTreeContractAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Identity/IdentityRoleManagementAssertions.cs`

**Interfaces:** every contributor declares one explicit stable module key, display name and order. The authorization response is `module -> pages -> actions`; no client inference from route, component, translated label or permission prefix is allowed. The tree exposed for cross-context Host roles must include assignable Host and Tenant pages while still excluding super-administrator-only permissions.

- [ ] **Step 1: RED** — prove the current response has no module nodes; prove Tenant-only Organization/Settings permissions cannot currently be selected in `RolesView`; freeze strict module/page/action JSON and module-level check/uncheck semantics.
- [ ] **Step 2: GREEN** — add code-owned module definitions, validate unique module keys and page ownership, project deterministic modules, and make module checking select page permissions plus their explicitly selected actions without inventing grants.
- [ ] **Step 3: Security** — preserve page-required-for-action validation; unknown modules/pages/actions fail closed; a module node is presentation/selection metadata and never a wildcard permission.
- [ ] **Step 4: Verify/commit** — Unit, OpenAPI, client-contracts, Vue, Identity dual-provider Integration and affected slice `admin-action-authorization-hierarchy-20260803`; commit `feat(identity): add module permission hierarchy`.

### Task 1: Files 上传、下载与删除权限（候选迁移 071）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Files.Contracts/HostFileContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/FilesAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Features/ManageHostFiles/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/071_FilesActionPermissions.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/071_FilesActionPermissions.sql`
- Create: `tests/Full.NET.UnitTests/Files/FilesAuthorizationContributorTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration071FilesActionPermissionsRecoveryTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Files/FilesHostFileManagementAssertions.cs`
- Modify: `tests/Full.NET.ArchitectureTests/LegacyCoarseActionPermissionRegistry.cs`
- Modify: `ui/admin/src/api/host-files.ts`
- Modify: `ui/admin/src/api/host-files.test.ts`
- Modify: `ui/admin/src/views/HostFilesView.vue`
- Create: `ui/admin/src/views/HostFilesView.test.ts`
- Modify: `tests/e2e/admin-real-stack/tests/host-files.spec.mjs`

**Interfaces:**
- Keep page permission: `files.files.read`
- Produce actions: `files.files.upload`, `files.files.download`, `files.files.delete`
- Retire: `files.files.write`
- Map: POST upload → `upload`; GET content → `download`; POST delete → `delete`
- Migration: legacy `read` keeps `read` and gains `download`; legacy `write` expands to `upload/delete`

- [ ] **Step 1: Write RED tests**

Assert Contributor actions under `host-files`, exact Endpoint bindings, migration recovery for role/API Key old-only and mixed states, and Vue DOM combinations: read-only has no upload/delete/download button; download-only shows only download; delete-only shows only delete. Add authenticated download tests proving the request carries the Bearer token and turns a successful response into a disposable Blob URL.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~FilesAuthorization|FullyQualifiedName~AuthorizationCatalogTests"
pnpm --filter @fullnet/admin exec vitest run src/views/HostFilesView.test.ts
```

Expected: missing constants/actions and coarse Vue gate assertions fail.

- [ ] **Step 3: Implement and migrate**

Define exactly:

```csharp
public const string Upload = "files.files.upload";
public const string Download = "files.files.download";
public const string Delete = "files.files.delete";
```

Use `PermissionGate` for every non-local operation and keep an imperative `session.can(...)` guard in upload/download/delete handlers. Replace `window.open(contentUrl)` with the shared authenticated HTTP client, fetch the Blob, open a short-lived object URL, and always revoke it; do not place tokens in query strings.

- [ ] **Step 4: Run GREEN and affected slice**

Remove the Files legacy allowlist entry in the same commit. Run focused Unit/Vue, Migration071 recovery, SQL Server/MySQL Files Integration, and `host-files.spec.mjs` covering successful authenticated download plus download-only/delete-only 403 scenarios; then affected `inner`/`slice` with snapshot `admin-action-files-20260803`.

- [ ] **Step 5: Record and commit**

Update inventory/verification/matrix from fresh evidence and commit `feat(files): split host file action permissions`.

### Task 2: Notifications 公告权限（候选迁移 072）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Notifications/Contracts/HostAnnouncementPermissions.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/NotificationsAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/072_NotificationsAnnouncementActionPermissions.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/072_NotificationsAnnouncementActionPermissions.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration072NotificationsAnnouncementActionPermissionsRecoveryTests.cs`
- Create: `tests/Full.NET.UnitTests/Notifications/NotificationsAuthorizationContributorTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Notifications/NotificationsHostAnnouncementAssertions.cs`
- Modify: `tests/Full.NET.ArchitectureTests/LegacyCoarseActionPermissionRegistry.cs`
- Modify: `ui/admin/src/views/HostAnnouncementsView.vue`
- Create: `ui/admin/src/views/HostAnnouncementsView.test.ts`
- Create: `tests/e2e/admin-real-stack/tests/host-announcements.spec.mjs`

**Interfaces:** `notifications.announcements.read/create/update/publish`; retire `notifications.announcements.write`; link actions to `host-announcements`.

- [ ] **Step 1: RED** — assert POST create, PUT update and POST publish use three different permissions; assert each Vue button disappears independently and direct API returns 403.
- [ ] **Step 2: GREEN** — add constants/actions/Endpoint bindings, expand legacy write to all three actions in roles and API Keys, replace `canWrite` with exact computed gates, and remove the announcement legacy allowlist entry in this task.
- [ ] **Step 3: Verify** — focused Unit, Migration072 recovery, dual-provider announcement Integration, Vue test and real-stack spec; run affected slice `admin-action-announcements-20260803`.
- [ ] **Step 4: Commit** — `feat(notifications): split announcement action permissions`.

### Task 3: Notifications 站内信发送权限（候选迁移 073）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Notifications/Contracts/InboxMessageContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/NotificationsAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/SendHostInboxMessages/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageMyInboxMessages/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/073_NotificationsInboxActionPermissions.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/073_NotificationsInboxActionPermissions.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration073NotificationsInboxActionPermissionsRecoveryTests.cs`
- Modify: Notifications Integration assertions
- Modify: `tests/Full.NET.ArchitectureTests/LegacyCoarseActionPermissionRegistry.cs`
- Modify: `ui/admin/src/views/InboxMessagesView.vue`
- Create: `ui/admin/src/views/InboxMessagesView.test.ts`
- Create: `tests/e2e/admin-real-stack/tests/inbox-messages.spec.mjs`

**Interfaces:** keep `notifications.inbox.read`; produce `notifications.inbox.send`, `notifications.inbox.mark_read`, `notifications.inbox.mark_all_read`; retire `notifications.inbox.write`; link all three actions to `inbox-messages`. GET uses `read`; send, mark-one-read and mark-all-read POST endpoints each use their exact action permission.

- [ ] **Step 1: RED** — prove read-only renders no send/mark-read/mark-all-read action; prove each POST rejects when its exact permission is absent and succeeds without either sibling action.
- [ ] **Step 2: GREEN** — bind the three Endpoints and Vue handlers independently; migrate legacy write to send, and migrate legacy read grants to mark-read/mark-all-read for backward-compatible inbox use; remove the inbox legacy allowlist entry in this task.
- [ ] **Step 3: Verify/commit** — dual-provider migration and notification tests, Vue test, real-stack independent DOM/403 scenarios, affected slice `admin-action-inbox-20260803`; commit `feat(notifications): split inbox action permissions`.

### Task 4: Jobs Definitions 权限（候选迁移 074）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Contracts/JobContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/JobsAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobDefinitions/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/074_JobsDefinitionActionPermissions.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/074_JobsDefinitionActionPermissions.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration074JobsDefinitionActionPermissionsRecoveryTests.cs`
- Modify: Jobs Unit/Integration assertions
- Modify: `tests/Full.NET.ArchitectureTests/LegacyCoarseActionPermissionRegistry.cs`
- Modify: `ui/admin/src/views/HostJobsView.vue`
- Create: `ui/admin/src/views/HostJobsView.test.ts`
- Create: `tests/e2e/admin-real-stack/tests/host-jobs.spec.mjs`

**Interfaces:** keep `jobs.definitions.read`; produce `jobs.definitions.create/update/disable/trigger`; retire `jobs.definitions.write`; link actions to `host-jobs`.

- [ ] **Step 1: RED** — test four exact Endpoint policies and independent create/edit/disable/trigger DOM; trigger must not reuse update.
- [ ] **Step 2: GREEN** — add constants/actions/bindings, migrate legacy write to four actions, replace the single `canWrite` gate, and remove the definition legacy allowlist entry in this task.
- [ ] **Step 3: Verify/commit** — Unit, Migration074, dual-provider Jobs, Vue, real stack and affected slice `admin-action-job-definitions-20260803`; commit `feat(jobs): split definition action permissions`.

### Task 5: Jobs Schedules 独立页面与权限（候选迁移 075）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Contracts/JobContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/JobsAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobSchedules/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/075_JobsScheduleActionPermissions.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/075_JobsScheduleActionPermissions.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration075JobsScheduleActionPermissionsRecoveryTests.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/HostJobScheduleServiceTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsScheduleAssertions.cs`
- Modify: `tests/Full.NET.ArchitectureTests/LegacyCoarseActionPermissionRegistry.cs`
- Modify: `packages/client-contracts/src/host-jobs.ts`
- Modify: `packages/client-contracts/tests/host-jobs.test.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Create: `ui/admin/src/views/HostJobSchedulesView.vue`
- Create: `ui/admin/src/views/HostJobSchedulesView.test.ts`
- Create: `ui/admin/src/api/host-job-schedules.ts`
- Create: `ui/admin/src/api/host-job-schedules.test.ts`
- Modify: `ui/admin/src/router/index.ts`
- Modify: `ui/admin/src/i18n/adminI18n.ts`
- Modify: `ui/admin/src/i18n/adminI18n.test.ts`
- Modify: `ui/admin/src/router/index.test.ts`
- Create: `tests/e2e/admin-real-stack/tests/host-job-schedules.spec.mjs`

**Interfaces:** page `jobs.schedules.read`; actions `jobs.schedules.create/update/pause/resume`; retire `jobs.schedules.write`; navigation ID/component key `host-job-schedules`, route `/jobs/host-schedules`.

- [ ] **Step 1: RED** — catalog/navigation test, exact Endpoint test, shared `host-jobs.ts` runtime response validators, schedule service/dual-provider assertions, four Vue button gates and direct API 403.
- [ ] **Step 2: GREEN** — extend the shared client contract instead of defining DTOs in the Vue API adapter; build the Vue page from existing schedule contracts without duplicating `HostJobsView`; migrate legacy write to four actions and remove the schedule legacy allowlist entry in this task.
- [ ] **Step 3: Verify/commit** — Migration075, both providers, Vue/API/E2E, route whitelist, affected slice `admin-action-job-schedules-20260803`; commit `feat(jobs): add exact schedule administration permissions`.

### Task 6: CodeGeneration Templates 独立页面与权限（候选迁移 076）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Contracts/CodeGenerationTemplateContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/CodeGenerationAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostTemplates/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/076_CodeGenerationTemplateActionPermissions.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/076_CodeGenerationTemplateActionPermissions.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration076CodeGenerationTemplateActionPermissionsRecoveryTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/LegacyCoarseActionPermissionRegistry.cs`
- Refactor: `ui/admin/src/views/CodeGenerationPreviewsView.vue`
- Create: `ui/admin/src/views/CodeGenerationTemplatesView.vue`
- Create: `ui/admin/src/views/CodeGenerationTemplatesView.test.ts`
- Modify: `ui/admin/src/router/index.ts`, i18n and route whitelist
- Modify: `tests/e2e/admin-real-stack/tests/host-code-generation-templates.spec.mjs`

**Interfaces:** page `codegen.templates.read`; actions `codegen.templates.create/update/delete`; retire `codegen.templates.write`; navigation ID/component key `code-generation-templates`, route `/code-generation/templates`.

- [ ] **Step 1: RED** — prove a template-reader can reach the templates page without `codegen.previews.read`; prove each action is independently hidden/403.
- [ ] **Step 2: GREEN** — extract template UI/API orchestration from the mixed preview workbench, register independent navigation/actions, bind exact Endpoints, migrate legacy write, and remove the template legacy allowlist entry in this task.
- [ ] **Step 3: Verify/commit** — CodeGeneration Unit/Integration, Migration076, Vue/route tests, existing template real-stack E2E and affected slice `admin-action-codegen-templates-20260803`; commit `feat(codegen): split template page action permissions`.

### Task 7: SerialNumbers 规则页面与权限（候选迁移 077）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.SerialNumbers/Contracts/SerialNumberContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.SerialNumbers/SerialNumbersAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.SerialNumbers/Features/ManageHostSerialRules/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/077_SerialNumberRuleActionPermissions.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/077_SerialNumberRuleActionPermissions.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration077SerialNumberRuleActionPermissionsRecoveryTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/LegacyCoarseActionPermissionRegistry.cs`
- Create: `ui/admin/src/api/serial-number-rules.ts` and test
- Create: `ui/admin/src/views/SerialNumberRulesView.vue` and test
- Create: `packages/client-contracts/src/serial-number-rules.ts`
- Create: `packages/client-contracts/tests/serial-number-rules.test.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Modify: `ui/admin/src/router/index.ts`
- Modify: `ui/admin/src/router/index.test.ts`
- Modify: `ui/admin/src/i18n/adminI18n.ts`
- Modify: `ui/admin/src/i18n/adminI18n.test.ts`
- Create: `tests/e2e/admin-real-stack/tests/serial-number-rules.spec.mjs`

**Interfaces:** page `serial_numbers.rules.read`; actions `create/update/enable/disable/preview`; retire `serial_numbers.rules.write`; navigation `serial-number-rules` at `/serial-numbers/rules`. Legacy read gains preview; legacy write expands to create/update/enable/disable.

- [ ] **Step 1: RED** — freeze strict client contracts, navigation/action catalog, five Endpoint policies, independent DOM and 403 scenarios.
- [ ] **Step 2: GREEN** — implement only the existing API surface; do not add rule semantics or new database behavior; remove the serial-number legacy allowlist entry in this task.
- [ ] **Step 3: Verify/commit** — Migration077, dual-provider SerialNumbers, Vue/API/E2E and affected slice `admin-action-serial-numbers-20260803`; commit `feat(serial-numbers): add exact rule administration permissions`.

### Task 8: Document Items 权限（候选迁移 078）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentPermissions.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/DocumentAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentItems/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/078_DocumentItemActionPermissions.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/078_DocumentItemActionPermissions.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration078DocumentItemActionPermissionsRecoveryTests.cs`
- Modify: Document Unit/Integration assertions
- Modify: `tests/Full.NET.ArchitectureTests/LegacyCoarseActionPermissionRegistry.cs`
- Modify: `ui/admin/src/views/HostDocumentItemsView.vue` and create focused test
- Modify: `tests/e2e/admin-real-stack/tests/host-documents.spec.mjs`

**Interfaces:** keep `document.host_documents.read`; produce `create/update/add_version/restore`; narrow the existing `document.host_documents.delete` to delete only; retire `document.host_documents.write`. Legacy write expands to create/update/add_version; every existing legacy delete grant keeps delete and gains restore.

- [ ] **Step 1: RED** — test five distinct policies and Vue gates, including delete and restore as different actions.
- [ ] **Step 2: GREEN** — register actions under `host-document-items`, migrate legacy write and expand legacy delete grants to restore, replace `canWrite/canDelete`, and remove the retired write/multi-action-delete allowlist bindings in this task.
- [ ] **Step 3: Verify/commit** — Migration078, Document dual-provider, Vue/E2E, affected slice `admin-action-document-items-20260803`; commit `feat(document): split item action permissions`.

### Task 9: Document Categories 权限（候选迁移 079）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentCategoryPermissions.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/DocumentAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentCategories/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/079_DocumentCategoryActionPermissions.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/079_DocumentCategoryActionPermissions.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration079DocumentCategoryActionPermissionsRecoveryTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Document/DocumentHostCategoryTagAssertions.cs`
- Modify: `tests/Full.NET.ArchitectureTests/LegacyCoarseActionPermissionRegistry.cs`
- Modify: `ui/admin/src/views/DocumentCategoriesView.vue` and create focused test
- Create: `tests/e2e/admin-real-stack/tests/document-categories.spec.mjs`

**Interfaces:** `document.categories.read/create/update/delete`; retire `document.categories.manage`. GET list/detail and navigation use category read; legacy category manage expands to all four; legacy `document.host_documents.read` gains category read to preserve item-editor lookup access.

- [ ] **Step 1: RED** — expose the current navigation/API mismatch: a role with page permission `manage` cannot call GET without unrelated host-document read.
- [ ] **Step 2: GREEN** — align navigation, GET and buttons to category permissions, migrate both legacy access paths, and remove the category legacy allowlist entry in this task.
- [ ] **Step 3: Verify/commit** — Migration079, Document dual-provider, Vue, real-stack independent DOM/403 scenarios and affected slice `admin-action-document-categories-20260803`; commit `feat(document): split category action permissions`.

### Task 10: Document Tags 权限（候选迁移 080）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentTagPermissions.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/DocumentAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentTags/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/080_DocumentTagActionPermissions.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/080_DocumentTagActionPermissions.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration080DocumentTagActionPermissionsRecoveryTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Document/DocumentHostCategoryTagAssertions.cs`
- Modify: `tests/Full.NET.ArchitectureTests/LegacyCoarseActionPermissionRegistry.cs`
- Modify: `ui/admin/src/views/DocumentTagsView.vue`
- Create: `ui/admin/src/views/DocumentTagsView.test.ts`
- Create: `tests/e2e/admin-real-stack/tests/document-tags.spec.mjs`

**Interfaces:** `document.tags.read/create/update/delete`; retire `document.tags.manage`. GET and navigation use tag read; legacy manage expands to all four; legacy host-document read gains tag read for item-editor lookup access.

- [ ] **Step 1: RED** — reproduce navigation/API mismatch and independent button/API boundaries.
- [ ] **Step 2: GREEN** — align catalog, Endpoint, migration and Vue, and remove the tag legacy allowlist entry in this task.
- [ ] **Step 3: Verify/commit** — Migration080, dual-provider Document, Vue, real-stack independent DOM/403 scenarios and affected slice `admin-action-document-tags-20260803`; commit `feat(document): split tag action permissions`.

### Task 11: Program closeout and forward-only governance

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/EndpointAuthorizationTests.cs`
- Modify: `docs/roadmap/admin-action-permission-inventory.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Create/modify: one verification record per completed resource
- Modify: `eng/testing/test-matrix.json` from fresh discovery only

- [ ] **Step 1: RED governance** — make the architecture test reject newly introduced multi-action `.write`, `.manage` or `.delete` bindings unless explicitly frozen in a finite registry.
- [ ] **Step 2: Audit retired allowances** — every resource task already removed its own stale entry. At closeout, assert `AllowedBindings` has no entries for the retired codes and the inventory remaining list is empty; do not defer resource cleanup to this task.
- [ ] **Step 3: Run fresh gates**

```powershell
pnpm test:governance
pnpm test:naming
pnpm test:sql-safety
pnpm test:openapi
pnpm --filter @fullnet/client-contracts test
pnpm --filter @fullnet/admin test
pnpm test:dotnet:unit
pnpm test:dotnet:architecture
node --test tests/governance/layui-freeze.test.mjs
git diff --check
```

Run the final affected `merge` phase using the program snapshot `admin-action-w4-w5-program-20260803`, not the last task snapshot; full Integration remains for `main` CI according to repository rules.

- [ ] **Step 4: Close status** — only after fresh tests, mark W4/W5 complete and “菜单、页面与按钮权限管理” Verified; do not cite Layui as evidence.
- [ ] **Step 5: Commit** — `docs(authorization): close exact action permission rollout`.

## Cursor execution order

Execute strictly: **0 Authorization hierarchy → 1 Files → 2 Announcements → 3 Inbox → 4 Job Definitions → 5 Job Schedules → 6 CodeGeneration Templates → 7 SerialNumbers → 8 Document Items → 9 Categories → 10 Tags → 11 Closeout**. One task equals one reviewable commit; do not batch migrations or share task snapshots between tasks. Preserve the program snapshot until Task 11 merge validation completes.
