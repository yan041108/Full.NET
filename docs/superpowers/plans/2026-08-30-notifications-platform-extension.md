# Notifications Platform Extension Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `fullnet-module-delivery`, `fullnet-performance-hardening`, and `superpowers:test-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不重建现有公告/站内信的前提下，交付 Tenant Inbox、模板版本、逻辑通知、多个 Provider Profile、显式场景绑定、渠道任务/尝试/回执和运维控制面；真实外部 Provider 另按已选厂商建立后续纵向计划。

**Architecture:** 继续扩展单一 `Full.NET.Modules.Notifications`。业务 Producer 只创建 Intent；模块将其解析为 Recipient、内建 Inbox 和外部 Delivery。Provider Type 是代码闭合目录，Profile/Binding 是版本化配置；Worker 使用租约在事务外调用 Adapter，Attempt 与 Receipt 负责至少一次、回执和对账。

**Tech Stack:** .NET 10、Dapper、DbUp、System.Text.Json Source Generation、MemoryPack、Vue 3、SQL Server、MySQL、Host.Api/Worker Native AOT。

## Global Constraints

- 批准依据：[`2026-08-30-notifications-platform-extension-design.md`](../specs/2026-08-30-notifications-platform-extension-design.md)。
- 保留现有 Host Announcement/Inbox API、表、Realtime 和已验证能力；禁止建立第二套 Inbox。
- 模块生产 SQL 只访问 `fn_notifications_*`；Identity、Organization、Files、Workflow 和 Settings 通过最小 Port/事件协作。
- Profile 只保存 Secret Reference；HTTP API、日志、审计、指标和响应不得接收或回显明文 Secret。
- 多个 Enabled Profile 不自动 FanOut；Intent 固定绑定 BindingVersion 与 ProviderProfileVersion。
- 外部 I/O 不在数据库事务内；至少一次不是恰好一次；回执不能直接改变业务或 Workflow 状态。
- 只修改 Vue `ui/admin`；冻结 Layui 零新增。
- 迁移 `102_NotificationsPlatformExtension.sql` 只有在开工时 102 仍为空闲才可使用，否则先修订本计划中的两个精确文件名。
- 本计划只使用测试程序集内 `TestNotificationProvider` 验证平台语义；未选择真实厂商前禁止创建生产 Provider 项目或把外部渠道标为已实现。
- 开工第一步运行 `pnpm test:task:start -- notifications-platform-extension-20260830`；后续 inner/slice 必须使用同一快照。

---

## File Map

### Contracts、领域与持久化

- `src/Modules/Full.NET.Modules.Notifications/Contracts/NotificationPlatformContracts.cs`
- `src/Modules/Full.NET.Modules.Notifications/Contracts/NotificationPlatformPermissions.cs`
- `src/Modules/Full.NET.Modules.Notifications/Contracts/NotificationsErrorCodes.cs`
- `src/Modules/Full.NET.Modules.Notifications/Domain/NotificationPolicy.cs`
- `src/Modules/Full.NET.Modules.Notifications/Domain/NotificationRoutePlanner.cs`
- `src/Modules/Full.NET.Modules.Notifications/Domain/NotificationDeliveryStateMachine.cs`
- `src/Modules/Full.NET.Modules.Notifications/Auditing/NotificationDomainAuditWrite.cs`
- `src/Modules/Full.NET.Modules.Notifications/Auditing/NotificationDomainAuditWriter.cs`
- `src/Modules/Full.NET.Modules.Notifications/Auditing/NotificationDomainAuditActionKeys.cs`
- `src/Modules/Full.NET.Modules.Notifications/Providers/NotificationProviderTypeCatalog.cs`
- `src/Modules/Full.NET.Modules.Notifications/Providers/INotificationProviderAdapter.cs`
- `src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationPlatformRecords.cs`
- `src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationPlatformSql.cs`
- `src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationPlatformSqlParameters.cs`
- `src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationsDapperAotMaterializerContributor.cs`
- `src/Modules/Full.NET.Modules.Notifications/Serialization/NotificationsJsonSerializerContext.cs`
- `src/Modules/Full.NET.Modules.Notifications/NotificationsModule.cs`
- `src/Modules/Full.NET.Modules.Notifications/NotificationsAuthorizationContributor.cs`

### Features 与 Worker 入口

