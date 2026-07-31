# Notifications Outbox Realtime Repair Implementation Plan

> **For agentic workers:** Execute this plan inline with test-driven development. Do not create a worktree or dispatch subagents for this shared dirty workspace. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Host 公告发布、站内信送达和收件箱已读状态变更增加事务 Outbox 修复推送，使数据库提交后的直接 SignalR 推送失败能够由 Worker 至少一次补发。

**Architecture:** API 写路径在业务数据和对应 Integration Event 之间使用同一 `ICommandTransaction`；提交后仍直接推送以保持低延迟。Worker 通过仅发布型 SignalR 注册接入 Redis Backplane，Notifications 后台 Handler 反序列化事件并复用同一投递服务；Handler 失败向上抛出，由现有 Outbox 重试/死信策略处理。站内信未读数在消费时重新查询当前数据库状态，避免延迟或乱序事件把旧计数重新写回客户端。

**Tech Stack:** .NET 10、Dapper、MessagePack、事务 Outbox、ASP.NET Core SignalR、StackExchange.Redis、MSTest、NSubstitute。

## Global Constraints

- 任务基线固定为 `975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`，任务快照固定为 `notifications-outbox-repair-20260730`。
- 只处理 Notifications 实时修复；不新增迁移、HTTP/JSON、权限、双管理端页面或用户可见消息。
- 消息类型固定为 `fullnet.notifications.announcement.published`、`fullnet.notifications.inbox.received` 和 `fullnet.notifications.inbox.read_state_changed`，SchemaVersion 固定为 `1`。
- 公告、发信和真实已读状态变更必须与 Outbox 原子提交；Outbox 写入失败必须回滚业务写入。
- 已经处于已读状态的单条消息和零行更新的全部已读不得产生新的 Outbox 事件。
- API 提交后的直接推送继续吞掉并记录发布异常；Worker Handler 必须传播发布、查询、反序列化与取消异常，让 Outbox 负责重试。
- 重复 Outbox 投递只触发相同客户端刷新/未读数收敛，不产生新的数据库业务写入；Handler 声明 `NaturallyIdempotent`。
- Worker 只注册 SignalR 发布能力和可选 Redis Backplane，不映射 Hub、不注册 JWT Bearer 适配器。
- 不修改 Jobs、CodeGeneration、Outbox Worker 算法、迁移、共享测试矩阵或双管理端文件。

---

### Task 1: Extract the SignalR publisher-only registration

**Files:**

- Modify: `src/BuildingBlocks/Full.NET.Realtime.SignalR/ServiceCollectionExtensions.cs`
- Modify: `tests/Full.NET.UnitTests/Realtime/RealtimeBackplaneRegistrationTests.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj`
- Modify: `src/Hosts/Full.NET.Host.Worker/Program.cs`

**Interfaces:**

- Produces: `AddFullNetRealtimePublisher(IServiceCollection, IConfiguration, string)`.
- Preserves: `AddFullNetRealtimeSignalR(...)` API registration, Hub protocol, Redis ready check and JWT access-token adaptation.
- Consumes later: Worker resolves `IRealtimePublisher` for Notifications Integration Event Handler。

- [x] **Step 1: Write the publisher-only registration RED**

Add tests that call `AddFullNetRealtimePublisher` with enabled Redis configuration and assert `IRealtimePublisher` plus `realtime-backplane` are registered while `IPostConfigureOptions<JwtBearerOptions>` is absent. Add a disabled case asserting `NullRealtimePublisher` is resolved without Redis registration.

- [x] **Step 2: Run focused RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~RealtimeBackplaneRegistrationTests" --no-restore
```

Expected: compilation fails because `AddFullNetRealtimePublisher` does not exist.

- [x] **Step 3: Implement the publisher-only registration**

Move option binding, validation, SignalR MessagePack, optional Redis Backplane, health check and `IRealtimePublisher` registration into `AddFullNetRealtimePublisher`. Make `AddFullNetRealtimeSignalR` call it and then register only `JwtBearerSignalRAccessTokenPostConfigure`. Keep option validation and service lifetimes unchanged.

- [x] **Step 4: Register publishing in Worker**

Add the Realtime SignalR project reference and call:

```csharp
builder.Services.AddFullNetRealtimePublisher(
    builder.Configuration,
    builder.Environment.EnvironmentName);
