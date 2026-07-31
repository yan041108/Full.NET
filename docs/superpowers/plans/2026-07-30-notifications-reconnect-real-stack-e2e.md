# Notifications Reconnect Real-Stack E2E Implementation Plan

> **For agentic workers:** Execute this plan inline with test-driven development. Do not create a worktree or dispatch subagents for this shared dirty workspace. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Vue 与 Layui 管理端在 SignalR 短时重连及自动重连耗尽后都能恢复连接，并在恢复时补拉当前未读数、刷新收件箱；同时用包含真实 Worker、Outbox 和 Redis Backplane 的浏览器 E2E 固定该行为。

**Architecture:** 共享 `@fullnet/client-contracts` 控制 SignalR 生命周期：`onreconnected` 通知上层执行状态修复，`onclose` 在自动重连耗尽后重新创建连接。两个管理端只负责各自的未读数补拉和页面刷新。真实栈保持 API/Worker 运行角色分离，由同一 bootstrap 使用同一数据库与 Redis 启动 Worker，浏览器测试通过独立在线观察端证明离线期间 Outbox 已被消费，再断言恢复端补拉到数据库事实。

**Tech Stack:** TypeScript 7、Vue 3、Layui、SignalR 10、Vitest、Node Test Runner、Playwright 1.61、.NET 10 Worker、事务 Outbox、Redis Backplane。

## Global Constraints

- 任务基线固定为 `975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`，任务快照固定为 `notifications-browser-reconnect-e2e-20260730`。
- 只修改 Notifications 实时客户端、双管理端对应适配器、真实栈 E2E 编排与 Notifications/Realtime 验证记录。
- 不修改 Jobs、CodeGeneration、迁移、`eng/testing/test-matrix.json`、`docs/roadmap/capability-status.md` 或 `docs/roadmap/adminnet-feature-parity.md`。
- SignalR 故障继续降级，不得阻断 HTTP 会话；重连后的补拉失败也不得传播未处理异常。
- Worker 必须作为独立进程运行，不得把 Outbox Processor 塞入 API Host。
- 双端测试使用同一稳定行为，不复制 SignalR 状态机。
- 共享脏工作区不暂存、不提交；每个任务以聚焦验证和 diff 检查作为检查点。

---

### Task 1: Restore client state after SignalR reconnect

**Files:**

- Modify: `packages/client-contracts/src/notifications-realtime.ts`
- Modify: `packages/client-contracts/tests/notifications-realtime.test.ts`
- Modify: `ui/admin/src/notifications/realtime.ts`
- Modify: `ui/admin/src/notifications/realtime.test.ts`
- Modify: `ui/admin-layui/js/core/realtime-notifications.js`
- Modify: `ui/admin-layui/tests/realtime-notifications.test.js`

**Interfaces:**

- Extends: `NotificationsHubConnection.onreconnected(handler)` and `NotificationsHubConnection.onclose(handler)`.
- Extends: optional `NotificationsRealtimeOptions.onReconnected(): void | Promise<void>`.
- Preserves: `NotificationsRealtimeController.whenSettled()` and `dispose()`.
- Produces: Vue/Layui reconnect repair that reloads unread count and refreshes the active inbox view.

- [x] **Step 1: Write shared-controller RED**

Add fake connection hooks and assertions equivalent to:

```ts
connection.reconnect();
expect(onReconnected).toHaveBeenCalledOnce();

connection.close();
await vi.advanceTimersByTimeAsync(0);
await controller.whenSettled();
expect(connectionFactory).toHaveBeenCalledTimes(2);
```

The explicit `stop()` path must not schedule a replacement connection.

- [x] **Step 2: Run shared-controller RED**

```powershell
pnpm --filter @fullnet/client-contracts test -- notifications-realtime.test.ts
```

Expected: FAIL because the fake/production connection contract has no reconnect or close hooks and the controller has no `onReconnected` option.

- [x] **Step 3: Implement the minimal shared lifecycle**

Register `connection.onreconnected` before `start()` and invoke the optional callback through a caught promise:

```ts
connection.onreconnected(() => {
  void Promise.resolve(options.onReconnected?.()).catch(() => undefined);
});
```

Register `connection.onclose` so only the still-active connection clears `activeConnection`, `activeHandler` and `activeKey`, then calls `scheduleInitialRetry(targetKey)`. Because `stopActiveConnection()` clears the active reference before `stop()`, logout, tenant switch and disposal must remain no-ops for the close callback.

- [x] **Step 4: Write Vue/Layui adapter RED**

Capture `options.onReconnected` from each adapter’s injected realtime factory. After an authenticated initial load, update the stubbed unread result, invoke the callback and assert:

```ts
expect(loadUnreadCount).toHaveBeenCalledTimes(2);
expect(state.unreadCount.value).toBe(6);
expect(state.inboxRevision.value).toBe(1);
```

For Layui, assert the second request updates `onUnreadCount(6)` and calls `onInboxChanged()` once. Add a rejected repair query case that resolves without an unhandled rejection.

- [x] **Step 5: Run adapter RED**

```powershell
pnpm --filter @fullnet/admin test -- src/notifications/realtime.test.ts
pnpm --filter @fullnet/admin-layui test -- tests/realtime-notifications.test.js
```

Expected: FAIL because neither adapter supplies `onReconnected`.

