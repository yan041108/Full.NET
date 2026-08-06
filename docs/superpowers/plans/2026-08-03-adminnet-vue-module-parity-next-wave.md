# Admin.NET Vue Module Parity Next Wave Implementation Plan

> **2026-08-06 审查覆盖说明：** Cursor 已实现 Task 1/2 的部分能力，但尚未关闭 Document 引用对账、Jobs 预览权限以及当前 Identity/Organization WIP 的双库门禁。后续执行顺序和修正后的验收条件以 [`2026-08-06-cursor-adminnet-review-followup.md`](2026-08-06-cursor-adminnet-review-followup.md) 为准；本文件保留模块产品语义，不得据此把 Task 1/2 标记为完成。

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans`; each task must finish RED→GREEN and verification before starting the next task.

**Goal:** 在不复制 Admin.NET 源码、不破坏 Full.NET 架构边界的前提下，补齐当前 Vue 后台已暴露模块的关键产品能力、合理交互、表字段和精确权限。

**Architecture:** Vue 是唯一持续交付的后台；每个页面和业务操作使用独立稳定权限码，Endpoint 失败关闭。模块通过 Contracts 或窄用例接口协作，禁止跨模块引用实现项目。数据库仅由 Migrator 以成对 SQL Server/MySQL 迁移修改，业务访问保持 Dapper 显式 SQL和单往返分页。

**Tech Stack:** .NET 10、Minimal API、Dapper、DbUp、SQL Server/MySQL、Vue 3、Pinia、Element Plus、Vitest、Playwright。

## Global Constraints

- 严格按 Task 1→9 顺序执行；一个任务一个 fresh snapshot、一个可审查提交，不并行占用共享 .NET/Docker 或迁移号。
- 每次写迁移前重新列出两库最新编号。2026-08-03 当前最大为 `080`，`081` 只是候选，发现已占用就停止并协调，不自动抢号。
- 先写可失败测试，再写生产代码。每个写 Endpoint 都覆盖：按钮缺失、直接 API `403 authorization.permission_denied`、乐观并发、审计和双库恢复。
- 所有列表必须返回真实 `page/pageSize/total`，Vue 必须提供分页、加载、空态和 ProblemDetails；禁止固定只取前 20 条后把 `items.length` 当总数。
- 不修改 `ui/admin-layui/**`，不生成 Layui 页面，不把 Layui 纳入新功能验收。
- Admin.NET 只用于产品语义和交互参考；禁止复制源码、资源、动态程序集任务、脚本任务、公共文件 URL 和物理存储路径。
- 完成每个纵向切片后运行同一 snapshot 的 affected inner/slice；合并候选最后运行 merge。永久测试数量只改 `eng/testing/test-matrix.json`。

---

### Task 1: 建立 Document 自包含的上传版本与下载边界（P0）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentPermissions.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentItems/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentItems/HostDocumentItemManagementService.cs`
- Create or modify narrow content-reader and upload-writer use-case contracts under `src/Modules/Full.NET.Modules.Files.Contracts/**`; the current contracts project has no upload writer, so this is required rather than optional
- Modify the Files implementation, module registration and Files contract tests to implement that upload writer; do not reference `Full.NET.Modules.Files` implementation from Document
- Modify: `ui/admin/src/api/host-document-items.ts`
- Modify: `ui/admin/src/views/HostDocumentItemsView.vue`
- Test: Document Unit, API/OpenAPI, SQL Server/MySQL Integration, Vue and real-stack E2E

**Behavior:**

- Add exact `document.host_documents.download`; retain the existing stable `document.host_documents.add_version` for upload-version and do not create a duplicate permission code. Keep read/create/update/delete independent.
- Document Endpoint resolves the requested document/version, verifies it is active, then consumes a narrow Files contract returning stream, content type and safe original name. It must never expose `StorageKey` or local path.
- Upload-version Endpoint owns the Document permission and calls a new narrow Files upload-writer contract with stream, safe original name and content type. The opaque result contains only file reference ID and safe metadata needed by Document.
- Files remains the sole owner of Provider selection, actual-size/hash calculation, Pending→Publishing→Ready lifecycle, read-back reconciliation and commit-uncertainty compensation. Document must not duplicate or partially reimplement that state machine.
- Synchronous release is allowed only when the Document transaction can prove it rolled back before commit. If commit outcome is unknown, do not delete or release the Blob: retain the opaque file reference and let delayed reconciliation wait through a configured grace period, then query a narrow Document reference-reader or validate a one-time claim token before purging an unreferenced file. Dual-provider tests must cover committed-but-threw, definite rollback, worker-won reconciliation and commit-uncertain retry; a referenced Blob must never be deleted.
- Vue no longer needs `files.files.download` or `files.files.upload`; it gates download with the new Document code and version upload with the existing `document.host_documents.add_version`.
- Add dual-provider tests proving a user with Document permission succeeds without broad Files permissions, while direct bypass without the exact Document permission returns 403.

**Verification:** focused RED→GREEN, `pnpm test:openapi`, client-contracts, Vue focused/full, affected SQL Server/MySQL slice, migration recovery if permissions require a new migration, and `git diff --check`.

### Task 2: 让 Jobs 计划页自包含并完成分页交互（P0）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Contracts/JobContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobSchedules/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobSchedules/HostJobScheduleService.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs`
- Modify: `ui/admin/src/api/host-job-schedules.ts`
- Modify: `ui/admin/src/views/HostJobSchedulesView.vue`
- Test the matching Unit, API/OpenAPI, dual-provider Integration and Vue files

**Behavior:**

- Schedule list response includes definition display name under `jobs.schedules.read` and returns single-roundtrip `page/pageSize/total`.
- Expose a separate enabled definition-options Endpoint `{ id, jobKey, displayName }` under `jobs.schedules.create`; do not authorize it with `jobs.definitions.read` or broaden it to `jobs.schedules.read`.
- Vue adds search, status, trigger kind, pagination, IANA timezone selection, Cron help/next-run preview, ISO date-time picker and visible misfire policy.
- Table shows Cron/one-time expression, timezone, state, misfire policy, next and last execution. Create/update/pause/resume remain separate permissions.
- Tests must reproduce the original cross-permission 403 and prove the page is now self-contained.

### Task 3: 补齐公告类型、受众和发布/撤回状态机

**Files:**
- Modify: Notifications announcement contracts, records, SQL, services, Endpoint and serializer context
- Modify: `ui/admin/src/api/host-announcements.ts`
- Modify: `ui/admin/src/views/HostAnnouncementsView.vue`
- Create: paired candidate migration only after rechecking the current maximum
- Test: Notifications Unit, migration recovery, SQL Server/MySQL API, OpenAPI/contracts, Vue and E2E

**Approved table semantics:**

- Announcement owns stable `Kind` (`notice`/`announcement`), `AudienceKind` (`all`/`users`/`organizations`), lifecycle status (`draft`/`published`/`retracted`), `PublishedAtUtc/PublishedByUserId`, `RetractedAtUtc/RetractedByUserId`, audit columns and `Version`.
- Target user/organization IDs live in normalized child rows with uniqueness and foreign-key/ownership validation; do not copy display names into target rows as authority.
- Update is draft-only; publish and retract are explicit idempotent state transitions with independent permissions and audit. Publish uses the existing reliable event boundary where delivery requires it.
- Vue uses filters, paged table, audience picker/summary, rich status feedback and destructive confirmation. Do not add an unreviewed rich-text renderer; content remains safely rendered until sanitization policy is approved.

### Task 4: 把站内信收件人 UUID 输入改为可授权选择器

**Files:**
- Modify the Notifications send/inbox contracts and endpoints only as needed
- Consume an existing Identity host-user projection through Contracts; if absent, add a narrow paged candidate contract, not an Identity implementation reference
- Modify: `ui/admin/src/views/InboxMessagesView.vue` and matching API/tests

**Behavior:**

- Sender selects active recipients through paged search by username/display name; raw UUID is never the primary interaction.
- Show selected recipient count and confirmation before send. Empty, duplicated, disabled or cross-scope recipients fail closed on the server.
- Inbox adds server pagination, unread/status filters and independent mark-one/mark-all-read permissions only if the API has corresponding independent side effects.
- Cover exact 403, large recipient selection, duplicate collapse, tenant/host boundary and visible ProblemDetails.

### Task 5: 提升 SerialNumbers 规则管理体验，不混淆规则与计数状态

**Files:**
- Modify: `ui/admin/src/api/serial-number-rules.ts`
- Modify: `ui/admin/src/views/SerialNumberRulesView.vue`
- Modify server contracts/query only when pagination/filter fields are missing
- Test matching Vue, API and dual-provider Integration files

**Behavior:**

- Add server pagination and filters for key/name/scope/reset interval/status.
- Form exposes Pattern、ResetInterval、Min/Max、DisplayOrder、Scope and enabled state with inline validation and examples. Use a real UTC date-time input; remove hard-coded preview dates.
- Preview displays rendered value, reset bucket and next sequence without mutating counter state.
- Keep allocation counters in the existing state table; do not copy Admin.NET's mutable current sequence into the rule row. Do not add expiry/remark fields without an approved consumer.

### Task 6: 建立 Jobs 执行历史与只读运行健康页面

**Files:**
- Extend existing `ManageHostJobExecutions` contracts/query/Endpoint; do not introduce dynamic job code
- Add Vue execution-history view, API, navigation catalog, i18n and tests
- Add exact read/retry/cancel operations only for side effects that really exist

**Behavior:**

- Paged history filters by definition, schedule, status and time range; detail shows attempts, elapsed time, safe error code, next retry and correlation identifiers without exception body/secrets.
- A read-only health panel exposes registered allowlisted handlers, queue/backlog summaries and Worker instance heartbeat only from bounded low-cardinality data.
- Reuse typed `JobHandlerRegistry`; explicitly reject Admin.NET `AssemblyName`/arbitrary type/script fields.
- Any retry/cancel operation requires a separately approved state machine, exact permission, affected-row invariant and dual-provider tests; otherwise ship read-only history first.

### Task 7: 完成 Files 目录分页筛选与安全预览策略

**Files:**
- Modify Files contracts/query SQL/Endpoint only for paged filters
- Modify: `ui/admin/src/api/host-files.ts`
- Modify: `ui/admin/src/views/HostFilesView.vue`
- Test matching Unit, SQL Server/MySQL Integration, Vue and E2E files

**Behavior:**

- Add original-name/content-type/provider/status/time-range filters and true server pagination.
- Table shows safe business metadata, actual size, content type, provider and lifecycle status; never return `StorageKey`, physical path or a permanent public URL.
- Preview is allowlisted by safe content type and size. Unsupported content downloads through authenticated Blob flow; active content never executes inline without an approved sanitization/CSP policy.
- Preserve all existing upload reconciliation, compensation, affected-row and actual-size invariants.

### Task 8: 提升 CodeGeneration 模板目录交互

**Files:**
- Modify CodeGeneration template list contracts/query only when server pagination/filter support is missing
- Modify: `ui/admin/src/api/code-generation-templates.ts`
- Modify: `ui/admin/src/views/CodeGenerationTemplatesView.vue`
- Test matching client-contracts, API, Vue and E2E files

**Behavior:**

- Add true server pagination and name/owner/module/entity filters; show `total` rather than `items.length`.
- Replace the raw-JSON-only primary workflow with a typed Schema form for stable fields and columns while preserving an advanced JSON mode with strict validation.
- Before update, show a normalized Schema diff and validation report. Version conflicts preserve the user's draft and offer reload/compare rather than silently replacing it.
- Keep the existing delete confirmation and confirmation-in-flight re-entry guard. Add version history only after a bounded immutable template-revision model is approved; do not infer history from mutable audit fields.
- Never execute generated code in the browser or expose secret-bearing generated artifacts.

### Task 9: Document 完整插件能力分四个独立切片推进

Do not implement all four in one commit. Before 9A, update the approved Document spec with the exact tables, permissions and state machines below; each subtask gets its own snapshot, migration pair and merge gate.

- **9A Core library:** category tree (`ParentId`, stable `Code`, display order, icon/color as presentation metadata), tag color and assignments, version history with change description, and persistent recycle-bin query/restore/purge.
- **9B Sharing:** opaque high-entropy share token, optional password hash, expiry, max access count, view/download capability and rate limit. Never store plaintext password or derive authority from a public URL.
- **9C Document ACL:** normalized grant rows for user/organization/role plus action (`view`/`download`/`edit`/`manage`); validate through Identity/Organization contracts and combine with host/tenant data scope. Deny unknown objects and empty ACL ambiguity explicitly.
- **9D Preview, audit and statistics:** safe preview adapters, immutable operation log and bounded aggregates. Virus scanning is a Provider boundary and remains optional until a real engine and quarantine state machine are approved.

Each subtask must deliver exact module/page/action authorization, SQL Server/MySQL migration and recovery, standard API/ProblemDetails, Vue interaction, E2E, operations documentation and license provenance.

## Final Program Verification

After Task 9D, create one new program snapshot and run:

1. `pnpm test:integration:affected:plan -- --snapshot <literal-id> --phase merge` and inspect every selected shard.
2. `pnpm test:integration:affected -- --snapshot <literal-id> --phase merge`; require non-zero SQL Server and MySQL discovery and Docker teardown residual `0`.
3. `pnpm test:dotnet:unit`, `pnpm test:dotnet:architecture`, `pnpm test:openapi`, client-contracts and Vue full suites.
4. `pnpm test:naming`, `pnpm test:sql-safety`, `pnpm test:governance`, `pnpm test:skills`, `git diff --check` and `git status --short`.
5. Update capability status only from fresh evidence. `Build-verified` is the default; `Verified` requires all product, dual-provider, permission, Vue and E2E gates, not merely a successful build.