```

before module background registration. Do not map the Hub in Worker.

- [x] **Step 5: Run focused GREEN and Worker build**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~RealtimeBackplaneRegistrationTests" --no-restore
dotnet build src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj -c Release --no-restore
```

Expected: all focused tests pass and Worker builds with zero errors.

### Task 2: Define notification repair events and reusable delivery

**Files:**

- Create: `src/Modules/Full.NET.Modules.Notifications/Contracts/NotificationRealtimeIntegrationEvents.cs`
- Create: `src/Modules/Full.NET.Modules.Notifications/NotificationRealtimeDelivery.cs`
- Create: `src/Modules/Full.NET.Modules.Notifications/NotificationRealtimeIntegrationEventHandlers.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Full.NET.Modules.Notifications.csproj`
- Modify: `src/Modules/Full.NET.Modules.Notifications/NotificationsModule.cs`
- Create: `tests/Full.NET.UnitTests/Notifications/NotificationRealtimeIntegrationEventHandlerTests.cs`
- Create: `tests/Full.NET.UnitTests/Notifications/NotificationsModuleRegistrationTests.cs`

**Interfaces:**

- Produces: `AnnouncementPublishedIntegrationEvent(Guid AnnouncementId, string Title)`.
- Produces: `InboxMessageReceivedIntegrationEvent(Guid RecipientUserId, Guid MessageId, string Title)`.
- Produces: `InboxReadStateChangedIntegrationEvent(Guid RecipientUserId)`.
- Produces: `NotificationRealtimeDelivery.PublishAnnouncementAsync(...)`, `PublishInboxMessageAsync(...)` and `PublishInboxUnreadCountAsync(...)`.
- Produces: three exact-route `IIntegrationEventHandler` implementations.

- [x] **Step 1: Write Handler and module registration RED**

Assert all three handlers deserialize MessagePack and publish the existing stable realtime codes. For inbox events, assert the unread count comes from `InboxMessageSql.CountUnreadForRecipient` at handling time. Configure the publisher to throw and assert each Handler propagates the same exception. Assert `NotificationsModule.AddBackgroundServices` registers exactly the three Notifications routes with SchemaVersion `1` and `NaturallyIdempotent`.

- [x] **Step 2: Run focused RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~NotificationRealtimeIntegrationEventHandlerTests|FullyQualifiedName~NotificationsModuleRegistrationTests" --no-restore
```

Expected: compilation fails because the event, delivery and Handler types do not exist.

- [x] **Step 3: Implement events and delivery**

Add `[MessagePackObject]` records with stable numeric `[Key]` positions. `NotificationRealtimeDelivery` must query the latest unread count only for inbox delivery and must not catch exceptions.

- [x] **Step 4: Implement and register Handlers**

Each Handler must deserialize one event type with `IIntegrationEventSerializer`, delegate to `NotificationRealtimeDelivery`, expose the exact event type and SchemaVersion, and declare why repeated client refresh messages are naturally idempotent. Register the delivery plus all three scoped handlers from both `AddServices` and `AddBackgroundServices` without duplicating descriptors.

- [x] **Step 5: Run focused GREEN**

Repeat Step 2 and require zero failures.

### Task 3: Make notification writes transactionally enqueue repair events

**Files:**

- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/HostAnnouncementManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/SendHostInboxMessages/HostInboxMessageService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageMyInboxMessages/MyInboxManagementService.cs`
- Modify: `tests/Full.NET.UnitTests/Notifications/HostAnnouncementManagementServiceTests.cs`
- Create: `tests/Full.NET.UnitTests/Notifications/HostInboxMessageServiceTests.cs`
- Create: `tests/Full.NET.UnitTests/Notifications/MyInboxManagementServiceTests.cs`

**Interfaces:**

- Consumes: the three v1 event records and their exact event type constants.
- Preserves: direct post-commit realtime fast path and public Result behavior.
- Produces: one Outbox event for each real committed notification state change.

