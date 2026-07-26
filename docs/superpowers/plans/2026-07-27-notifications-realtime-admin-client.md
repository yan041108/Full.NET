# Notifications Realtime Admin Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Vue 与 Layui 管理端在认证会话内连接 Notifications SignalR Hub，并以同一稳定消息契约刷新未读徽标和当前通知页面。

**Architecture:** `@fullnet/client-contracts` 提供 SignalR 连接生命周期、消息守卫和传输无关的会话接口；Access Token 仍只从身份会话闭包按需读取，不进入快照、持久化或日志。Vue 与 Layui 分别保留自己的 UI 状态和刷新适配器，只共享协议与连接实现。

**Tech Stack:** TypeScript 7、Vitest 4、Vue 3/Pinia、原生 JavaScript/Layui、`@microsoft/signalr` 10.0.0。

## Global Constraints

- Hub 路径固定为 `/hubs/notifications`，客户端方法固定为 `ReceiveMessageAsync`。
- 稳定机器码仅接受 `notifications.announcement.published`、`notifications.inbox.message.received`、`notifications.inbox.unread.changed` 和 `realtime.probe.self`。
- 切换 Host/租户上下文必须重建连接，避免保留旧 SignalR 分组；匿名、退出和销毁必须停止连接。
- 连接失败不得破坏登录或页面 HTTP 主流程；自动重连只使用 SignalR 客户端的有界重试序列。
- Access Token 不进入 `IdentitySessionSnapshot`、Web Storage、DOM、日志或错误消息。
- Vue 与 Layui 必须覆盖相同实时行为，不共享框架 UI 源码。
- 不修改 Realtime 后端、Outbox、日志、数据库迁移或 OpenAPI 门禁。

---

### Task 1: Shared realtime contract and authenticated lifecycle

