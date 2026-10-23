# Notifications Platform Extension Implementation Plan

## 2026-10-23 活动清单（本次实施授权）

### 续办：无厂商凭据的剩余验收

用户于 2026-10-23 明确授权补齐当前 Worker Native AOT、新增治理 API 的双库原生运行与通知告警触发/恢复验证，并修正本计划过期状态。沿用 main@5093b6f4、现有任务快照和脏工作区；不重新检测 CPU。最新指令“提交所有代码，并同步”已授权提交全部工作区代码并同步远端，取代此前禁止提交或推送的限制；下文旧限制和失败记录属于历史。复用此前授权的稳定核心执行条件并在结束时恢复。

- [x] 当前 Worker 经默认 Linux SDK/ILC 发布，双库原生进程验证通知消费、治理执行及正常停止；最终通知治理、Workflow 投影及 PDF 恢复六项 6/6、零跳过，不能外推完整 Worker 全绿。
- [ ] 新增治理 API 在当前 Host.Api Linux 原生产物上执行双库 HTTP/JSON、权限、偏好/额度与归档撤回行为验收。
- [x] 使用真实通知指标和当前 Prometheus 规则验证积压、死信、Unknown 告警触发与恢复，保留失败证据。
- [x] 对齐本计划旧活动清单和能力边界；真实厂商与无法访问的外部原报告单列，容量保持 Capacity-not-verified。