- [x] **Step 6: Implement and verify adapter repair**

Extract one queued unread-count loader per adapter with a `refreshInbox` flag. Session authentication calls it with `false`; reconnect calls it with `true`. Apply results only when the captured session generation is current, then advance the inbox revision/callback only after a successful current-generation response.

Run:

```powershell
pnpm --filter @fullnet/client-contracts test -- notifications-realtime.test.ts
pnpm --filter @fullnet/admin test -- src/notifications/realtime.test.ts
pnpm --filter @fullnet/admin-layui test -- tests/realtime-notifications.test.js
pnpm --filter @fullnet/client-contracts build
pnpm --filter @fullnet/admin typecheck
```

Expected: all focused tests and type checks pass.

### Task 2: Add the real Worker role to the E2E stack

**Files:**

- Modify: `tests/e2e/admin-real-stack/scripts/spec-contracts.test.mjs`
- Modify: `tests/e2e/admin-real-stack/scripts/bootstrap-stack.mjs`
- Modify: `tests/e2e/admin-real-stack/global-setup.mjs`

**Interfaces:**

- Produces: `.stack-state.json.workerPid`.
- Preserves: existing SQL Server/MySQL provider selection, Redis container, API URL and teardown behavior.
- Provides: independent Worker consumption of the same database Outbox and publication through the same Redis Backplane.

- [x] **Step 1: Write bootstrap contract RED**

Extend `spec-contracts.test.mjs` to assert that bootstrap spawns `Full.NET.Host.Worker.csproj`, stores `workerPid`, and teardown kills `workerProcess`; assert global setup checks `isProcessAlive(existingState.workerPid)` before reusing a kept stack.

- [x] **Step 2: Run bootstrap contract RED**

```powershell
pnpm --dir tests/e2e/admin-real-stack test:provisioner
```

Expected: FAIL because the real stack currently starts only the API.

- [x] **Step 3: Start and stop Worker with the stack**

After API readiness and viewer provisioning, spawn:

```js
const workerProcess = spawn(
  'dotnet',
  ['run', '--project', 'src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj'],
  { cwd: repoRoot, env: sharedEnv, stdio: 'pipe' }
);
```

Set `OutboxWorker__PollMilliseconds` to `100` in the E2E environment, store the process in `activeStack`, persist `workerPid`, and kill it before stopping containers. In global setup, reuse state only when the API responds and `workerPid` is alive.

- [x] **Step 4: Run bootstrap contract GREEN**

Repeat Step 2 and require all provisioner contract tests to pass.

### Task 3: Prove offline delivery repair in both browsers

**Files:**

- Create: `tests/e2e/admin-real-stack/tests/notifications-reconnect.spec.mjs`
- Modify: `tests/e2e/admin-real-stack/tests/support/real-stack-auth.mjs`
- Modify: `docs/verification/notifications-inbox-message-2026-07-26.md`
- Modify: `docs/verification/realtime-signalr-foundation-2026-07-26.md`
- Modify: this plan

**Interfaces:**

- Produces helper: `markAllInboxMessagesReadViaApi(request, clientKind)`.
- Produces helper: `sendHostInboxMessageViaApi(request, clientKind, recipientUserId, options)`.
- Consumes: `findSeedAdminUserViaApi`, real admin login, separate Playwright browser contexts, real Worker Outbox processing and Redis Backplane.

- [x] **Step 1: Write the dual-project browser E2E**

For each Playwright project:

1. Mark all existing admin inbox messages read.
2. Open a recovery page and an independent observer context; log both in as Host admin and wait for their Notifications Hub WebSockets.
3. Put only the recovery context offline.
4. Send a uniquely titled inbox message through the real API.
5. Assert the online observer badge becomes `1`, proving Worker consumed the Outbox while the recovery page was offline.
6. Restore the recovery context and assert its badge becomes `1` without page reload or a second notification, proving `onreconnected` repaired missed state.

- [x] **Step 2: Run browser RED/GREEN after Jobs releases Docker**

```powershell
pnpm --dir tests/e2e/admin-real-stack exec playwright test tests/notifications-reconnect.spec.mjs
```

Expected before Tasks 1–2: FAIL because no Worker consumes the Outbox and no reconnect repair exists. Expected after Tasks 1–2: 2 passed, one per Vue/Layui project.

- [x] **Step 3: Update factual verification records**

Record the independent API/Worker topology, Redis Backplane path, offline observer proof, reconnect unread repair and exact commands. Do not change shared test counts or roadmap status.

- [x] **Step 4: Run affected and static verification**

```powershell
pnpm test:integration:affected:plan -- --snapshot notifications-browser-reconnect-e2e-20260730 --phase inner
pnpm test:naming
git diff --check
git status --short --branch
```

Expected: affected selector plans only the actual impact set; naming and whitespace checks pass; status preserves other windows’ changes.

## Self-Review

- Spec coverage: shared reconnect lifecycle, long-outage fallback, Vue/Layui state repair, independent Worker topology, real Outbox/Redis delivery and offline browser recovery each map to a test.
- Placeholder scan: no deferred implementation marker or undefined interface remains.
- Type consistency: `onReconnected` is optional at the public controller boundary and maps to both adapters with the same semantics.
- Scope isolation: Jobs, CodeGeneration, migrations, shared matrix and roadmap files remain untouched.