**Files:**
- Create: `packages/client-contracts/src/notifications-realtime.ts`
- Create: `packages/client-contracts/tests/notifications-realtime.test.ts`
- Modify: `packages/client-contracts/src/identity-session.ts`
- Modify: `packages/client-contracts/tests/identity-session.test.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Modify: `packages/client-contracts/package.json`
- Modify: `pnpm-lock.yaml`

**Interfaces:**
- Produces: `RealtimeMessage`, `NotificationsRealtimeController`, `createNotificationsRealtimeController(options)`.
- Consumes: `IdentitySessionController.snapshot()`, `subscribe()` and the new transport-only `readAccessToken()`.

- [x] **Step 1: Write failing contract and lifecycle tests**

  Add tests proving malformed messages are ignored, authenticated sessions start once, tenant identity changes stop then restart, anonymous sessions stop, `dispose()` removes the handler, and reconnect token reads use the latest in-memory token.

- [x] **Step 2: Run the focused tests and verify RED**

  Run: `pnpm --filter @fullnet/client-contracts test -- notifications-realtime.test.ts identity-session.test.ts`

  Expected: FAIL because the realtime exports and `readAccessToken()` do not exist.

- [x] **Step 3: Implement the minimal shared controller**

  Build `HubConnectionBuilder().withUrl('/hubs/notifications', { accessTokenFactory }).withAutomaticReconnect([0, 2000, 10000, 30000])`, register `ReceiveMessageAsync`, and serialize start/stop transitions by the authenticated `sessionId + tenantId` key. Catch transport start failures so authentication remains usable.

- [x] **Step 4: Expose the transport-only token reader**

  Return `readAccessToken: () => token?.accessToken` from the identity-session closure. Keep it out of snapshots and clear it before logout requests, as existing behavior requires.

- [x] **Step 5: Run focused and package tests**

  Run: `pnpm --filter @fullnet/client-contracts test`

  Expected: all client-contract tests pass with no unhandled rejection.

### Task 2: Vue realtime state and shell integration

**Files:**
- Create: `ui/admin/src/notifications/realtime.ts`
- Create: `ui/admin/src/notifications/realtime.test.ts`
- Modify: `ui/admin/src/auth/session.ts`
- Modify: `ui/admin/src/App.vue`
- Modify: `ui/admin/src/views/InboxMessagesView.vue`
- Modify: `ui/admin/src/views/HostAnnouncementsView.vue`
- Modify: `ui/admin/src/framework/art-design/layout/ArtAdminShell.vue`
- Modify: `ui/admin/src/framework/art-design/layout/ArtTopBar.vue`
- Test: relevant Vue component/store tests

**Interfaces:**
- Consumes: shared `createNotificationsRealtimeController` and Vue inbox unread API.
- Produces: reactive `unreadCount`, `inboxRevision`, and `announcementRevision`.

- [x] **Step 1: Write failing Vue state tests**

  Prove authentication loads the initial unread count, unread messages update the count, inbox messages increment `inboxRevision`, announcements increment `announcementRevision`, and disposal unsubscribes.

- [x] **Step 2: Run the focused test and verify RED**

  Run: `pnpm --filter @fullnet/admin test -- src/notifications/realtime.test.ts`

  Expected: FAIL because the Vue realtime state module does not exist.

- [x] **Step 3: Implement and initialize the Vue adapter**

  Initialize it once from `App.vue`, pass `unreadCount` through the shell, render the notification badge only when the count is positive, and cleanly dispose it on unmount.

- [x] **Step 4: Refresh active notification views**

  Watch `inboxRevision` in `InboxMessagesView.vue` and `announcementRevision` in `HostAnnouncementsView.vue`; call each view's existing `load()` without changing its HTTP error model.

- [x] **Step 5: Run Vue tests, typecheck, and build**

  Run: `pnpm --filter @fullnet/admin test && pnpm --filter @fullnet/admin build`

  Expected: all Vue tests pass; `vue-tsc` and Vite exit 0.

### Task 3: Layui realtime state and shell integration

**Files:**
- Create: `ui/admin-layui/js/core/realtime-notifications.js`
- Create: `ui/admin-layui/tests/realtime-notifications.test.js`
- Modify: `ui/admin-layui/js/app.js`
- Modify: `ui/admin-layui/js/core/shell-notification-panel.js`
- Modify: `ui/admin-layui/index.html`
- Test: `ui/admin-layui/tests/app.test.js`
- Test: `ui/admin-layui/tests/shell-notification-panel.test.js`

**Interfaces:**
- Consumes: shared realtime controller, Layui session, existing page controllers.
- Produces: `setUnreadCount(count)` for the shell and page-refresh callbacks for inbox/announcements.

- [x] **Step 1: Write failing Layui adapter and panel tests**

  Prove unread messages update a numeric/accessible badge, a zero count hides it, inbox and announcement codes refresh only their current routes, and disposal tears down realtime subscriptions.

- [x] **Step 2: Run the focused tests and verify RED**

  Run: `pnpm --filter @fullnet/admin-layui test -- tests/realtime-notifications.test.js tests/shell-notification-panel.test.js tests/app.test.js`

  Expected: FAIL because the adapter and dynamic unread API do not exist.

- [x] **Step 3: Implement the Layui adapter**

  Wire the shared controller into `initializeAdminApp`, keep route decisions in `app.js`, update the shell badge through safe DOM APIs, and preserve existing ProblemDetails behavior.

- [x] **Step 4: Run Layui tests and build**

  Run: `pnpm --filter @fullnet/admin-layui test && pnpm --filter @fullnet/admin-layui build`

  Expected: all Layui tests pass and Vite exits 0.

### Task 4: Governance, documentation, and integration

**Files:**
- Modify: `THIRD-PARTY-NOTICES`
- Modify: `docs/verification/realtime-signalr-foundation-2026-07-26.md`
- Modify: `docs/verification/capability-status.md`
- Modify if discovered counts change: `docs/verification/test-threshold-audit-2026-07-19.md`

**Interfaces:**
- Consumes: Tasks 1-3 verification evidence.
- Produces: truthful client completion status and dependency provenance.

- [x] **Step 1: Register the dependency**

  Record `@microsoft/signalr` 10.0.0, MIT, `https://github.com/dotnet/aspnetcore` in `THIRD-PARTY-NOTICES`.

- [x] **Step 2: Update capability and verification records**

  Mark only the browser realtime client boundary as completed; keep any real-browser, load, failure-injection or non-browser clients outside this slice explicitly pending.

- [x] **Step 3: Run repository client gates**

  Run: `pnpm test:clients && pnpm build:clients && pnpm test:e2e && pnpm test:workspace && pnpm audit:clients`

  Expected: all commands exit 0; no unreviewed high/critical advisory.

- [x] **Step 4: Run governance and repository hygiene**

  Run: `pnpm test:governance && pnpm test:skills && git diff --check && git status --short --branch`

  Expected: governance and Skill checks pass; diff check is silent; status contains only this slice.

- [ ] **Step 5: Synchronize and integrate**

  Rebase or merge the latest local `main` after Realtime and bounded logging finish, rerun affected gates, commit the closed slice, merge it into local `main`, then remove `codex/notifications-realtime-admin-client` and its worktree.

## Self-Review

- Spec coverage: authentication lifecycle, tenant reconnection, stable message validation, dual-admin parity, badge/page refresh, dependency license, audit and documentation are all mapped.
- Placeholder scan: no deferred implementation placeholder remains; non-goals are explicit.
- Type consistency: both UI adapters consume the same shared controller and stable message codes; token access stays on the identity-session transport boundary.