- `src/Modules/Full.NET.Modules.Notifications/Features/ManageTemplates/Endpoint.cs`
- `src/Modules/Full.NET.Modules.Notifications/Features/ManageTemplates/NotificationTemplateService.cs`
- `src/Modules/Full.NET.Modules.Notifications/Features/CreateNotificationIntents/NotificationIntentService.cs`
- `src/Modules/Full.NET.Modules.Notifications/Features/ManageProviderProfiles/Endpoint.cs`
- `src/Modules/Full.NET.Modules.Notifications/Features/ManageProviderProfiles/NotificationProviderProfileService.cs`
- `src/Modules/Full.NET.Modules.Notifications/Features/ManageBindings/Endpoint.cs`
- `src/Modules/Full.NET.Modules.Notifications/Features/ManageBindings/NotificationBindingService.cs`
- `src/Modules/Full.NET.Modules.Notifications/Features/ManageDeliveries/Endpoint.cs`
- `src/Modules/Full.NET.Modules.Notifications/Features/ManageDeliveries/NotificationDeliveryService.cs`
- `src/Modules/Full.NET.Modules.Notifications/Features/ManagePreferences/Endpoint.cs`
- `src/Modules/Full.NET.Modules.Notifications/Features/ManagePreferences/NotificationPreferenceService.cs`
- `src/Modules/Full.NET.Modules.Notifications/Workers/NotificationDeliveryWorkerServiceCollectionExtensions.cs`
- `src/Modules/Full.NET.Modules.Notifications/Workers/NotificationDeliveryBatchProcessor.cs`
- `src/Modules/Full.NET.Modules.Notifications/Workers/NotificationReceiptProcessor.cs`

### Migration、测试与 Vue

- `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/102_NotificationsPlatformExtension.sql`
- `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/102_NotificationsPlatformExtension.sql`
- `tests/Full.NET.UnitTests/Notifications/NotificationPolicyTests.cs`
- `tests/Full.NET.UnitTests/Notifications/NotificationRoutePlannerTests.cs`
- `tests/Full.NET.UnitTests/Notifications/NotificationDeliveryStateMachineTests.cs`
- `tests/Full.NET.UnitTests/Notifications/NotificationPlatformAuthorizationTests.cs`
- `tests/Full.NET.ArchitectureTests/NotificationsPlatformBoundaryTests.cs`
- `tests/Full.NET.IntegrationTests/Notifications/NotificationPlatformAssertions.cs`
- `tests/Full.NET.IntegrationTests/Notifications/NotificationPlatformSqlServerTests.cs`
- `tests/Full.NET.IntegrationTests/Notifications/NotificationPlatformMySqlTests.cs`
- `tests/Full.NET.IntegrationTests/Notifications/TestNotificationProvider.cs`
- `tests/Full.NET.IntegrationTests/Migrations/NotificationsPlatformMigrationRecoveryAssertions.cs`
- `ui/admin/src/api/notification-platform.ts`
- `ui/admin/src/api/notification-platform.test.ts`
- `ui/admin/src/views/NotificationTemplatesView.vue`
- `ui/admin/src/views/NotificationTemplatesView.test.ts`
- `ui/admin/src/views/NotificationProviderProfilesView.vue`
- `ui/admin/src/views/NotificationProviderProfilesView.test.ts`
- `ui/admin/src/views/NotificationBindingsView.vue`
- `ui/admin/src/views/NotificationBindingsView.test.ts`
- `ui/admin/src/views/NotificationDeliveriesView.vue`
- `ui/admin/src/views/NotificationDeliveriesView.test.ts`
- `ui/admin/src/views/NotificationPreferencesView.vue`
- `ui/admin/src/views/NotificationPreferencesView.test.ts`
- `tests/e2e/admin-real-stack/tests/notification-platform.spec.mjs`

## Stable Interfaces

```csharp
internal enum NotificationDispatchMode
{
    Single,
    FanOut,
    Failover,
    Match
}

internal enum NotificationDeliveryStatus
{
    Persisted,
    Accepted,
    Sent,
    Delivered,
    Unknown,
    Read,
    Failed,
    Suppressed,
    DeadLettered
}

internal sealed record CreateNotificationIntentCommand(
    string ProducerKey,
    string SceneKey,
    string TemplateKey,
    IReadOnlyList<NotificationRecipientInput> Recipients,
    IReadOnlyDictionary<string, JsonElement> Parameters,
    string IdempotencyKey);

internal sealed record NotificationRecipientInput(
    string RecipientTypeKey,
    string RecipientKey);

internal sealed record NotificationProviderRequest(
    Guid DeliveryId,
    string ChannelKey,
    string RecipientEndpoint,
    string Subject,
    string Body,
    string IdempotencyKey);

internal sealed record NotificationProviderResult(
    bool Accepted,
    string ResultCategory,
    string? ProviderMessageId,
    TimeSpan? RetryAfter);

internal interface INotificationProviderAdapter
{
    string ProviderTypeKey { get; }
    ValueTask<NotificationProviderResult> SendAsync(
        NotificationProviderRequest request,
        CancellationToken cancellationToken);
}
```

