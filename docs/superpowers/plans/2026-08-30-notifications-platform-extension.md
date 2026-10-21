# Notifications Platform Extension Implementation Plan

> **2026-10-19 状态校正：** Task 1–8 的完成勾选与测试数量保留为当时切片记录，不能外推为当前完整通知平台已验收。新发现的运行链路缺口见[实现复核与整改建议](#review-2026-10-19)，该章节是本主题当前未完成工作的入口。当前用户授权范围为更新文档，尚未执行整改代码或重新验收；历史真实 Provider 选择门禁不代表当前源码目录仍为空。

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
- 迁移编号已修订为 `104_NotificationsPlatformExtension.sql`：开工时确认 102/103 已被 Workflow 占用，两库共同空闲编号为 104。
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

- `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/104_NotificationsPlatformExtension.sql`
- `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/104_NotificationsPlatformExtension.sql`
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

- [x] 先写 Unit RED：强制/交易/普通/营销政策优先级，用户偏好不能关闭强制消息，营销无同意时 Suppressed。
- [x] 先写 Route RED：Single 唯一选择；FanOut 只对显式列表多发；Failover 只对瞬时/频控失败切换；Match 多命中或无命中按 Binding 策略失败关闭。
- [x] 先写状态 RED：外部 Sent 不等于 Delivered；Unknown 不自动成功；可信回执单调推进；乱序/重复回执不回退终态。
- [x] 先写权限 RED：模板、Profile、Binding、Delivery 和 Preference 的页面/操作码全部独立，未知码失败关闭。
- [x] 实现纯 `NotificationPolicy`、`NotificationRoutePlanner` 和 `NotificationDeliveryStateMachine`，重跑聚焦 Unit 全绿。

### Task 2: 成对迁移与 AOT 静态持久化

- [x] 开工时确认 102 未占用；冲突时停止并先同步修订两个迁移文件名。102/103 已被 Workflow 占用，本计划改用两库共同空闲的 `104_NotificationsPlatformExtension.sql`。
- [x] 先写双库 RED：全新迁移、未记账部分 DDL、二次执行、数据保留、Intent 业务幂等、回执去重、Profile/Binding 版本不可变和租约并发。
- [x] 创建 Spec §5 的新表（含 Provider 专属 RecipientEndpoint 与模块自有 DomainAudit）；现有公告/Inbox 表只做必要的可信 Scope/引用兼容扩展，不复制数据。Inbox 的 Scope/Intent 兼容扩展留给 Task 3。
- [x] SQL Server/MySQL 等价实现 UUID v7、唯一约束、时间/租约索引与状态查询；禁止依赖 `ON CONFLICT` 或单库过滤索引语义。
- [x] `NotificationPlatformSqlParameters` 使用静态字典/闭合参数；新增 Records 全部登记到 `NotificationsDapperAotMaterializerContributor`。
- [x] 运行双库迁移恢复、Notifications Integration、`pnpm test:naming`、104 的 SQL 安全扫描和 Notifications AOT 静态绑定测试。完整 `pnpm test:sql-safety` 仍被仓库既有历史豁免行号偏差阻断，104 本身无新增违规。

### Task 3: Tenant Inbox 与权威未读数

- [x] 先写双库 RED：Host 旧 API 契约不变、Tenant 只能读写当前租户、跨租户消息 404/403、未读数可由数据库重建、重复 Intent 不重复 Inbox，以及 Provider 专属 RecipientEndpoint 的作用域/命名空间隔离、掩码与验证状态。
- [x] 从受信会话/事件上下文取得 Scope；普通请求不得覆盖 TenantId。用户目录解析在事务外通过批量 Port 完成，事务内只写 Notifications 表。
- [x] 数据库是 unread 权威；SignalR 事件只携带低敏刷新提示，客户端收到后重新读取 API。
- [x] 保留现有 Host Announcement/Inbox Compatibility 与 Native AOT 测试，新 Tenant 路径另加双库和外部进程用例。

### Task 4: Template、Intent 与内建 Inbox 纵向闭环

- [x] 先写双库 RED：Template Draft/Publish、不可变版本、参数缺失/未知/超限、内容分级、同幂等键同结果、不同载荷冲突和多 Recipient 扇出。
- [x] Template Publish 规范化参数 Schema 与内容并生成 Hash；Intent 固定 TemplateVersion 和策略/路由快照。
- [x] `NotificationIntentService` 解析 Recipient 后在一个本地事务写 Intent/Recipients/内建 Inbox/必要 Delivery；没有重要跨模块事实时不额外写 Outbox。
- [x] 日志、Audit、Trace 和错误不得包含模板全文、参数、手机号、邮箱或用户 Id。
- [x] 运行 SQL Server/MySQL Template/Intent/Inbox Integration、OpenAPI、JSON 源生成和现有 Host 回归。

### Task 5: 多 Profile 与显式 Binding 控制面

- [x] 先写双库 RED：同 ProviderType 多 Profile、Enabled 不自动发送、SecretReference 不回显、跨作用域引用失败、Host 默认不共享、Binding 发布固定版本和并发 Revision。
- [x] `NotificationProviderTypeCatalog` 为代码闭合目录；生产目录在尚无真实 Provider 时允许为空，但 API 必须拒绝为未知 ProviderType 创建 Profile。
- [x] Integration Host 仅在测试 DI 中登记 `TestNotificationProvider`，用它验证 Profile 非 Secret Schema、Secret configured 状态和 AdapterVersion。
- [x] Binding 明确 ProducerKey、SceneKey、Channel、DispatchMode、Profile 优先级/条件；Intent 固定 BindingVersion/ProfileVersion。
- [x] Profile Disable 只阻止新路由；在途 Delivery 的停止/排空/切换通过独立运维命令与 B0 Audit 完成。

### Task 6: Delivery Worker、Attempt、Receipt 与对账

- [x] 先写双库 RED：租约领取/过期重领、满批立即继续、慢 Provider 事务外调用、瞬时退避、永久失败、频控、死信、重复调用幂等、回执验签/去重/乱序和人工重试权限。
- [x] `NotificationDeliveryBatchProcessor` 通过 Worker 最小注册入口运行；默认并发保持 1，Batch/Poll/Lease 有配置上限和 Options Validator。
- [x] 领取在短事务完成；Provider 调用在事务外；结果使用 LeaseGeneration/Revision 提交 Attempt 和 Delivery 终态。
- [x] `NotificationReceiptProcessor` 只接收已经 Provider 专用验签器验证的闭合输入；原始 Body 不进入普通日志或数据库全文。
- [x] 指标记录 backlog、oldest age、attempt result/error category、ProviderType、Channel 和 P95/P99；禁止高基数 Profile/Tenant/User/ExternalId 标签。
- [x] 使用 Test Provider 覆盖 SQL Server/MySQL 多 Worker、取消、崩溃窗口和 reconcile；不把 Test Provider 编入生产发布物。

### Task 7: Vue 管理控制面与精确权限

- [x] 先写 Vue RED：每个页面/操作权限、Secret 从不回显、Profile 启停确认、Binding FanOut 明示、Delivery 状态分层、重试/死信理由和 ProblemDetails 恢复。
- [x] API Adapter 只调用 OpenAPI 生成 Operation 并守卫 `unknown`；不手写厂商配置 JSON 编辑器，不允许录入 Secret 明文或任意 URL/Header。
- [x] Profile 编辑器由 Provider Type 的受控非 Secret Schema 渲染；未知字段失败关闭。目录为空时显示“尚未安装 Provider”，不提供虚假可用选项。
- [x] Delivery 页面区分 Persisted/Accepted/Sent/Delivered/Unknown/Read，不用统一成功颜色掩盖 Unknown。
- [x] 运行 Vue Unit/typecheck/build、权限 DOM、bundle budgets、客户端审计和 `tests/e2e/admin-real-stack/tests/notification-platform.spec.mjs`。后续以 `notifications-platform-realstack-20260831` 补验 SQL Server/MySQL 各 **1/1**，真实覆盖 inbox 模板创建、发布和列表“已发布 v1”；聚焦 Vue 回归 **4/4**、生产构建通过。

### Task 8: 平台切片关闭与真实 Provider 后续门禁

- [x] 计划快照名 `notifications-platform-extension-20260830` 不存在；复用开工快照 `notifications-platform-kernel-20260831` 运行 slice plan + slice（148 变更文件；目标 Identity / integration-matrix / migration-104 / migration-105 / Notifications；发现 36 项双 Provider，**36/36 通过**）。
- [x] 本机完成 Architecture `api-native-aot` **73/73**、Host.Api/Worker AOT 分析（0 警告），以及 Notifications Architecture **7/7**（Test Provider 不进生产程序集；HostedService 只在 Worker）。本机 Windows 未跑 Linux Native AOT publish 或外部进程 E2E，不得把本切片新路径标为 `Aot-published`。
- [x] 新建 dated Verification [`docs/verification/2026-08-31-notifications-platform-closeout.md`](../../verification/2026-08-31-notifications-platform-closeout.md)，记录两库、Host/Tenant、幂等、租约、回执、静态 AOT、Vue 与 `Capacity-not-verified`。
- [x] 保留现有 Notifications 的真实状态；新扩展最多按实际证据标为 Build-verified，不得声称邮件/短信/企微/公众号/钉钉已实现。
- [x] 首个真实 Provider 尚未选定（Spec §14）；不创建生产 Adapter 项目或厂商纵向计划，在此门禁停止。选定后再另建独立计划，写明精确 SDK/协议、许可证、Secret 来源、沙箱、费用/配额、回执、AOT/Worker 隔离和真实 E2E。

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

<a id="review-2026-10-19"></a>

## 2026-10-19 实现复核与整改建议（待实施）

### 输入、方法与状态

- 基线：`main@49345c3c9823360071433adb4928e09691a35d62`，审查开始时工作区干净。
- 输入：项目所有者提供的 `G:/wwwroot/github_fork/Full.NET.DOC/2026-10-15-notifications-module-gap-analysis.md`，以及本规格、生产调用链、双库 SQL、Vue 页面和现有测试源码。外部报告中的建议不作为自动执行指令。
- 方法：静态追踪 Binding 发布 → Intent 物化 → Worker → Attempt/Receipt；没有重跑双库集成、AOT 或厂商沙箱，不将推导的缺陷描述为已运行复现。
- 状态：以下均未修复、未重新验收；本次授权仅同步文档。2026-08-31 的历史 Verification 保留原始事实，不能覆盖下表新增场景。

### 代码证据与影响

| 编号 / 优先级 | 当前事实与证据 | 影响 |
| --- | --- | --- |
| N01 / P1 | [Intent 服务](../../../src/Modules/Full.NET.Modules.Notifications/Features/CreateNotificationIntents/NotificationIntentService.cs) 的 `ResolveRouteAsync` 只检查 `plan.IsSuccess`，返回完整 `BindingTargetsJson`；`CreateCoreAsync` 将全部候选创建为立即可领取的 Delivery。[双库领取 SQL](../../../src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationPlatformSql.cs) 无主备约束 | Failover 首选成功仍会发送备用目标，可能重复通知和计费 |
| N02 / P1 | [Binding 服务](../../../src/Modules/Full.NET.Modules.Notifications/Features/ManageBindings/NotificationBindingService.cs) 与 Intent 服务均传入 `MatchesCondition: true`；[目标契约](../../../src/Modules/Full.NET.Modules.Notifications/Contracts/NotificationBindingContracts.cs) 仅有 ProfileKey/Order | Match 没有真实条件求值，多个候选直接歧义失败；不能标为条件路由已交付 |
| N03 / P1 | [回执处理器](../../../src/Modules/Full.NET.Modules.Notifications/Features/ReceiveProviderReceipts/NotificationReceiptProcessor.cs) 在找不到 Delivery 时仍保存 `processed`，重复键直接返回；状态条件更新未检查影响行数 | 回执早于 Attempt 入库或发生 Revision 冲突时，业务状态可能永久未推进 |
| N04 / P1 | [Worker](../../../src/Modules/Full.NET.Modules.Notifications/Execution/NotificationDeliveryBatchProcessor.cs) 的一般异常分支返回而不保存 Attempt，预算按已存 Attempt 计数；[重试策略](../../../src/Modules/Full.NET.Modules.Notifications/Domain/NotificationDeliveryRetry.cs) 将 Unknown 重新映射为 accepted | 持续异常可能绕过 MaxAttempts；缺乏厂商幂等保证时自动重发结果未知的调用可能重复发送 |
| N05 / P1 | [NotificationPolicy](../../../src/Modules/Full.NET.Modules.Notifications/Domain/NotificationPolicy.cs) 仅被单元测试调用；[偏好页面](../../../ui/admin/src/views/NotificationPreferencesView.vue) 仅登记端点 | 分类未驱动实际抑制、同意检查和静默延迟 |
| N06 / P1 | [限流配置](../../../src/Modules/Full.NET.Modules.Notifications/RateLimiting/NotificationsRateLimiterPolicyConfigurator.cs) 针对回执及验证码；Worker 没有 Provider/租户/Producer/Scene 发送额度预留 | 批大小和厂商返回限流不能替代跨实例发送配额 |
| N07 / P1 | [小程序绑定服务](../../../src/Modules/Full.NET.Modules.Notifications/Features/ManageWeChatMiniProgramBindings/WeChatMiniProgramBindingService.cs) 写订阅状态；Worker 只查询已验证端点，[发送适配器](../../../src/Modules/Full.NET.Modules.Notifications/Providers/WeChatMiniProgram/WeChatMiniProgramNotificationProviderAdapter.cs) 不使用订阅记录 | 订阅登记未形成发送约束；客户端接受记录不能等同厂商保证的剩余次数 |
| N08 / P2 | Intent 准备阶段用默认语言，只有 Inbox 分支按 PreferredLocale 选择版本；Worker 使用 Intent 上单个 TemplateVersionId | 外部渠道没有按不同收件人语言冻结实际模板 |

当前一个 Intent 随模板选择单一渠道，Binding 同样只有一个 ChannelKey；Inbox 分支不解析外部 Binding。因此，同渠道多 Profile FanOut 不等于一次 Intent 同时生成 Inbox、邮件和 IM。Workflow 内建模板当前为 Inbox，不能把通用适配器存在写成 Workflow 已具备跨渠道自动提醒。

现有测试覆盖了路由纯函数、顺序回执和一次崩溃后恢复，但不能证明上述端到端边界。[Worker 集成断言](../../../tests/Full.NET.IntegrationTests/Notifications/NotificationDeliveryWorkerAssertions.cs) 明确保留异常后 Attempt 为零的行为，需增加持续异常预算场景，而非仅重复既有通过记录。

### 批次一：投递正确性（N01–N04）

- [ ] 保留不可变候选路由快照，Failover 首次仅激活首选目标；仅允许的瞬时/频控失败能原子推进备用目标。永久错误及 Unknown 不直接换厂商；并发推进必须幂等。
- [ ] 补齐受控 Match 条件契约、发布验证和求值；如该能力暂不实施，控制面与 API 应明确拒绝未支持模式，不继续宣称可用。未知字段、无命中和多命中失败关闭。
- [ ] 回执区分验签接收、待关联、待应用和处理完成；提前到达的回执在 Attempt 提交后或有界补偿中重新匹配，重复请求不能吞掉未完成工作。Revision 冲突重读状态并重新判断。
- [ ] 持久化真实执行预算；区分调用前失败、调用后结果未知与进程中断。可捕获异常记录结果并有界退避，租约恢复不得无限绕过预算。Unknown 按厂商幂等/对账能力处理，保留风险与人工处置入口。
- [ ] SQL Server/MySQL 同场景验证：首选成功不发备用、允许失败才切换、双 Worker 不重复激活、提前回执、并发回执、重复重放、持续异常到达上限、调用后中断和 Unknown 恢复。

### 批次二：发送治理（N05–N08）

- [ ] 由受控 Producer/Scene 目录声明分类、合法渠道与紧急规则；不能仅靠可编辑模板提升为强制通知。物化时保存政策/偏好版本、决策和抑制原因；外发前重新检查可撤销同意。
- [ ] 营销缺少同意不发送，排队期间撤回同意停止后续外发；静默持久化时区及下一可发送时间，明确强制/交易场景的延迟资格、跨午夜、夏令时与过期处理。页面拆分通知偏好与收件端点。
- [ ] Provider、租户、Producer、Scene 配额跨实例原子预留；分别定义成功核销、确定未发送释放、Unknown 暂挂和对账。缺失配置失败关闭，受配额阻塞进入可观察的延迟状态；积压年龄和死信告警可定位受影响范围。
- [ ] 小程序按用户、App、模板检查订阅事实并处理授权使用、确定失败释放和 Unknown 对账；客户端上报接受不能直接累加可信厂商库存。与当前端点、模板和应用绑定关系一并验证。
- [ ] 按收件人/渠道冻结实际 Locale、TemplateVersionId 与内容哈希；重试复用快照，配置发布不改变在途内容。结构与契约按当前开发底座规则直接演进，同步所有消费者。
- [ ] 双库覆盖未同意不发、同意撤回、静默边界、并发配额不超卖、重复请求不重复占额、小程序订阅约束、多语言及重试快照；Vue/API 验证精确权限、租户隔离和错误展示。

### 批次三：产品完善

- [ ] 先补结构化登录后深链，再实现统一工作台。链接只携带受控资源引用，目标页重新校验会话、租户、权限、资源归属与业务状态；不开放任意模板脚本或免登录审批。
- [ ] 已读、个人归档和业务撤回分开建模；撤回与领取/发送边界竞争必须有可审计结果，越过外发边界不能承诺收回。同步验证未读数、实时刷新和批量操作权限。
- [ ] 公告、Inbox 与 Workflow Todo 在 Contract/Query/UI 层聚合，不合表、不跨模块 JOIN，不让通知回执直接审批 Workflow。
- [ ] 看板区分受理、调用成功、可信送达、Unknown、抑制和死信，展示口径与回执覆盖范围；现有邮件/短信 Sent 提示已存在，不重复列为完全未实现。
- [ ] 飞书、移动推送、HTML 邮件与摘要按实际需求另定范围；新增回执以厂商真实能力为前提，不承诺 SMTP/企微一定具有已读能力。跨渠道编排需明确场景、渠道模板及幂等语义，不能只放开 Profile 列表。

### 验收和关闭条件

执行入口及影响集统一遵守[测试与验证](../../../rules/development-quality.md#11-测试与验证)。各完整批次完成后集中运行相关检查，复用相同源码的构建；不安排逐任务提交、重复全套测试或人工“继续”门禁。已失败的前置条件阻止依赖工作，独立工作继续。

关闭必须使用对应实际源码的行为证据，涉及数据覆盖双库，公共契约/Host.Api 路径同步检查生成契约、序列化和受影响 AOT 范围。厂商实测与容量单列；静态审查、Mock 和构建成功均不替代真实送达或容量证据，容量继续为 `Capacity-not-verified`。