- 当前用户明确授权连续完成三个整改批次，历史“仅更新文档”是历史任务范围，不限制本次实施。
- 实际工作目录经用户确认改为 `E:/wwwroot/github/Full.NET`；分支 `main`，基线 `5093b6f4e6fc65071fedbeae0e23ba80a8ba28d6`，保留现有未提交实现。任务快照 `notifications-20261023` 由 `node scripts/testing/create-task-snapshot.mjs` 创建（pnpm 包装入口因本机版本自动安装失败，未变更依赖）。
- [x] 复核批次一实现并取得本次双库行为证据；复用已经正确的 Failover、Match、回执与预算实现。
- [x] 完成批次二：一次性小程序授权预留/核销/释放/Unknown、最终许可、过期、快照及治理控制面验收。
- [x] 完成批次三：结构化深链、个人/批量归档、场景筛选、可审计撤回及发送竞争、统一工作台和投递状态口径。
- [x] 集中执行相关单测、架构、契约、Vue、双库及迁移恢复，按实际终态登记；更新能力表与百科，单列厂商及容量证据边界。
- [x] 当前验收：完整 Linux SDK/ILC 发布链路、该产物 Notifications 双库原生运行及四项 JS 预算均通过；前次两阶段路径只作历史对照，全部失败保留，本机后续仍需要稳定 CPU 执行条件。
- [x] 续办：首页、SignalR 延迟包、Chart、VForm 四项预算通过；生产构建匿名页实际加载补验通过。
- [x] 历史对照：相同输入由 Windows ILC 生成 Linux ELF，再在 Linux 链接及原生双库 2/2；这项历史结果不替代下一条默认 Linux 链路验收。
- [x] 当前续办：默认 Linux ILC 在授权临时亲和性约束下重新生成对象、完成链接和发布，退出码 0；该新产物原生双库 2/2、零跳过，最终进程及 92 个线程均已恢复原始亲和性。未使用两阶段对象替代此门禁，未重新检测 CPU，未提交或推送。
- 本轮执行条件经用户明确批准：临时限制共享 WSL/Docker 虚拟机到已知稳定核心，验证后于任务结束恢复。只读核验进程及 92 个线程的亲和性均为 CPU 31 掩码，原始掩码、进程世代与线程读回记录在系统临时目录 `vm-affinity-original.json`、`vm-thread-affinity-active.json`。最小 Linux ILC 编译、链接和原生启动通过，只证明基本工具链可运行。完整 `dotnet publish` 使用 Linux SDK/ILC 10.0.402/10.0.13、独立 `/out/default-linux-native/` 与 `/out/default-linux-publish`，未传 `--no-build`、未调用 Windows ILC，也不复用旧 `.o`。资源池未准入 16 GiB 时未绕过门禁，改用真实 10 GiB 预留/容器上限；排队期间恢复共享 VM 原亲和性，获得名额后才重新限制，完成/失败均在 finally 恢复。
- 2026-10-23 前轮历史授权：用户授权继续区分编译器、输入与本机环境并直接修复。保持同一源码和 ILC 10.0.13/Linux x64 目标，以 Windows 宿主 ILC、任务进程 CPU 31 做平台对照；若生成 ELF 对象，仍须回 Linux 完成链接、原生启动及双库运行。不得以 Windows 目标程序替代 Linux 验收，保留此前全部失败。
- 前轮硬件历史证据（本轮未重新检测）：只读 Windows System / Microsoft-Windows-WHEA-Logger 日志，2026-10-22 多次事件 19，处理器核心 Corrected Machine Check / Internal parity error，涉及 APIC 33、41；原始节选保存系统临时目录 `whea-processor-errors.json`。这证实本机存在硬件异常，但尚未建立每次 ILC 崩溃的直接因果；没有修改 BIOS、全局运行时或虚拟机 CPU 设置。Windows ILC 10.0.13 与同一 Linux 目标输入的对照已成功，随后 Linux 链接和双库原生运行通过；此为隔离实验，官方不支持标准跨 OS 发布，不能据此声称默认 Linux 发布链路已修复（[官方说明](https://learn.microsoft.com/dotnet/core/deploying/native-aot/cross-compile)）。
- 2026-10-23 续办授权：用户明确要求修复 Native AOT 发布失败和首页 JS 包体超标、完成剩余验收；沿用上述基线与快照，不提交或推送。先定位发布资源/编译器故障与首屏实际依赖，再以固定预算及真实原生进程补验。前次 gzip 395,887 也超过 376,928 × 1.05 = 395,774.4；下方旧“gzip 仍在范围内”判断有误，本次一并校正。
- 指定 G 盘外部报告在当前设备不可访问；本地搜索亦未找到原件，不能伪称已读或覆盖原报告。命名的三项过程 Skill 未安装，实施采用根因定位、风险驱动测试与新鲜证据核对，不引入新的流程授权。

> **历史与当前状态：** Task 1–8 的完成勾选与测试数量保留为当时切片记录，不能外推为当前完整通知平台已验收。[2026-10-19 复核](#review-2026-10-19)记录原始缺陷与三个整改批次；当前进度统一以文首活动清单及文末本轮证据为准。历史真实 Provider 选择门禁不代表当前源码目录仍为空。

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
- 平台语义使用测试程序集内 `TestNotificationProvider` 验证；当前已有 SMTP、阿里云短信、钉钉、企微和小程序适配器，本轮只完善已授权范围。适配器实现、模拟测试、原生发布与真实厂商认证分别记录，不能把前者外推为真实送达。
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

## 2026-10-19 实现复核与整改记录

### 输入、方法与状态

- 历史审查基线：`main@49345c3c9823360071433adb4928e09691a35d62`，审查开始时工作区干净。
- 本次实施基线：`main@5093b6f4e6fc65071fedbeae0e23ba80a8ba28d6`，当前用户于 2026-10-21 明确授权在 `E:/wwwroot/github/Full.NET` 连续完成 Notifications 三个整改批次；旧文档中“仅授权更新文档”的描述只记录历史任务，不限制本次实施。
- 输入：项目所有者提供的外部差距报告、本规格、生产调用链、双库 SQL、Vue 页面和现有测试源码。外部报告是审查资料，不作为独立指令来源；本轮以当前用户授权、项目规则和已批准规格为执行边界。
- 方法：先基于当前源码逐项复核 N01–N08，复用已经正确实现并具备证据的能力；随后按投递正确性、发送治理、产品闭环三个功能批次同步完成后端、数据库、API、Vue、权限、测试和文档，并在批次末集中验收。
- 状态：以下清单从“待实施”转为本次活动清单；每项关闭必须以当前源码的新鲜行为证据为准，历史 Verification 只保留原始事实。

### 2026-10-21 本次实施活动清单

- [x] 批次一：修复 Failover 候选物化、受控 Match 条件、回执待关联/待应用闭环以及异常、超时、崩溃和 Unknown 的有界重试预算。（2026-10-21 双库通过，见文末本轮证据）
- [x] 批次二：将 Producer/Scene 政策、用户偏好、静默时段、营销同意、跨实例配额、小程序订阅约束和按收件人语言/模板快照接入真实发送链路。（2026-10-23 双库与 Vue 行为证据见文末）
- [x] 批次三：补齐站内信登录后深链、个人归档、业务撤回、统一工作台聚合和投递控制面口径，保持通知偏好与收件端点管理分离。（2026-10-23 双库与 Vue 行为证据见文末）
- [x] 已有验收：Unit、Architecture、双库 Integration、OpenAPI/序列化、Vue 和 Host.Api 原生发布/基础运行已有本轮证据；当前 Worker、新增治理 API 原生覆盖及通知告警闭环按文首续办清单单独关闭。真实厂商、容量与外部报告仍单列。

### 2026-10-22 批次二建设与验收状态（历史进度，已由 2026-10-23 记录替代）

- 发送治理仍在开发中，不关闭批次二：已实现独立尝试预算、治理延迟退款、人工重试不回退租约世代、旧世代预留转待对账以及按原消息号收敛配额；偏好与端点 Vue 拆分、过期、授权消费和产品闭环仍待完成。
- 双库验收发现 SQL Server 282 首次执行报 `Invalid column name 'AttemptBudgetUsed'`；已将建列、回填、约束收紧按 `GO` 分批。恢复断言仅选择 282，覆盖未记账回填、约束收紧后重跑不覆盖真实预算；最终双库结果尚未取得。
- 补充旧台账已处于 `pending_reconciliation` 后再次人工重投的保护：查询持续纳入旧世代未决台账，保持占额并禁止外发。新增断言复用现有双库治理用例，不以 SQL 文本检查替代最终数据库行为验证。
- 本地命名、数据库中文注释、UUID 存储和 SQL 安全检查实际通过：`node --test tests/naming/*.test.mjs tests/database/uuid-storage-contract.test.mjs tests/sql/*.test.mjs`，40 项通过、0 失败、0 跳过。
- 当前 .NET 新鲜构建受运行时异常影响：非共享编译出现 `csc.exe -1073741819`，单 MSBuild 节点又出现 CLR `0x80131506`。保留分析器与源生成器，未修改系统配置、未绕过构建新鲜性门禁；诊断仅使用进程环境，不将构建失败或旧结果报告为通过。
- 当前分支 `main`，基线 `5093b6f4e6fc65071fedbeae0e23ba80a8ba28d6`；保留既有脏工作区，未提交或推送。真实厂商送达及容量仍未验证。
- 下一任务已转向 Vue 接线，快照 `notifications-vue-20261022`：完成独立渠道发送偏好编辑器，覆盖渠道关闭、营销同意、带时区静默时段、只读无保存入口、保存中防重复和损坏规则禁止覆盖；尚未接入页面/API，不标记偏好产品能力已完成。
- 原收件端点页保留已有验证流程，登记与删除增加函数级权限/重复提交保护，操作失败改用统一 `showProblem`，保存失败保留输入；本轮不改变端点与偏好的服务端权限模型。
- Vue 聚焦回归实际 12/12 通过，双语字典 9/9 通过，`vue-tsc --noEmit -p tsconfig.json` 与 Vite 生产打包通过。新编辑器尚未进入路由产物，组件编译由 Vue 类型检查和挂载测试验证；真实栈页面/E2E 未执行。
- 新 API 接线受真实 OpenAPI 生成阻塞：限定单处理器的新鲜构建仍在 MSBuild 初始化阶段出现 CLR `0x80131506`。本轮试加 manifest 条目已撤回，未伪造 OpenAPI、手写 HTTP 路径或修改生成文件，场景目录与配额页面仍待开发。
- 现有客户端 OpenAPI 离线检查失败于快照 CRLF 与规范化 LF 差异（第 1 行）；本轮未改该快照，不把检查失败报告为通过。前端依赖按冻结锁文件恢复，仅 Vue 范围安装成功，锁文件未变化。

### 历史审查代码证据与影响

下表描述 2026-10-19 基线的缺陷，并非当前源码现状。N01–N08 的整改及本次行为证据见后续三个批次与 2026-10-23 验收记录；保留原证据便于追溯。

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

现有测试覆盖了路由纯函数、顺序回执和一次崩溃后恢复。2026-10-21 批次一已补入持续异常预算（到 MaxAttempts 后 `dead_lettered` 且不被自动重领）、双 Worker 并发 Failover、乱序回执可审计 `ignored`、并发重复回执只推进一次等端到端场景，见文末本轮证据。

### 批次一：投递正确性（N01–N04）

- [x] 保留不可变候选路由快照，Failover 首次仅激活首选目标；仅允许的瞬时/频控失败能原子推进备用目标。永久错误及 Unknown 不直接换厂商；并发推进必须幂等。
- [x] 补齐受控 Match 条件契约、发布验证和求值；如该能力暂不实施，控制面与 API 应明确拒绝未支持模式，不继续宣称可用。未知字段、无命中和多命中失败关闭。
- [x] 回执区分验签接收、待关联、待应用和处理完成；提前到达的回执在 Attempt 提交后或有界补偿中重新匹配，重复请求不能吞掉未完成工作。Revision 冲突重读状态并重新判断。
- [x] 持久化真实执行预算；区分调用前失败、调用后结果未知与进程中断。可捕获异常记录结果并有界退避，租约恢复不得无限绕过预算。Unknown 按厂商幂等/对账能力处理，保留风险与人工处置入口。
- [x] SQL Server/MySQL 同场景验证：首选成功不发备用、允许失败才切换、双 Worker 不重复激活、提前回执、并发回执、重复重放、持续异常到达上限、调用后中断和 Unknown 恢复。

### 批次二：发送治理（N05–N08）

- [x] 由受控 Producer/Scene 目录声明分类、合法渠道与紧急规则；不能仅靠可编辑模板提升为强制通知。物化时保存政策/偏好版本、决策和抑制原因；外发前重新检查可撤销同意。
- [x] 营销缺少同意不发送，排队期间撤回同意停止后续外发；静默持久化时区及下一可发送时间，明确强制/交易场景的延迟资格、跨午夜、夏令时与过期处理。页面拆分通知偏好与收件端点。
- [x] Provider、租户、Producer、Scene 配额跨实例原子预留；分别定义成功核销、确定未发送释放、Unknown 暂挂和对账。缺失配置失败关闭，受配额阻塞进入可观察的延迟状态；积压年龄和死信告警可定位受影响范围。
- [x] 小程序按用户、App、模板检查订阅事实并处理授权使用、确定失败释放和 Unknown 对账；客户端上报接受不能直接累加可信厂商库存。与当前端点、模板和应用绑定关系一并验证。
- [x] 按收件人/渠道冻结实际 Locale、TemplateVersionId 与内容哈希；重试复用快照，配置发布不改变在途内容。结构与契约按当前开发底座规则直接演进，同步所有消费者。
- [x] 双库覆盖未同意不发、同意撤回、静默边界、并发配额不超卖、重复请求不重复占额、小程序订阅约束、多语言及重试快照；Vue/API 验证精确权限、租户隔离和错误展示。

### 批次三：产品完善

- [x] 先补结构化登录后深链，再实现统一工作台。链接只携带受控资源引用，目标页重新校验会话、租户、权限、资源归属与业务状态；不开放任意模板脚本或免登录审批。
- [x] 已读、个人归档和业务撤回分开建模；撤回与领取/发送边界竞争必须有可审计结果，越过外发边界不能承诺收回。同步验证未读数、实时刷新和批量操作权限。
- [x] 公告、Inbox 与 Workflow Todo 在 Contract/Query/UI 层聚合，不合表、不跨模块 JOIN，不让通知回执直接审批 Workflow。
- [x] 看板区分受理、调用成功、可信送达、Unknown、抑制和死信，展示口径与回执覆盖范围；现有邮件/短信 Sent 提示已存在，不重复列为完全未实现。
- [x] 飞书、移动推送、HTML 邮件与摘要按实际需求另定范围；新增回执以厂商真实能力为前提，不承诺 SMTP/企微一定具有已读能力。跨渠道编排需明确场景、渠道模板及幂等语义，不能只放开 Profile 列表。

### 验收和关闭条件

执行入口及影响集统一遵守[测试与验证](../../../rules/development-quality.md#11-测试与验证)。各完整批次完成后集中运行相关检查，复用相同源码的构建；不安排逐任务提交、重复全套测试或人工“继续”门禁。已失败的前置条件阻止依赖工作，独立工作继续。

关闭必须使用对应实际源码的行为证据，涉及数据覆盖双库，公共契约/Host.Api 路径同步检查生成契约、序列化和受影响 AOT 范围。厂商实测与容量单列；静态审查、Mock 和构建成功均不替代真实送达或容量证据，容量继续为 `Capacity-not-verified`。

### 2026-10-21 批次一证据记录

修复的实质缺陷：

- [NotificationPlatformSql.cs](../../../src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationPlatformSql.cs) 的 `ClaimDeliveriesSqlServer` `OUTPUT` 子句缺少 `NotificationDeliveryRecord` 新增的治理快照列（`ContentSnapshotJson`、`PreferenceVersion`、`PolicyDecisionKey`、`SuppressionReasonCode`），导致 SQL Server 上 Dapper 物化失败、整个纵向切片无法运行；MySQL 走 `SelectDeliveriesByLease` 不受影响，属双库不一致缺陷。已在 `OUTPUT` 补齐四列。
- [NotificationDeliveryBatchProcessor.cs](../../../src/Modules/Full.NET.Modules.Notifications/Execution/NotificationDeliveryBatchProcessor.cs) 的候选顺序投影改用值元组，去掉触发 Native AOT 匿名参数规则的匿名类型（语义不变）。
- 架构断言同步：`FindDeliveryById`/`SelectDeliveriesByLease` 期望投影补齐四列，物化器注册列表加入 `NotificationPreferenceRecord`。

实际执行的验证命令与结果（基线 `main@5093b6f4`，工作目录 `E:/wwwroot/github/Full.NET`）：

| 检查 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release` | 0 警告 0 错误 |
| SQL Server 端到端 | `dotnet test ... --no-build --filter "FullyQualifiedName~NotificationsApiSqlServerTests"` | 总计 1，失败 0，通过 1（45s） |
| MySQL 端到端 | `dotnet test ... --no-build --filter "FullyQualifiedName~NotificationsApiMySqlTests"` | 总计 1，失败 0，通过 1（1m 18s） |
| 架构/AOT/契约 | `dotnet test tests/Full.NET.ArchitectureTests/... --filter "FullyQualifiedName~Notification"` | 总计 9，失败 0，通过 9 |

两个端到端用例均串行覆盖以下场景（SQL Server 与 MySQL 同场景）：Failover 首选只物化 1 条、首选成功不发备用、瞬时失败才原子推进备用且每个目标各 1 次 Attempt、双 Worker 竞争只激活 1 条备用、永久错误不换厂商、Unknown 暂挂；Match 缺条件发布 400 `BindingValidationFailed`、唯一命中、无命中/多命中 422；提前回执 `pending_association` → 补偿 → `delivered`；并发重复回执只推进一次且只入库一条；乱序迟到 `sent` 回执可审计记为 `ignored` 且不把 `delivered` 拉回；持续异常到 `MaxAttempts` 后 `dead_lettered` 且不被自动重领取。OpenAPI/序列化契约断言在同一用例内执行。

未验证项：真实厂商送达（无凭据）、容量实测（保持 `Capacity-not-verified`）。

### 2026-10-22 Vue 治理接线与契约证据

- 本人发送偏好与收件端点已拆为独立路由。偏好使用仅需 `PreferencesRead` 的最小渠道目录，不再依赖渠道控制面读取权限；端点页按权限决定是否读取 Profile。
- 场景目录与配额页面已接入服务端导航、本地可信目录、双语资源及生成 Operation 薄适配层。编辑与启停携带读取版本；确认期间撤权不发送命令；两个路由复用组件时重置列表并拒绝旧异步响应覆盖。
- SQL Server/MySQL 真实 OpenAPI 各通过 1 项，分别用时 43.894 秒与 45.812 秒，无跳过，规范化契约一致。客户端生成与离线检查均零漂移。此前 Docker 未启动的失败已保留为历史环境失败，不计为通过。
- 当前关联 Vue 测试 46/46、共享客户端 292/292、双语资源 14/14、SQL 命名与安全 40/40；Vue 类型检查与生产构建通过，构建包含治理与发送偏好页面。集成分片无遗漏或重复。
- OpenAPI 全组首次执行 211/213：操作总数及分组旧断言正在同步；依赖工作区既有摘要分页破坏变更的 Git-ref 测试当前返回 0，而测试要求 1，单独保留失败，不宣称全组通过。
- 双库通知纵向用例与租约预算恢复正在集中复核；真实栈页面、Native AOT、完整迁移恢复仍待验收，不以契约导出或组件测试替代。
- 发送最终边界复核发现：偏好检查早于附件读取，附件准备期间撤回尚未由最终复查覆盖；该时序及小程序一次性授权生命周期仍是批次二未完成项。批次二、三保持未关闭，容量仍为 `Capacity-not-verified`。

### 2026-10-22 发送最终许可与失败整改

- 上轮真实双库复核终态为 3/8 通过、5 项失败、无跳过：SQL Server 三项租约用例通过；MySQL 三项在独立 282 恢复连接缺少用户变量设置处失败；两库通知纵向用例均在人工重投未知调用的旧 `sent` 期望处失败，尚未执行到治理断言。
- 恢复连接现已使用与生产迁移一致的 `MySqlConnectionStringPolicy`，仅在固定目标脚本的 DbUp 连接启用用户变量；业务读写连接不放宽。
- 崩溃恢复断言改为验证人工操作理由不能替代可信对账：旧世代未决配额继续占用，投递保持 `unknown` 且厂商调用次数不得增加。
- 附件和配额等待结束后增加权威目录、收件人偏好复查，再确认有效租约后外发；撤回、静默或目录停用导致确定未发送时，在投递完成事务内释放本轮预留。同步增加六项时序单测，编译与真实双库复核尚待本轮终态。
- OpenAPI 全组实际通过 213/213，无跳过。操作集合改为与 manifest 精确对应；Git-ref 测试使用系统临时目录中的独立 Git 对象树，分别验证无变化通过与分页类型改写失败，不依赖用户当前 HEAD，不产生提交，完成后清理夹具。
- 本记录不关闭批次二、三；小程序一次性授权、过期、完整迁移恢复、原生运行与真实页面验收仍需独立证据。
- 本轮 Notifications 单测实际通过 188/188（含新增六项最终许可时序），无跳过，Release 构建 0 警告、0 错误；SQL 命名与安全 40/40、仓库治理 85/85，分片仍为 1344 项且无遗漏重复。首次未限定处理器数量的编译停留在还原阶段后被主动停止，不计为通过；确认子进程退出并清理本轮资源锁后，单处理器编译完成。
- 随后的完整 Notifications 影响集发现 16 项，终态 11 成功、3 失败、2 项真实厂商凭据缺失而跳过。两项旧失败已越过：MySQL 固定 282 迁移恢复可执行，人工重投不再要求再次外发。新的失败包括 MySQL 租约测试复用同一精确时刻、SQL Server 并发测试固定 500ms 阈值，以及 MySQL 孤儿预留人工重投出现 Attempt 序号重复；不能把此轮算作双库通过。
- 尝试编号现改为读取 `MAX(AttemptNumber)`，人工重置预算后仍接续最大历史序号，避免中断空档导致唯一键冲突；双库治理断言明确验证空档后的 `2 -> 3`。并发测试改用受控厂商阻塞与第二 Worker 完成信号，租约循环使用前进的模拟时间。上述新修复仍需新鲜回归，前一轮单测结果不外推到变更后的代码。
- 上述修复后的 Notifications 单测再次通过 188/188，无失败或跳过；`api-native-aot` 架构选择通过 73/73，无失败或跳过。通知专项架构、Host.Api AOT 分析与双库影响集仍在执行或资源排队，尚不宣称双库或原生运行通过。
- 小程序当前仍按 `accepted` 记录存在性判断授权，未建立按投递原子预留、成功核销、确定失败释放与 Unknown 暂挂的完整生命周期；后续必须覆盖并发投递、撤销和重复客户端上报，不能把当前存在性检查或客户端接受记录表述为厂商可信剩余次数。
- 本轮资源排队没有形成新的双库行为终态：AOT 构建在还原阶段停滞后主动停止，通知专项架构与双库入口因已退出进程的遗留锁失败，未执行对应测试。确认该任务及子进程退出后，仅清理其两把锁；改用显式单 MSBuild 节点重跑 AOT 分析，再顺序运行单测、架构及双库影响集，保留分析器、源生成器与最低发现数。

### 2026-10-22 治理控制面精确授权整改

- 场景目录与发送配额各自拆分 `create`、`update`、`set_enabled`，移除粗粒度 `manage`；授权目录、父页面动作与 Endpoint 同步绑定独立权限。HTTP 路径和请求响应结构未改变，不手改 OpenAPI 或生成客户端。
- 279/281 双库迁移不再向全部存量角色授予权限。新权限由运行时目录声明，普通角色必须经角色管理员显式授予；超级管理员仍从可信目录获取已知权限，不增加授权旁路。
- Vue 创建、编辑、启停入口按单项权限创建，保存前重查对应权限；启停确认期间切换控制面时拒绝提交旧行标识。治理页面单测 20/20、Vue 类型检查通过；SQL 命名与安全检查再次通过 40/40，无跳过。
- 双库纵向用例增加单项授权成功与其他操作直接调用 `403 authorization.permission_denied`，并在创建受限角色后固定重跑 279/281，验证不追加角色权限。复用已有用例，不增加 Integration 发现数；此轮服务端回归正在执行，尚不将权限整改升级为双库已验收。
- 本记录不关闭发送治理或产品闭环批次；一次性订阅授权、完整迁移恢复、原生运行与真实页面仍待完成。
- 权限整改后关联 Vue 回归实际通过 55/55，类型检查及 Vite 生产构建通过；本轮独立临时构建输出已清理。OpenAPI 全组再次通过 213/213，无跳过，未改变权限整改前后的 HTTP 契约形状。
- 首次显式单节点命令因 PowerShell 未引用 MSBuild 冒号参数而报 `MSB1031`，没有形成分析证据；引用参数后重新执行的 Host.Api AOT 分析已进入 C# 编译阶段。后续单测、架构及双库结果仍以该顺序任务的实际终态为准，不将正在执行的任务计为通过。
- 上述 Host.Api `FullNetAotAnalysis=true` Release 构建随后实际完成，0 警告、0 错误，用时 7 分 30.68 秒；保留分析器与源生成器。这是分析构建证据，不是 Linux Native AOT 发布或原生运行证据；后续单测、架构及双库仍待终态。

### 2026-10-23 本次实施与最终验收记录

本次继续使用本计划。实际目录经用户确认是 `E:/wwwroot/github/Full.NET`，分支 `main`、HEAD `5093b6f4e6fc65071fedbeae0e23ba80a8ba28d6`；以 `notifications-20261023` 快照保护先前未提交实现。没有创建工作树、启动多代理或提交、推送、合并、发布。

实现与设计：

- N01–N04 复用既有正确实现，复核真实物化、双 Worker、回执补偿及持久化预算。不可变路由现统一为含 `targets` 的元数据对象；284 成对迁移只包装旧数组一次，不重排或重新解析候选。
- N05/N06 由场景目录控制分类、合法渠道和紧急资格；按收件人冻结政策/偏好版本、实际语言、模板版本及内容。Worker 在附件、额度及意图行锁等待之后重查意愿，保存静默后的下一可发送时刻，并抑制过期或撤回消息。
- 四维额度在短事务中预留，调用在事务外；成功核销，确定未调用释放，Unknown 暂挂。退信不作为未受理证明释放额度。独立 `notifications.deliveries.reconcile` 权限接受 `confirmed_accepted` / `confirmed_not_called` 及证据引用，以 Revision 排除竞争/活动租约，同事务收敛额度、一次性许可和 B0 审计；人工接受仅记 Sent。Unknown 及仍有未决许可的投递不能直接重试。
- N07 的 283 双库台账按用户、应用、模板和客户端交互标识控制本地一次许可，上报幂等且保留不可变原始状态；可用/预留/核销/撤销/待对账分别记录。拒绝、旧上报重放与重新绑定不能复活旧许可，外发边界再次确认确切预留和 OpenId 指纹；客户端接受不是厂商库存。确认未调用的人工处置也不恢复旧同意，需重新授权。
- N08 按收件人及渠道冻结实际 Locale、TemplateVersionId 和内容依据，重试不随发布或偏好变化漂移。
- Inbox 分开保存已读、个人归档和业务撤回；提供个人/批量归档、场景/Producer 查询和受控 Workflow 资源深链。目标页调用原有权威 API 重新验证资源，不通过通知执行审批。撤回与 Worker 以意图行锁确定外发边界，已经跨界及原本 Unknown 均不承诺收回。
- Vue 工作台通过各模块 API 组合公告、Inbox 与 Workflow Todo。控制面显示状态、积压及回执模式，复用邮件/短信 Sent 提示，增加有独立权限的死信、撤回与人工对账；Prometheus 分别监控积压年龄、Unknown 与死信。
- 核对 SMTP、阿里云短信、钉钉、企微及小程序：网络丢失、5xx 和模糊发送响应保留 Unknown；不生成虚假厂商消息号或送达证明。没有扩展新厂商、移动推送、HTML 邮件、摘要或跨渠道编排。

定位和审查记录：

1. 原路由是数组，初次元数据冻结误当对象，真实双库接口返回 500；修正为 `{ targets: [...] }` 并补入单测和迁移重复执行断言。
2. SQL Server 测试缓存只比较迁移条数，复用了缺少 ReportedStatusKey 的旧结构。测试模板现比较全部嵌入迁移名称及内容 SHA-256，并检查无引导业务数据；MySQL Journal 使用实际小写 `schemaversions`。首次大小写错误的失败保留，不计为通过。
3. 取得真实 SQL Server 死锁图：小程序堆表上的 UPDLOCK/HOLDLOCK 扫描申请分区表锁形成环。将参数转换到受控 varchar 列类型，使用现有 Scope/User/App 唯一索引、FORCESEEK 与行锁；SHOWPLAN 确认 Index Seek。双库并发预留继续验证只有一个赢家。
4. 看板首次 SQL Server 500 来自 COUNT 的 Int32 与记录 Int64 映射不一致，改为 SQL Server COUNT_BIG / MySQL COUNT 的显式成对实现。
5. 风险自审覆盖：调用前/后 Unknown、租约换代、旧同意重放、重绑定、撤回与外发竞争、CAS 为零、跨租户资源锚点、精确权限及 B0 审计原子性。没有把无法核验的厂商接受当成可信送达。
6. 最终复核发现待应用回执只依赖厂商重复请求：两次 CAS 冲突后虽正确保留 `pending_apply`，后台却只补偿 `pending_association`。现沿 Worker 采样周期恢复两种状态，每轮最多五十条、只选已有同厂商消息号 Attempt 的记录，逐条短事务重验状态机/CAS；空闲发送队列也执行，不触发重新外发。284 成对增加状态/时间与消息号索引；单测模拟连续两次 CAS 失败后下一轮恢复，双库测试模拟两种待处理状态且无厂商重发。最新补验终态单列，修改前的 193/87/2 不能替代这项补丁验收。
7. 撤回在外发边界前获胜、Worker 随后中断时，原路径可能留下 reserved 配额与订阅许可。撤回事务现自行收敛当前世代、未跨边界的确定未调用预留，保留旧 Unknown 和已跨边界占用；小程序预留同时使用意图到投递的锁顺序，禁止撤回释放之后又登记新预留。双库断言模拟 Worker 中断、四维释放，以及同意有效/已撤销两种许可结局。该整改期间一次构建被源码冻结检查拒绝登记，未运行该中间产物，随后按冻结源码集中补验。
8. 真实页面窄屏复核：首次复合场景在双重登录阶段耗尽 30 秒预算，调整为该场景独立 90 秒；随后实际抵达手机条件选择，截图发现侧栏遮挡。根因为主题将移动侧栏容器设为零宽，而抽屉用自身宽度百分比平移，内部菜单未移出可视区；绑定编辑表单仍三列。按影响修复抽屉自身宽度和该表单移动布局，不修改冻结的 Layui；使用原始 trace、截图和同场景补验核对，失败不记为通过。
9. 深链消费者复核确认 App 按路径缓存页面，原目标页只在首次挂载读取资源，同页切换通知及旧请求迟到会留下错误资源。先以两项回归复现，再监听目标路径与资源标识；待办详情使用请求世代、实例使用取消令牌拒绝迟到结果，离开后返回重新调用权威 API。修复过程中同步保留原手工查询/审批/历史页行为，两个目标页最终 20/20 通过；不让通知回执执行审批。

验证按资源池集中执行；直接命令在 `withTestRun` / `enterHeavyTestPhase` 保护下运行，构建均保留源生成与分析器，使用 Release、单 MSBuild 节点和关闭节点复用。相同产物用于行为测试与 OpenAPI 导出；只有源码变化或实际失败才重建/重跑受影响范围。

本轮实际执行结果（2026-10-23；不是历史测试复述）：

| 范围 | 实际入口/选择条件 | 结果 |
| --- | --- | --- |
| 通知单测 | Release UnitTests DLL，`--filter FullyQualifiedName~Full.NET.UnitTests.Notifications. --minimum-expected-tests 195 --no-ansi --progress off` | 最新冻结代码 195/195，通过（3.298 秒）；包含两库 CAS 连续冲突后再次恢复；保留此前 CLR 崩溃及新增测试替身失败记录 |
| 架构及静态闭包 | Release ArchitectureTests DLL，`FullyQualifiedName~Notification\|FullyQualifiedName~GlobalSqlStatementCatalogTests\|FullyQualifiedName~NativeAot\|FullyQualifiedName~MemoryPackControlledProtocol\|FullyQualifiedName~AsyncBoundaryTests`，最低 80；修正目录后单独选择 `GlobalSqlStatementCatalogTests.Production_global_sql_statements_are_exactly_cataloged`，最低 1 | 本轮 87 项先 86 通过、1 失败：目录仍要求旧 `pending_association` 条件；同步为两种待处理状态后单独 1/1 通过，未重复其余 86 项 |
| 双库行为 | Release IntegrationTests DLL，`--filter FullyQualifiedName~NotificationsApi --minimum-expected-tests 2 --timeout 30m` | 最新 SQL Server/MySQL 合计 2/2，通过（3 分 21.709 秒，无跳过）；并发路由/回执、预算、静默/撤回/额度、小程序、语言、归档/撤回/资源授权及 283/284 部分失败/重复执行均在用例内实际断言；新增空闲回执补偿、撤回自行释放四维预留、许可有效/撤销两种结局均通过 |
| 双库真实 OpenAPI | 同一 IntegrationTests 产物，分别选择 SQL Server/MySQL OpenAPI 导出用例；重新生成客户端 | 各 1/1，通过，24.529/37.715 秒；不是手改生成契约 |
| OpenAPI 离线门禁 | `node --test tests/openapi/*.test.mjs` | 213/213，通过 |
| 客户端与多语言 | `pnpm --filter @fullnet/client-contracts test`；`pnpm --filter @fullnet/admin-i18n test`（固定 pnpm 10.26.0） | 客户端 292/292；双语 14/14，通过 |
| Vue 行为 | `vitest run` 指定 Notifications 的 15 个 API、组件及页面测试文件（含两个 Workflow 资源目标页），并增加 ArtAdminShell 相关测试 | 窄屏修复后 92+5 项通过；深链缓存整改后二目标页 20/20 重跑通过，复用其余 79 项，最终 16 文件合计覆盖 99 项；不是将旧失败计为通过 |
| Vue 静态检查与构建 | `vue-tsc --noEmit -p tsconfig.json`；Vite `build({ root: 'ui/admin', configFile: 'ui/admin/vite.config.ts', build: { outDir: <系统临时目录> } })`，资源池预算 3 CPU / 4 GiB | 包含窄屏及深链缓存修复的最新类型检查、生产构建均通过，构建 1.42 秒；输出复用本任务系统临时目录，未输出到根目录 artifacts |
| 真实 Vue 基础场景 | 资源池包装的 Playwright `inbox-messages.spec.mjs`、`notification-platform.spec.mjs` 与 viewer 权限相关用例，`--project=vue-admin`；分别设置 SQL Server/MySQL | 各库 4/4，通过；归档/未读数、工作台、控制面及无权限 403；未启用冻结的 Layui |
| Match 页面补验 | `notification-platform.spec.mjs --grep 'Match 条件编辑' --project=vue-admin`，仅显式开启 SMTP 类型目录，未发送邮件 | SQL Server 桌面 1/1；最新 MySQL 1/1（完整入口 2.9 分钟，进程回收成功），覆盖两次条件保存、390×844 窄屏、菜单开关/Escape、键盘焦点、边界及登录后控制台；已检查最终截图 |
| SQL 结构及安全 | `node --test tests/naming/sql-naming.test.mjs tests/naming/uuid-storage-sql-governance.test.mjs tests/sql/sql-safety.test.mjs tests/naming/sql-comments.test.mjs` | 最新 27/27，通过 |
| 治理规则 | `node --test tests/governance/*.test.mjs` | 85/85，通过 |
| 当前 Host.Api AOT 分析 | `run-build-with-resources.mjs --cpu 1 --memory-gib 6 --isolated-output <系统临时目录> -- dotnet build src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj -c Release -p:FullNetAotAnalysis=true -m:1 -nr:false -v:minimal`；本任务进程绑定逻辑 CPU 31、进程级 `DOTNET_TieredCompilation=0` | 最新回执恢复、撤回释放和小程序预留锁边界均已包含，0 警告/0 错误，2 分 23.21 秒；这是静态分析构建，不是原生发布或运行 |

影响集记录：`run-affected-integration.mjs --snapshot notifications-20261023 --phase slice` 中基础集成共 12 项曾通过，包括 Auditing 指标管线 1/1（12 分 13 秒）、Logging 指标 1/1（6 分 08 秒）及其余 10 项；通知场景因测试模板缓存、死锁及 COUNT 映射连续暴露缺陷后修复，最终双库 2/2 独立重跑通过。原影响集曾失败且两个真实厂商用例跳过，不能将其写成一次完整影响集零失败。

本轮未关闭的技术验收：

- Native AOT：早期分析构建 0 警告/0 错误，但之后更新过 SQL Summary；最新分析构建曾在 ILLink RoslynAnalyzer 内部以 `0x80131506` 崩溃，不能沿用早期成功。Linux x64 SDK 10.0.402 / Runtime 10.0.13 的首次完整原生发布在 `ElfObjectWriter.EmitRelocation` 报 OutOfMemory（容器上限 6 GiB）；内存恢复后的单 CPU、12 GiB、关闭本地调试符号补验在 ILC `Generating native code` 阶段 Segmentation fault，退出码 139。没有可验收原生程序，也没有本轮原生 HTTP/SignalR 证据。未移除分析器/源生成器或修改产品 AOT 配置来绕过检查。
- 前端包体：按任务开始快照 Git blob 虚拟载入源码构建基线，没有复制整仓。首页静态 JS 基线 1,511,113 字节/gzip 393,213；最新 1,521,901/gzip 395,887，增加 10,788 字节（0.71%）/gzip 2,674（0.68%）。既定预算 1,426,485 字节、允许 5%，两者均超标；gzip 同样超过 376,928 字节加 5% 范围，前次判断已校正。Chart 与 VForm 懒加载预算通过。不提高预算，也不将该失败记成通过。
- 环境：只读 `node scripts/doctor.mjs --profile full --json` 记录 Node 24.20.0、pnpm 10.26.0、SDK 10.0.402；Docker 曾暂不可用，恢复后才重试依赖环境的补验。其他任务的进程、数据库及资源锁不强行终止。
- 最新环境故障：MySQL Match 补验在 Host.Api `csc.exe` 以 `-1073740940` 退出，尚未进入页面；随后分析及 Unit 编译分别在 Roslyn 不同路径出现 `0x80131506`。独立 PowerShell 进程也在 `System.Globalization.CompareInfo.Compare` 发生 `0xC0000005`，故不能将所有失败推断为通知源码或单纯内存不足。按[官方运行时编译配置](https://learn.microsoft.com/dotnet/core/runtime-config/compilation)以进程级 `DOTNET_TieredCompilation=0` 做过差异诊断，仍失败；之后仅将本任务进程绑定逻辑 CPU 31 再核验。没有变更系统、BIOS、全局运行时设置或产品配置；未取得足够证据确定环境根因。
- 差异诊断后，包含最新补丁的 Host.Api AOT 分析成功（见上表），此前 CLR 崩溃仍是历史失败，不再称当前分析未通过。单测曾 241 项中 239 通过、2 失败，失败来自新增查询替身对内部投影的动态代理可见性；改为显式查询替身后，通知命名空间最终 195/195，通过结果见上表，未放宽生产类型可见性。
- 最终通知单测实际通过 195/195；本轮单测/架构/双库入口恢复默认分层编译，仅限制本任务进程到逻辑 CPU 31，SDK 分析器与源生成器始终保留。测试硬件为 i9-14900K、32 个逻辑 CPU、约 64 GiB 内存，Docker Engine 29.9.1；该进程诊断配置与低并发场景不构成容量证据，也没有充分证据确定环境崩溃根因。
- 临时产物清理：自动审批审查拒绝了对本任务 `C:/Users/dai27/AppData/Local/Temp/fullnet-notifications-native-20261023` 内可再生成构建目录的递归清理，只返回 `blocked by policy`；该操作未执行，保留失败日志及暂存构建产物。没有将失败的大产物复制到仓库。
- 页面补验诊断：将整个浏览器进程链绑定单核导致加载和交互预算耗尽，改为仅对本任务直接启动的 .NET 子进程设置 CPU 绑定，浏览器/Vite 使用正常 CPU；诊断启动器不进入产品代码。曾使用父进程包装而导致回收失败，确认归属后停止该轮遗留的两个临时 .NET 进程，最终恢复直接启动并验证正常回收。控制台唯一预期项是登录前 `/api/v1/auth/refresh` 的 CSRF 403，按响应路径、文本和登录阶段精确识别；登录后的错误仍失败。没有通过提高全局超时或忽略全部 403 来掩盖错误。

厂商、容量和外部资料边界：真实 SMTP/阿里云短信因无运行凭据而跳过；钉钉、企微、小程序也未取得真实厂商发送/送达证据。SMTP 历史 External-auth-not-verified 保留，容量继续 Capacity-not-verified。指定 G 盘外部报告原件在当前设备不可访问，不能伪称已经读取或更新；本次实现、历史失败及验收证据保留于本计划、能力表和代码百科。

收尾复核：分支 `main`，HEAD 仍为 `5093b6f4e6fc65071fedbeae0e23ba80a8ba28d6`；`git -c core.safecrlf=false diff --check` 通过。整个工作区有 123 个已跟踪修改、59 个未跟踪项，均未暂存，包含既有实现及其他任务的改动，不能全部归为本任务。Layui 差异为零，根目录 artifacts 不存在；本任务 5159/25173 端口及临时 .NET 服务均已退出。未提交、推送、合并或发布；完整原生发布与首页 JS 包体预算两项验收仍未关闭。

### 2026-10-23 续办：原生发布与 JS 预算

- 保持 main@5093b6f4 与原任务快照，不提交或推送。匿名首屏不使用实时传输，将 SignalR 改为认证状态机触发动态加载；加载失败沿首次连接退避恢复，异步加载后重新判断会话/租户与 dispose，迟到连接不能启动。
- 新增三个竞态/加载失败回归，客户端 `vitest run` 295/295；Vue App 与实时状态 15/15（此前一次 5 秒超时失败保留，未提高预算）；类型检查及生产构建通过。
- 固定首页预算不变：1,467,584 minified / 382,260 gzip / 316,942 Brotli；SignalR 延迟包 55,116 / 14,040 / 12,449，新增独立 5% 退化门禁；Chart 496,497 / 166,668 / 141,757，VForm 8,501 / 3,235 / 2,785，四项全部通过。预算结构回归 3/3。全部 JS 为 4,717,131/gzip 1,406,320，相对任务开始快照 4,691,654/1,399,652 增长 0.54%/0.48%，包含前述功能批次；不宣称总体积减少或容量提升。
- 实际命令：`node <临时目录>/notifications-frontend-accepted-20261023.mjs`（资源池保护的 Vite + vue-tsc）、`node <临时目录>/notifications-budgets-closed.mjs`（直接调用仓库 measure/evaluateBudget，未修改既定首页阈值），输出在 `notifications-bundle-closed-20261023.json`。
- 生产构建 Edge headless 补验：匿名登录页面可见，首屏 SignalR 请求 0，实际延迟模块加载成功，pageerror 0；原始结果 `notifications-lazy-browser-retry.log`。首次使用未安装的 Playwright 默认浏览器及按钮文本定位失败保留，改用项目既定 msedge 与稳定 login-view 锚点。此检查使用匿名 API 夹具，不替代此前真实双库业务 E2E。
- Native 差异实验：单容器 CPU 31、16 GiB、ILC parallelism:1，仍出现 PropertyDefinition.GetAccessors 元数据越界；483 个输入程序集全量属性元数据检查通过，409 个 NuGet DLL 及 ILC 工具均与包内 SHA256 一致。仅将本轮 ILC 输入二进制复制到容器临时文件系统，继续相同编译器/配置实验，不复制源码。Docker 引擎中途不可用，恢复并核验原 PID/容器均已退出后，仅清理本任务两项残留锁；重跑正在执行。全部失败日志保留，未扩大 suppression 或关闭分析器；原生发布/运行尚不得标为通过。

- Native 本地输入实验最终失败：ILC 约 9 分钟后在 `Internal.TypeSystem.LockFreeReaderHashtable.TryGetValue` 触发 Access Violation / FailFast，容器退出 134；未生成 `.o` 或原生可执行文件。16 GiB 上限下观察内存约 3.4 GiB，此次没有 OOM 证据。输入完整性检查通过也不能证明编译器或机器可靠；不能断言硬件根因。原始完整堆栈保留 `fullnet-notifications-native-20261023/local-inputs-ilc.log`，先前 139、元数据越界及 Docker 中断记录均保留。暂未取得原生产物，原生 HTTP/SignalR 双库运行未执行，Aot-published 保持未达成；下一步需在稳定 Linux 编译环境用同一源码/SDK 复现，不能以调整业务代码、关闭检查或换用历史程序冒充通过。

- 续办收尾：原生编译失败后，核验 kind_snyder 的完整命令及挂载均属于本任务，停止其依赖的测试程序集预构建；该构建未完成，不记为通过。临时编译容器均退出，浏览器 25174 端口已释放。分支 main、HEAD 5093b6f4 未变，工作区 126 个已跟踪修改/61 个未跟踪项，未暂存，包含既有及其他任务工作；git diff --check 通过，仓库根 artifacts 不存在。未提交或推送。

### 2026-10-23 Native AOT 两阶段历史对照（已由下节默认链路验收补齐）

- 本机硬件异常有 WHEA 事件 19 / Internal parity error 直接证据；输入 483 个程序集元数据检查、409 个包内 DLL 及 ILC 工具一致性通过。本轮对 484 个输入（含 RD.XML）另记 SHA-256，编译完成后全部未变；同一 ILC 10.0.13 的 Windows 宿主版本在仅本任务 CPU 31 上成功完成 Linux x64 目标生成，退出码 0，ELF `.o` 1,021,490,539 字节。证明这批输入并非必然导致编译失败，当前证据更偏向本机执行环境；仍未完全排除 Linux 版 ILC 特定缺陷，不将硬件事件当成每次失败的直接因果。
- 未变更任何产品代码、AOT 根、分析器、源生成器、模块注册或告警准入。Windows ILC 原始 9 条告警按 MSBuild 格式规范化后使用原 `validatePublishWarnings` 严格验证，全部为既有准入程序集/告警码；原日志保持原样。
- 对照过程曾因长时间编译采样：首次符号解析等待暂停本任务进程，核验归属后退出调试器并恢复线程；后续离线采样快速脱离。匹配官方 PDB 解析旧采样，位于 ObjectDataInterner.EnsureMap / MethodInternComparer.Equals 去重阶段；编译最终成功，不能据长耗时断言死循环。相关栈和调试器失败日志均保留。
- Linux SDK 10.0.402 / Runtime 10.0.13 / clang 镜像执行 `dotnet publish src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj --no-restore --no-build -p:IlcSingleThreaded=true -c Release -r linux-x64 --self-contained true -p:FullNetPublishMode=NativeAot -p:RuntimeFrameworkVersion=10.0.13 -p:UseArtifactsOutput=true -p:ArtifactsPath=/out/build -p:RestoreFallbackFolders=/cache -o /out/publish -m:1 -nr:false -v:minimal`，复用上述 ELF 完成真实链接/符号分离，退出码 0。实际原生文件 142,770,016 字节，SHA-256 `78C00814945CF73C33BF9C1984EE2C918B393765EC0CE96FB0ACEACA4E202023`。这是本地定制两阶段证据；官方不支持标准跨 OS publish，不能宣称默认 Linux ILC 链路已恢复，也不据此改变正式发布入口。
- Linux IntegrationTests Release 构建 0 警告/0 错误，6 分 26.58 秒；同一产物运行 `dotnet <临时产物>/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter FullyQualifiedName~NativeApiNotifications --minimum-expected-tests 2 --timeout 30m`，**SQL Server/MySQL 合计 2/2，失败 0，跳过 0，2 分 06.819 秒**。真实 Linux ELF 子进程完成登录、Notifications HTTP/JSON、事务 Outbox/SignalR 及启动停止；两份原生进程日志包含 listening 与 shutting down。回收时 Hub 的取消日志不等于未处理崩溃；用例实际全部通过。
- 源码始终只读挂载，没有复制整仓；容器内临时 `/src` 通过符号链接使用真实源码，根构建配置及 Migrator 的嵌套只读挂载确保 `--no-build --no-restore` 复用已构建 Linux 产物。仅测试输出写入系统临时目录，仓库根没有 artifacts。
- 原始结果统一在 `C:/Users/dai27/AppData/Local/Temp/fullnet-notifications-native-20261023`：`windows-host-linux-target.log`、`input-hashes-windows-host.json`、`compile-inputs-and-warnings.json`、`publish-final-link.log`、`native-tests-build.log`、`native-runtime-verified.log`、`native-logs/native-aot/linux-x64/test-logs/`；对应可复现脚本为该目录的 `publish-final-link.mjs`、`build-tests.mjs`、`run-runtime-only.mjs` 以及父临时目录 `notifications-ilc-windows-host.mjs`。旧 OOM/139/元数据越界/134/Docker 中断证据保留，未清理此前被自动审批拒绝删除的目录。
- 当前 Notifications 可声明本地两阶段 Linux 原生产物及双库运行已验，不外推 Worker/Migrator Native AOT、新增 Provider、真实厂商送达或完整所有 Provider/功能门禁；容量继续 Capacity-not-verified。标准 Linux ILC 故障及 WHEA 处理器异常仍需在稳定环境独立复核，不通过改产品配置掩盖。

- 最终检查：`git -c core.safecrlf=false diff --check` 通过；main / 5093b6f4 未变，126 个已跟踪修改、61 个未跟踪项、无暂存（含其他任务既有改动）；本任务 ILC、调试器、发布及运行容器均已退出，保留共享双库/Redis 测试缓存，仓库根 artifacts 不存在。未提交、推送或发布到外部环境。JS 四项预算沿用同一源码产物的通过证据，本轮未改前端或通知产品代码。

### 2026-10-23 默认 Linux SDK/ILC 完整链路最终验收

本次用户要求默认 Linux ILC 链路本身通过，明确不能用前次两阶段产物替代，并告知 CPU 已有问题、不需再次检测。用户另行明确授权临时限制共享 WSL/Docker 虚拟机到已知稳定核心，核验后恢复。本轮只检查亲和性配置及恢复，不重做硬件检测。

**结论：本次默认 Linux 编译、链接、发布及新产物双库运行均通过。** 同一 SDK/ILC 和输入在受控执行条件下可完成，当前没有定位到需要修补 Full.NET 源码或升级/替换编译器的缺陷。修复措施在执行环境：已知稳定核心约束与足够内存；这不修复 CPU 本身，不能外推为解除约束后本机仍可靠，也不作编译器绝无其他缺陷的断言。

| 证据 | 本轮结果 |
| --- | --- |
| 基础工具链最小复现 | Linux SDK 10.0.402 / ILC 10.0.13 发布最小程序并成功原生启动；只证明基本编译器/链接器可用 |
| 执行环境 | 用户授权后记录原 VM 亲和性；取得资源名额后限制宿主 VM 为 CPU 31，进程及 92 线程读回均为 2147483648。10 GiB 实际预留与容器上限、1 CPU；原 16 GiB 请求未被资源池准入时没有绕过门禁，排队期间保持原亲和性 |
| 输入隔离 | Linux 常规构建后 484 个输入 SHA-256 与前次完全一致；新旧 Linux ILC RSP 除输出目录外完全一致 |
| 编译器执行 | 容器 `/proc/87/exe` 为 `/out/nuget/runtime.linux-x64.microsoft.dotnet.ilcompiler/10.0.13/tools/ilc`，命令行使用全新 `/out/default-linux-native/Full.NET.Host.Api.ilc.rsp`；没有调用 Windows ILC，没有复用旧 `.o`，没有传 `--no-build` |
| 完整 publish | Linux ILC 新生成 1,020,414,827 字节 ELF 对象，随后 clang/ld 链接、符号分离和发布，最终退出码 0；ILC 约 45 分钟，完整流程约 57 分钟，仅记录执行事实，不作为性能或容量结论 |
| 告警 | 本轮原始日志直接经过原 `validatePublishWarnings`，9 条全部为既有准入；未改分析器、源生成器、根、模块注册、白名单或业务实现 |
| 新程序 | `default-linux-publish/Full.NET.Host.Api`，ELF，142,770,016 字节；SHA-256 `B50FA29233D605297AC2B9488B6E6F1FFC69F69DEFDF7BE7CE9D250A363F75C1`，不同于前次两阶段程序 |
| 新产物原生运行 | Linux Release IntegrationTests 复用已构建程序集，`--filter FullyQualifiedName~NativeApiNotifications --minimum-expected-tests 2 --timeout 30m --no-ansi --progress off`，SQL Server/MySQL **2/2，失败 0、跳过 0，4 分 32.140 秒**；HTTP/JSON、事务 Outbox/SignalR、原生进程启动停止实际通过，未复用前次运行结果 |
| 恢复 | 发布及运行各自 finally 恢复；最终独立读取 VM 进程与全部 92 个线程，掩码均为原始 4294967295。发布/运行容器均退出，保留共享双库/Redis 缓存 |

实际发布命令在 SDK 容器内执行（源码只读，NuGet 缓存只读，系统临时输出可写）：

```text
dotnet publish src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj --no-restore -p:IlcSingleThreaded=true -p:NativeIntermediateOutputPath=/out/default-linux-native/ -c Release -r linux-x64 --self-contained true -p:FullNetPublishMode=NativeAot -p:RuntimeFrameworkVersion=10.0.13 -p:UseArtifactsOutput=true -p:ArtifactsPath=/out/build -p:RestoreFallbackFolders=/cache -o /out/default-linux-publish -m:1 -nr:false -v:minimal
```

输出目录隔离只用于防止旧对象导致假绿；其余编译参数和输入保持对照一致。该流程不是 Windows 编译后由 Linux 链接的两阶段流程。本轮本机资源前提仍须注明，稳定生产/CI 环境不应硬编码此机器 CPU 31。旧 6 GiB OOM 与本轮观察到约 7 GiB 的 ILC RSS 应单独解释，不能把资源不足和访问冲突混为同一软件缺陷。

证据统一位于系统临时目录 `fullnet-notifications-native-20261023`：`linux-ilc-minimal.log`、`default-linux-ilc-affinity.log`、`default-linux-compiler-process.txt`、`default-linux-publish-container.json`、`default-linux-managed-input-comparison.json`、`default-linux-rsp-comparison.json`、`default-linux-link-proof.txt`、`default-linux-publish-result.json`、`default-linux-native-runtime.log`、`vm-affinity-original.json`、`vm-thread-affinity-active.json`、`vm-affinity-final-restored.json`，以及两份新原生进程日志 `fullnet-native-aot-295e25b6971840f09d89c2c9ac96f70a.log`、`fullnet-native-aot-9de42286db4841ab8096475db0f24c9b.log`。复现入口为同目录 `publish-default-linux-scoped.mjs`、`run-default-linux-runtime.mjs`；必须重新确认目标 VM 世代及原亲和性，不能将本机参数当通用项目默认配置。

所有旧失败与两阶段对照均保留。此前排队进程在未启动编译容器时停止，核验无子容器后仅清理该 PID 的失效资源锁；没有抢占其他任务。Windows PowerShell 诊断包装器输出过 CLIXML/编码杂讯，数字读回及独立 JSON 核验有效，此包装器不进入产品代码。源码及四项 JS 预算输入未变，复用已通过的前端证据；真实厂商与 Capacity-not-verified 边界不变。

收尾：`git -c core.safecrlf=false diff --check` 通过；main / 5093b6f4 未变，126 个已跟踪修改、61 个未跟踪项，无暂存，含此前及其他任务改动。共享虚拟机进程及全部 92 线程已独立读回原始掩码；本任务发布/运行容器、ILC 和调试器均退出，共享 SQL Server/MySQL/Redis 缓存保留，根 artifacts 不存在。未提交、推送或外部部署。

### 2026-10-23 续办：Worker、治理 API 与通知告警（执行中）

- 本轮开始时只新增验收覆盖与修正文档；后续已发现并修复 Worker 权益端口注册缺失，详见下文。初轮 Host.Api 复用前节默认 Linux 发布的新 ELF；`governance-api-reuse-proof.json` 再核对 484 个原编译输入 SHA-256 全部未变，可执行文件仍为 `B50FA29233D605297AC2B9488B6E6F1FFC69F69DEFDF7BE7CE9D250A363F75C1`。
- Worker 使用同一 Linux SDK 10.0.402 / ILC 10.0.13 的完整 `dotnet publish`，新目录 `worker-current-native`，生成 589,230,479 字节对象；482 个 ILC 输入记于 `worker-current-managed-inputs.json`。随后完整发布实际退出 0，7 条告警全部通过原准入；90,536,568 字节 ELF，SHA-256 `7391FDBF6DB38B7238DCFF718F3159C281CCE0E844AC8901E075559EF33A3AAE`，证据 `worker-current-publish-result.json`。运行门禁尚待终态，不能仅凭发布关闭验收。
- 首次资源探测 PowerShell 子进程退出码 2、无 stdout/stderr，`fullnet-test-run-KtmWJY/run.json` 保留失败。后续任务进程沿用获准稳定核心条件；VM 进程及 92 线程读回 CPU 31，证据 `worker-vm-affinity-active.json`，不重做硬件检测。
- 测试构建复用已预留 9 GiB/1 CPU 的容器，独立输出 `test-build/native-artifacts`，不复制源码。首个依赖构建在补全用例时主动停止，日志 `governance-tests-build-arrange-cancelled.log` 不计为通过；随后构建因新测试 `Assert.Inconclusive` 的可空参数失败（CS8604），原始 `governance-tests-build-final.log` 保留，未关闭可空检查，已修正并重建。
- 为避免发布容器退出中断其内部测试构建，曾核验归属并临时暂停仅本任务 SDK 发布父进程（容器 PID 1，Docker host PID 7184），没有暂停其他任务；ILC 当时已结束、链接子进程完成。可空参数修正后构建实际通过，0 警告/0 错误，7 分 00.86 秒，见 `governance-tests-build-retry.log`；父进程已恢复并完成 publish，容器退出，原亲和性已恢复。进入运行门禁后再按既有授权临时限制，结束仍必须恢复并独立核验。
- API 原生用例补政策/偏好/额度、CAS、人工对账 B0 审计、小程序本地授权重放/撤回、归档与业务撤回及租户隔离；小程序身份交换仅预置隔离事实，不作厂商授权证明。Worker 门禁使用 JIT API 只安排隔离数据，所有领取、治理、预留与恢复由独立原生 Worker 执行；与其他 Worker 原生探针保持同一安排方式，API 原生用例独立验收。
- 通知告警用真实数据库状态与生产采样器，经实际部署 Collector 配置导出到 Prometheus，保持原规则 `for: 5m` 验证积压、死信、Unknown 三类触发和恢复；已实际通过，原始失败与补跑终态见下文。真实厂商、外部报告不可访问及 Capacity-not-verified 边界不变。
- 已实际通过：`node --test tests/testing/test-matrix.test.mjs tests/testing/native-aot-runtime-results.test.mjs`，51/51；`node --test tests/governance/native-aot-publish.test.mjs tests/governance/native-aot-workflow-artifacts.test.mjs tests/governance/native-aot-guidance.test.mjs`，18/18。这些结果不替代原生运行或告警行为验收；最终重发产物仍须取得对应运行终态。
- 首轮原生运行实际发现 31 项（Worker 29 + API 双库 2），终态 **28 通过、3 失败、0 跳过，59 分 10.281 秒**。新增 Notifications 治理 Worker 双库均通过，完整 Worker 范围为 28/29；不能写为完整 Worker 门禁通过。MySQL/SQL Server API 分别运行 2 分 37.829 秒、1 分 51.614 秒后均在最后的租户隔离安排失败：误用 JIT 工厂种子 `acme`，实际原生 Migrator 的 Development 种子为 `local`。已改用现有 `NativeApiE2EAssertions.EnterLocalTenantAsync`，测试程序集重建通过，未改变产品输入或重新发布原生 API。另一失败为既有 `MySql_native_worker_recovers_settled_pdf_generation`：任务进入 failed；补充失败码诊断，只补跑失败的三项。原始 `governance-native-1792737302330.log`、完整 TRX、独立 Worker 范围核对 `worker-current-scope-first.json` 与进程日志保留，不裁剪失败结果。另观察到 DataApproval 请求恢复后台循环的 InvalidOperationException；该阶段尚未确认根因，后续定位及修复见下文，未关闭循环。
- 修正原生租户夹具后，`NativeApiNotifications` 双库与失败的 MySQL PDF 恢复三项补跑 **3/3、0 失败、0 跳过，8 分 43.489 秒**；完整 TRX 与精确发现 UID 校验为 `governance-api-pdf-1792742374097`。MySQL PDF 首轮瞬时失败尚无确认根因，补充诊断后未复现，不宣称产品修复。上述原生治理 API 行为通过，但后续 Worker 依赖修复影响共享 Tenancy 组件，最终产物与补验将另记，不能外推旧二进制。
- DataApproval 后台错误已沿构造依赖定位：`DataApprovalRequestLinkService → IWorkflowInstanceStarter → WorkflowInstanceManagementService → ITenantFeatureEntitlementPort`。系统临时最小 JIT 依赖解析原始记录 `worker-di-reproduction.log` 明确为该端口未注册（退出 1）；原 `TenancyModule.AddServices` 有注册、`AddBackgroundServices` 缺失。已把真实权益端口及查询依赖移至 API/Worker 共用的后台注册入口，保持 Scoped，不用空端口或关闭循环；新增回归并将审批恢复错误纳入原生 Worker 故障标记；修复后原生产物六项验收已通过，见下文。
- 告警首跑失败保留：`governance-alerts-1792742946692`，1 失败、0 跳过、56.837 秒，真实 Collector 启动后 `ForceFlush` 未完成导出。测试显式 OTLP Endpoint 缺少 `/v1/metrics`；依据已安装 OpenTelemetry 1.17.0 的 Endpoint 契约补齐路径并清除环境认证头，未修改生产规则或缩短五分钟窗口。原始 TRX、`alert-temp/fullnet-notification-alerts-40670ded0425485baed74efd12844d3c/` 的 Collector 日志及全部规则状态保留；修正后的补跑结果见下一条。
- 修正后告警实际通过：`governance-alerts-1792744316749`，**1/1、0 失败、0 跳过，7 分 16.180 秒**。真实 SQL Server 状态由生产 `SampleBacklogAsync` 采样，经部署 Collector 0.163.1 进入 Prometheus 3.15.0，三条规则保持 `for: 5m`，均经历 pending → firing → inactive，恢复后三项指标归零断言通过。输出名称精确匹配规则，序列不含租户、用户、Profile 或收件人标签。原始证据目录为同一系统临时根下 `alert-temp/fullnet-notification-alerts-f8ff1249636e42ba81c884176ab56147/`，包含 pending、firing、resolved、导出序列、Collector 日志与完整规则状态。另一次排队阶段 Windows 提交内存探针超时，记录 `fullnet-test-run-wR0nej`；没有进入告警测试，不能计为告警行为失败或通过。未验证外部 Alertmanager/真实通知接收端。
- 本批最终测试程序集重建 **0 警告、0 错误，12 分 12.76 秒**；权益注册及审批 Worker 选项回归 **3/3、0 跳过，1 分 55.244 秒**，API/Worker 均只注册一个真实 Scoped 权益端口。`node scripts/testing/run-dotnet-test-suite.mjs architecture --no-build --selection api-native-aot` 复用未改变的架构测试程序集检查当前源码，**73/73、0 跳过，51.087 秒**。治理 Node 检查 85/85、矩阵/原生结果校验 51/51；新程序集分片为 SQL Server 204、MySQL 204、迁移 562、基础设施 295、重型消息 80，合计 1345，无遗漏或重复。
- `node scripts/testing/check-frontend-bundle-budget.mjs` 对未改变的 Vue 构建产物重新检查，首屏与 SignalR/Chart/VForm 延迟包四项均通过：首屏 1,467,584/gzip 382,260；SignalR 55,116/14,040；Chart 496,497/166,668；VForm 8,501/3,235 字节。不以此推导容量、吞吐或首屏延迟改善。
- 共享 Tenancy 注册改变后重新发布 API 与 Worker，保留原生旧产物散列及前述行为结果作为历史。本轮提交内存预算不足时未绕过资源池；在同机重型锁内重查测试双库应用会话为零（SQL Server 内部 CEIP 单独排除），临时停止 `fullnet-it-mssql`、`fullnet-it-mysql`，保留容器与数据以回收缓存占用。`publish-after-di-pipeline.mjs` 对停止和恢复记录原始状态，发布成功或失败均在 finally 启动原实例；未停止 Redis 或共享虚拟机。新 Worker 运行终态已登记，API 新发布仍在进行；最终恢复读回须在本次执行结束登记。
- 依赖修复后的 Worker 默认 Linux 发布已成功（退出 0），没有使用两阶段对象：新 ELF **90,536,568 字节**，SHA-256 `2546357779c45291f22dfb6f1b10f2d8d11d3d548f2c624becd848f9e155813c`；新 `.o` **589,230,545 字节**，482 个输入有完整散列。与首轮相比变更为 Tenancy、Composition 与 Worker 入口三个程序集，文件大小相同但 ELF 散列已改变；7 条既有第三方告警全部准入。`worker-after-di-publish-result.json`、`worker-after-di-input-delta.json` 和原始发布日志保留。Worker、API 发布期间分别独立读回 VM 进程及 92 线程，均为 CPU 31 掩码；发布与最终运行终态继续登记，不由编译成功关闭行为门禁。
- API 重发发生新的环境中断：**17:37:12** Docker 返回 `error waiting for container: unexpected EOF`，原 VM PID 12784 退出，Docker 后端记录 WSL bootstrap 退出、引擎停止与自动恢复；新 VM PID 27216 于 17:37:21 启动。没有得到本次 ILC 自身的访问冲突或成功发布终态，不能把这次失败归类为已确认编译器缺陷，也不能计为发布成功。原始 `api-after-di-publish.log`、Docker 后端退出节选、`fullnet-test-run-y5dmix` 和中断时主输入散列保留。finally 恢复旧 PID 亲和性因其已退出而失败，未越过世代检查修改其他进程；控制脚本已启动原双库，新 VM 初始掩码读回为原始 4294967295，原世代记录另存 `vm-affinity-generation1-original.json`。Redis 也因 VM 重启退出，已恢复原实例。后续使用新世代记录与获批限制继续独立 Worker 验收；API 采用新的原生中间目录重新走默认链路，不复用中断对象，不重新检测 CPU。

- 最终新 Worker 行为补验 `governance-worker-final-1792748546679`：**6/6、0 失败、0 跳过，16 分 58.732 秒**，包含 SQL Server/MySQL 各一项通知发送治理/预算恢复、Workflow 通知投影及 PDF 已结算恢复。所有原生进程正常停止，故障标记没有再次报告权益依赖解析错误。首轮完整 29 项中的 MySQL PDF 失败及其余历史结果仍保留；本次六项通过不改写首轮为全绿，也不宣称首次 PDF 故障根因已确认。API 使用 `api-after-vm-restart-native` 的默认 Linux 发布已进入 ILC；为按最新指令合并远端而终止旧输入运行，未完成、未计为通过，不能据旧 API 二进制关闭共享组件修复及合并后的门禁。

### 2026-10-23 全部代码提交与远端同步

- 最新用户指令明确授权“提交所有代码，并同步”；全部既有工作区实现已保存为 `288f613f`。同步前 `origin/main` 前进至 `e50f7c82`，含四个 AI/性能提交，使用普通合并保留两边历史，不强制推送。
- 合并处理五处内容冲突：双库注释覆盖合并、测试最低发现数按两边新增量相加、保留两种 Git-ref 契约回归、保留通知与审批 Worker 故障标记；同一权益端口修复统一为共用入口的一份 Scoped 注册。重新生成客户端契约和数据库对象注释。
- 远端已使用迁移 277，Notifications 原 277–284 成对顺延为 278–285，并同步所有恢复测试的精确资源名。双 Provider 各编号唯一，16 份通知 SQL 正文逐字比较（仅换行归一化）均与提交 `288f613f` 相同；历史 283/284 失败恢复证据保留原编号，不能冒充新编号部署结果。
- 同步检查：治理、OpenAPI、矩阵、原生结果解析及数据库诊断脚本的直接相关 Node 检查初跑 364/366，两项过期规则断言失败；对齐远端新规则后 **366/366、0 跳过**。完整原始输出 `sync-node-checks.log`、`sync-node-retry.log` 留在同一系统临时根。没有运行全量测试。
- 合并后直接契约消费者检查：资源池准入后执行 `node ui/admin/node_modules/vue-tsc/bin/vue-tsc.js --noEmit -p ui/admin/tsconfig.json`（实际从 ui/admin 目录运行对应相对路径），类型检查通过；client-contracts 的 `notifications-realtime.test.ts` **12/12** 通过。数据库编号/正文比较与这两项原始输出为 `sync-focused-checks.log`；这不替代新编号的真实数据库部署或合并后原生运行。
- 前述 Worker 6/6、告警 1/1 和 JS 四项预算对应合并前已冻结输入；远端还改变了 AI、Identity 与消息路径，不能把旧二进制/前端产物视为合并后完整闭包证据。API 默认发布补跑进入 ILC 后因本次同步主动结束（保留 `api-after-vm-restart-publish.log` 与控制日志），不是新的编译器访问冲突或通过结果；合并后的 API/Worker 原生发布与相应运行仍须按实际变更范围另验。
- 终止后控制脚本 finally 已恢复 SQL Server/MySQL 原容器，Redis 仍在运行；新世代 VM PID 27216 的进程及全部 92 线程均读回原始掩码 4294967295，证据 `test-cache-restored.json`、`vm-final-restored.json`。未重新检测 CPU，未删除失败记录或共享数据库。
#### 独立保留的验收边界与资料缺口

- **真实厂商：** SMTP、阿里云短信、钉钉、企微、小程序的真实账户认证、发送及厂商实际支持的回执/对账尚未验证。需要相应授权配置、审核通过的模板/签名（适用时）、专用测试收件人/应用身份及回调接入资料；Secret 使用配置引用，不写入仓库、文档或日志。SMTP/企微/小程序没有可信送达回执的边界继续保留，调用成功不升级为可信送达。
- **外部告警接收：** 本轮验证数据库采样 → Collector → Prometheus 规则触发与恢复；没有配置外部 Alertmanager 接收端，不宣称已向真实运维渠道发出或送达告警。
- **外部原报告：** `G:/wwwroot/github_fork/Full.NET.DOC/2026-10-15-notifications-module-gap-analysis.md` 仍不可访问，本机未找到原件，不能逐条回写或宣称其全部结论已复核；需要可访问的原件或完整内容。本计划保留已有历史审查与本轮实施证据。
- **容量及本机条件：** 未运行代表性容量矩阵，继续 `Capacity-not-verified`，不推导 10K、最大吞吐或 SLO。按用户要求不再检测 CPU；已知本机 CPU 异常，原生发布/运行证据限定在获批的稳定核心条件下。
- **PDF 独立质量记录：** 首轮 MySQL 原生 PDF 恢复一次进入 failed；补充失败码诊断后，同一旧原生产物补跑通过，未确认首次失败根因，不能宣称通过重试修复了产品缺陷。原始失败 TRX/日志保留，最终新产物也纳入双库 PDF 恢复补验；它与 Notifications 凭据缺口分别记录。