生产 Adapter 的实现、DTO 与 SDK 类型不得进入 Contracts 或核心数据库模型。测试 Provider 只能存在于测试程序集。

---

### Task 1: 冻结政策、路由和状态机 RED

- [ ] 先写 Unit RED：强制/交易/普通/营销政策优先级，用户偏好不能关闭强制消息，营销无同意时 Suppressed。
- [ ] 先写 Route RED：Single 唯一选择；FanOut 只对显式列表多发；Failover 只对瞬时/频控失败切换；Match 多命中或无命中按 Binding 策略失败关闭。
- [ ] 先写状态 RED：外部 Sent 不等于 Delivered；Unknown 不自动成功；可信回执单调推进；乱序/重复回执不回退终态。
- [ ] 先写权限 RED：模板、Profile、Binding、Delivery 和 Preference 的页面/操作码全部独立，未知码失败关闭。
- [ ] 实现纯 `NotificationPolicy`、`NotificationRoutePlanner` 和 `NotificationDeliveryStateMachine`，重跑聚焦 Unit 全绿。

### Task 2: 成对迁移与 AOT 静态持久化

- [ ] 开工时确认 102 未占用；冲突时停止并先同步修订两个迁移文件名。
- [ ] 先写双库 RED：全新迁移、未记账部分 DDL、二次执行、数据保留、Intent 业务幂等、回执去重、Profile/Binding 版本不可变和租约并发。
- [ ] 创建 Spec §5 的新表（含 Provider 专属 RecipientEndpoint 与模块自有 DomainAudit）；现有公告/Inbox 表只做必要的可信 Scope/引用兼容扩展，不复制数据。
- [ ] SQL Server/MySQL 等价实现 UUID v7、唯一约束、时间/租约索引与状态查询；禁止依赖 `ON CONFLICT` 或单库过滤索引语义。
- [ ] `NotificationPlatformSqlParameters` 使用静态字典/闭合参数；新增 Records 全部登记到 `NotificationsDapperAotMaterializerContributor`。
- [ ] 运行双库迁移恢复、Notifications Integration、`pnpm test:naming`、`pnpm test:sql-safety` 和 AOT analysis。

### Task 3: Tenant Inbox 与权威未读数

- [ ] 先写双库 RED：Host 旧 API 契约不变、Tenant 只能读写当前租户、跨租户消息 404/403、未读数可由数据库重建、重复 Intent 不重复 Inbox，以及 Provider 专属 RecipientEndpoint 的作用域/命名空间隔离、掩码与验证状态。
- [ ] 从受信会话/事件上下文取得 Scope；普通请求不得覆盖 TenantId。用户目录解析在事务外通过批量 Port 完成，事务内只写 Notifications 表。
- [ ] 数据库是 unread 权威；SignalR 事件只携带低敏刷新提示，客户端收到后重新读取 API。
- [ ] 保留现有 Host Announcement/Inbox Compatibility 与 Native AOT 测试，新 Tenant 路径另加双库和外部进程用例。

### Task 4: Template、Intent 与内建 Inbox 纵向闭环

- [ ] 先写双库 RED：Template Draft/Publish、不可变版本、参数缺失/未知/超限、内容分级、同幂等键同结果、不同载荷冲突和多 Recipient 扇出。
- [ ] Template Publish 规范化参数 Schema 与内容并生成 Hash；Intent 固定 TemplateVersion 和策略/路由快照。
- [ ] `NotificationIntentService` 解析 Recipient 后在一个本地事务写 Intent/Recipients/内建 Inbox/必要 Delivery；没有重要跨模块事实时不额外写 Outbox。
- [ ] 日志、Audit、Trace 和错误不得包含模板全文、参数、手机号、邮箱或用户 Id。
- [ ] 运行 SQL Server/MySQL Template/Intent/Inbox Integration、OpenAPI、JSON 源生成和现有 Host 回归。

### Task 5: 多 Profile 与显式 Binding 控制面

- [ ] 先写双库 RED：同 ProviderType 多 Profile、Enabled 不自动发送、SecretReference 不回显、跨作用域引用失败、Host 默认不共享、Binding 发布固定版本和并发 Revision。
- [ ] `NotificationProviderTypeCatalog` 为代码闭合目录；生产目录在尚无真实 Provider 时允许为空，但 API 必须拒绝为未知 ProviderType 创建 Profile。
- [ ] Integration Host 仅在测试 DI 中登记 `TestNotificationProvider`，用它验证 Profile 非 Secret Schema、Secret configured 状态和 AdapterVersion。
- [ ] Binding 明确 ProducerKey、SceneKey、Channel、DispatchMode、Profile 优先级/条件；Intent 固定 BindingVersion/ProfileVersion。
- [ ] Profile Disable 只阻止新路由；在途 Delivery 的停止/排空/切换通过独立运维命令与 B0 Audit 完成。