- [x] **Step 1: Write transaction-boundary RED**

For each write path, use a recording transaction and an `IOutboxWriter` callback that asserts the transaction is active. Assert the exact event payload. Add no-op tests proving already-read and zero-row mark-all operations do not enqueue. Configure Outbox to throw and assert the transaction/action fails before the direct publisher is called.

- [x] **Step 2: Run focused RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~HostAnnouncementManagementServiceTests|FullyQualifiedName~HostInboxMessageServiceTests|FullyQualifiedName~MyInboxManagementServiceTests" --no-restore
```

Expected: new assertions fail because no Notifications Outbox events are written.

- [x] **Step 3: Implement minimal atomic event writes**

Inject `IOutboxWriter` into the three services. Add the event only after the corresponding business command succeeds and before the transaction callback returns. Capture affected rows from `MarkRead` and `MarkAllRead`; do not enqueue on no-op. Replace duplicated direct publish construction with `NotificationRealtimeDelivery`, but keep each service's existing catch-and-log post-commit behavior.

- [x] **Step 4: Run focused GREEN**

Repeat Step 2 and require zero failures.

### Task 4: Prove real Outbox persistence and close the slice

**Files:**

- Modify: `tests/Full.NET.IntegrationTests/Notifications/NotificationsHostAnnouncementAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Notifications/NotificationsInboxMessageAssertions.cs`
- Modify: `docs/verification/notifications-host-announcement-2026-07-26.md`
- Modify: `docs/verification/notifications-inbox-message-2026-07-26.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: this plan

**Interfaces:**

- Consumes: existing SQL Server/MySQL Notifications API fixtures and `fn_outbox_message`.
- Produces: dual-provider proof that successful writes persist the exact v1 Outbox routes.

- [x] **Step 1: Write Integration RED**

After publish/send/mark-read/mark-all API calls, query `fn_outbox_message` by exact `MessageType` and `SchemaVersion`; assert expected counts and deserialize representative payloads. Do not run an Outbox Worker inside these API assertions.

- [x] **Step 2: Run affected-plan and focused RED**

```powershell
pnpm test:integration:affected:plan -- --snapshot notifications-outbox-repair-20260730 --phase inner
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Notifications" --no-restore
```

Expected before Task 3 implementation: the new Outbox row assertions fail. If Task 3 already makes the focused Integration assertions green, retain the earlier Unit RED as the TDD evidence and continue.

- [ ] **Step 3: Run slice verification**

```powershell
pnpm test:integration:affected -- --snapshot notifications-outbox-repair-20260730 --phase inner
pnpm test:integration:affected:plan -- --snapshot notifications-outbox-repair-20260730 --phase slice
pnpm test:integration:affected -- --snapshot notifications-outbox-repair-20260730 --phase slice
dotnet build src/Modules/Full.NET.Modules.Notifications/Full.NET.Modules.Notifications.csproj -c Release --no-restore
dotnet build src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj -c Release --no-restore
```

Expected: the selected SQL Server/MySQL Notifications set, focused Unit set and both Release builds pass.

- [x] **Step 4: Update factual documentation**

Document the direct-fast-path plus Outbox-repair semantics, three stable event routes, current-state unread count query and Worker Redis requirement. Change only the Notifications/Realtime open-item wording in the capability matrix; do not change unrelated status or test counts.

- [x] **Step 5: Run static and governance checks**

```powershell
pnpm test:naming
git diff --check
git status --short --branch
```

Expected: naming and whitespace checks pass; status contains the shared workspace changes plus this task's scoped files only.

## Self-Review

- Spec coverage: atomic write, direct low-latency path, Worker repair, current unread count, at-least-once idempotency, retry propagation, disabled realtime and Redis publishing are all mapped to a task and test.
- Placeholder scan: no deferred implementation markers or undefined interfaces remain.
- Type consistency: the three event records, message type constants, Handler routes and service writes use the same v1 contracts.
- Scope isolation: Jobs, CodeGeneration, Outbox algorithms, migrations, clients and the test matrix remain untouched.