### Task 6: Delivery Worker、Attempt、Receipt 与对账

- [ ] 先写双库 RED：租约领取/过期重领、满批立即继续、慢 Provider 事务外调用、瞬时退避、永久失败、频控、死信、重复调用幂等、回执验签/去重/乱序和人工重试权限。
- [ ] `NotificationDeliveryBatchProcessor` 通过 Worker 最小注册入口运行；默认并发保持 1，Batch/Poll/Lease 有配置上限和 Options Validator。
- [ ] 领取在短事务完成；Provider 调用在事务外；结果使用 LeaseGeneration/Revision 提交 Attempt 和 Delivery 终态。
- [ ] `NotificationReceiptProcessor` 只接收已经 Provider 专用验签器验证的闭合输入；原始 Body 不进入普通日志或数据库全文。
- [ ] 指标记录 backlog、oldest age、attempt result/error category、ProviderType、Channel 和 P95/P99；禁止高基数 Profile/Tenant/User/ExternalId 标签。
- [ ] 使用 Test Provider 覆盖 SQL Server/MySQL 多 Worker、取消、崩溃窗口和 reconcile；不把 Test Provider 编入生产发布物。

### Task 7: Vue 管理控制面与精确权限

- [ ] 先写 Vue RED：每个页面/操作权限、Secret 从不回显、Profile 启停确认、Binding FanOut 明示、Delivery 状态分层、重试/死信理由和 ProblemDetails 恢复。
- [ ] API Adapter 只调用 OpenAPI 生成 Operation 并守卫 `unknown`；不手写厂商配置 JSON 编辑器，不允许录入 Secret 明文或任意 URL/Header。
- [ ] Profile 编辑器由 Provider Type 的受控非 Secret Schema 渲染；未知字段失败关闭。目录为空时显示“尚未安装 Provider”，不提供虚假可用选项。
- [ ] Delivery 页面区分 Persisted/Accepted/Sent/Delivered/Unknown/Read，不用统一成功颜色掩盖 Unknown。
- [ ] 运行 Vue Unit/typecheck/build、权限 DOM、bundle budgets、客户端审计和 `tests/e2e/admin-real-stack/tests/notification-platform.spec.mjs`。

### Task 8: 平台切片关闭与真实 Provider 后续门禁

- [ ] 使用任务快照运行 `pnpm test:integration:affected:plan -- --snapshot notifications-platform-extension-20260830 --phase slice`，审查选择器后运行 `pnpm test:slice -- --snapshot notifications-platform-extension-20260830`。
- [ ] Linux 原生 Host.Api/Worker 外部进程分别验证新增 HTTP/JSON/Dapper 与 Test Provider Worker 静态闭包；测试 Provider 不进入产品程序集。
- [ ] 新建 dated Verification，记录两库、Host/Tenant、幂等、租约、回执、AOT、Vue、性能基线和 `Capacity-not-verified`。
- [ ] 保留现有 Notifications 的真实状态；新扩展最多按实际证据标为 Build-verified，不得声称邮件/短信/企微/公众号/钉钉已实现。
- [ ] 选定首个真实 Provider 后另建一个厂商纵向计划，写明精确 SDK/协议、许可证、Secret 来源、沙箱、费用/配额、回执、AOT/Worker 隔离和真实 E2E；未选定前停止在此门禁。

---

## Stop Conditions

- 需要业务模块直接选择 Secret/厂商 SDK、Profile Enabled 自动多发、事务内外部调用、跨模块 SQL/事务、明文 Secret、任意 URL/Header/Body 或回执直接改变业务状态时停止。
- 首个生产 Provider 未明确厂商/协议、凭据、沙箱、费用、限速、回执与许可证时，不创建生产 Adapter 项目。
- SQL Server/MySQL 任一库无法验证迁移、租约、幂等或乱序回执时，不关闭平台切片。
- Native AOT 无法闭合的 SDK 必须隔离到经批准的非 AOT Worker Adapter；不得让 Host.Api 回退反射或动态加载。

## Completion Evidence

- Unit、Architecture、双库 Migration/Integration、Host/Tenant、OpenAPI/JSON、Host.Api/Worker AOT、Vue 与受影响 slice 均有新鲜非零输出。
- Test Provider 只存在测试程序集；生产 Provider Type Catalog 可为空且 UI 如实显示未安装。
- 真实外部渠道保持 Planned，直到独立厂商计划和真实沙箱证据完成。
