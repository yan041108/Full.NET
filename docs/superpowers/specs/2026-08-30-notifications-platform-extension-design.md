# Notifications 统一消息平台扩展设计规格

**状态：** Approved（2026-08-30，经当前授权用户审查批准）

**批准基线：** `main@007de9aa91dd6a31788b12a829d07b63aeab1e7a`

**适用范围：** 现有 `Full.NET.Modules.Notifications` 的 Host/Tenant 收件箱、公告、模板、外部渠道、Provider Profile、路由、投递、回执和用户偏好

**批准依据：** [`2026-08-30-unified-notifications-workflow-design-assessment.md`](../../verification/2026-08-30-unified-notifications-workflow-design-assessment.md)
**实施计划：** [`2026-08-30-notifications-platform-extension.md`](../plans/2026-08-30-notifications-platform-extension.md)

## 1. 决策摘要

统一消息中心是独立平台模块，不以 Workflow 是否实现为前提。现有公告、Host 站内信、未读/已读、SignalR 与 Outbox 修复链路继续作为内建基础能力；新增范围是 Tenant 收件箱、不可变模板版本、逻辑通知、接收人解析、外部渠道投递、多个 Provider Profile、显式场景绑定、回执、偏好、重试、对账和运维控制面。

公告和站内信属于模块内建渠道。短信、邮件、企业微信、微信公众号、钉钉及以后新增渠道属于可选 Provider。业务模块提交“发送什么、给谁、业务场景和幂等键”，不得直接选择厂商 SDK、读取 Provider Secret 或把多个 Active Profile 等同于自动群发。

本 Spec 批准架构和交付门禁，不宣称外部 Provider 已实现。首个真实外部 Provider 必须在对应任务启动前明确厂商/协议、凭据来源、测试环境、费用/频控和回执能力。

## 2. 已批准的长期决策

1. 数据链为 `Intent 1 → Recipient N → Inbox 0..1 / Delivery N → Attempt 1..N`；一次用户通知与一次渠道尝试不是同一事实。
2. 模块内建 `Announcement` 与 `Inbox`；外部渠道通过闭合 `ProviderTypeCatalog` 和显式 Adapter 接入。
3. 同一 ProviderType 可创建多套 Host/Tenant Profile，可启用多套；是否使用由 Producer/Scene Binding 决定，不能仅凭 `IsActive` 自动发送。
4. 首版路由模式为 `Single / FanOut / Failover / Match`。`FanOut` 必须显式批准；`Failover` 只在可比较失败类别和幂等边界内切换。
5. Profile Secret 只保存部署 Secret Reference；数据库与管理 API 不接收、不回显明文 Secret。直接托管 Secret 需要独立安全 ADR。
6. Host 管理 Profile 默认不被租户继承。只有 Profile 标记可共享且 Binding 显式列出允许租户/范围时可使用；租户管理员看不到 Host Secret 或其他租户配置。
7. 普通可操作通知首版只提供登录后深链；免登录一键审批、验证码、登录挑战和支付验证不进入通用模板能力。
8. 通知政策优先级固定为强制业务/合规政策 → 租户场景政策 → 同意与静默时段 → 用户偏好 → 成本/降级；用户偏好不能关闭安全、合规或交易强制通知。
9. 通知和 Workflow Todo 不合表；统一工作台在 Query/UI 层组合。
10. 数据库为未读数权威；SignalR 只提示客户端刷新。缓存必须可由数据库重建，不用 `HINCRBY` 作为唯一事实。

## 3. 目标与非目标

### 3.1 目标

- 保持现有 Host 公告、站内信和 Realtime API 兼容，并纵向扩展到受信 Tenant 作用域。
- 模板 Draft/Published version、参数 Schema、发布审计和版本固定绑定。
- 逻辑 Intent、接收人、内建 Inbox、外部 Delivery/Attempt、回执和最终状态分层。
- 可配置多套 Provider Profile，并按业务模块、Producer、Scene、Channel 和作用域显式绑定。
- Secret Reference、租户隔离、精确权限、内容分级、脱敏、频控、配额和回执验签。
- 租约 Worker、有界重试、退避/抖动、死信、重放、对账和低基数可观测性。
- SQL Server/MySQL、Host.Api/Worker Native AOT 和真实 Provider 沙箱验证。

### 3.2 非目标

- 同时交付所有短信、邮件和企业 IM 厂商。
- 承诺“必达”、恰好一次、外部已读或跨厂商完全相同的状态语义。
- 允许业务模块提交任意 URL、Header、脚本、SDK 参数或 Secret。
- 把验证码、认证挑战、支付验证的生成、有效期、尝试次数和验证状态迁入 Notifications。
- 让外部渠道回执直接触发 Workflow approve/reject 或其他业务状态变更。
- 在没有真实 Provider 前创建空 Adapter 项目、伪回执或无意义通用接口层。

## 4. 模块边界

| 参与者 | 边界 |
| --- | --- |
| 业务 Producer | 提交稳定 ProducerKey、SceneKey、TemplateKey/Version、Recipient、参数和 IdempotencyKey；不选择 Secret |
| Identity / Organization | 通过最小批量 Port 解析并验证用户、联系方式、租户和状态；Notifications 不查其表 |
| Settings | 不承载 Notifications 领域参数；Provider Profile 与路由由 Notifications 自有强类型表拥有 |
| Files | 附件和媒体只保存受控 FileId；投递前重新验证权限、租户、状态和 Provider 限制 |
| Messaging | 需要跨模块可靠交付的 Intent/业务事件走现有 Outbox/Inbox；Notifications 内部 Delivery Worker 使用自有任务表 |
| Realtime | 数据库提交后发布低延迟提示；失败由现有 Outbox 修复路径和客户端权威重读收敛 |
| Workflow | 只发布待办提醒事实；Notifications 不读取或更新 Workflow 表，也不执行审批动作 |

Notifications 生产 SQL 只访问 `fn_notifications_*`。所有外部 I/O 必须发生在数据库事务外。

## 5. 领域模型

| 表 | 用途与关键约束 |
| --- | --- |
| 现有公告/收件箱表 | 保持兼容，逐步补可信 Scope 与扩展引用；禁止重建第二套内建 Inbox |
| `fn_notifications_template` | 稳定 TemplateKey、Scope、Channel/内容类别和 Draft 指针 |
| `fn_notifications_template_version` | 不可变主题/正文/参数 Schema、内容分级、Hash 和发布审计 |
| `fn_notifications_intent` | ProducerKey、SceneKey、业务幂等键、模板版本、路由快照和状态 |
| `fn_notifications_recipient` | Intent 的受信接收主体、地址快照摘要和解析状态 |
| `fn_notifications_delivery` | Recipient + Channel + ProviderProfileVersion 的一次逻辑投递及终态 |
| `fn_notifications_delivery_attempt` | 每次 Provider 调用、租约、失败类别、回执摘要和时间；追加式 |
| `fn_notifications_provider_profile` | ProfileKey、ProviderTypeKey、Scope、非 Secret 配置、SecretReference、启用状态和修订 |
| `fn_notifications_provider_profile_version` | 不可变配置快照、AdapterVersion、Hash 和发布审计 |
| `fn_notifications_binding` | 稳定 BindingKey、Scope、Draft 和最新发布版本指针 |
| `fn_notifications_binding_version` | Producer/Scene/Channel/Scope 到 ProfileVersion/路由模式的不可变显式绑定 |
| `fn_notifications_preference` | 用户可配置渠道、静默时段和营销同意；不覆盖强制政策 |
| `fn_notifications_recipient_endpoint` | Provider Profile 命名空间内的 OpenId/外部 UserId 等端点、验证状态和受保护值；手机号/邮箱仍从 Identity 权威目录解析 |
| `fn_notifications_receipt` | 验签后的去重回执、外部状态、原始载荷摘要和处理状态 |
| `fn_notifications_domain_audit` | 模块自有 B0 领域审计；Profile/Binding 发布、启停、重试、排空和死信处置同事务写入 |

所有新表使用应用端 UUID v7、PascalCase 列与显式 Dapper SQL。唯一业务幂等键为 `(Scope, ProducerKey, IdempotencyKey)`；Provider 回执 Id 只用于回执去重，不能代替业务幂等键。

Provider 专属 RecipientEndpoint 按 Scope、UserId、ProviderProfileVersion/命名空间隔离，原值使用批准的数据保护方案保存，查询/日志/管理端只返回掩码与验证状态；禁止用加密值、手机号、邮箱或 OpenId 作为日志/指标标签。

## 6. 模板与内容安全

- Template Draft 可变；Publish 产生不可变 TemplateVersion，Intent 固定绑定版本和渲染参数快照。
- 参数 Schema 使用闭合类型和显式最大长度/集合上限；未知参数、缺失必填、类型不匹配和超限在创建 Intent 时失败关闭。
- 不执行任意 JavaScript、表达式注入或远程模板。富文本只有复用现有服务端净化边界并通过具体渠道兼容测试后才能开放。
- 内容分级决定是否允许进入主题、正文、通知预览、日志、追踪和外部渠道。Secret、令牌、完整表单和 S2 数据不得写入日志、指标、Integration Event 或回执摘要。
- 附件必须声明渠道大小/类型限制和过期语义；不得把 Base64、磁盘路径或任意 URL 写入模板参数。

## 7. Provider Profile 与多套参数

`ProviderTypeCatalog` 是代码拥有的闭合目录，至少声明 `ProviderTypeKey`、AdapterVersion、支持渠道、配置 Schema、Secret 字段、能力、回执模式和 Native AOT 状态。禁止通过反射扫描未知程序集或从数据库下载执行代码。

Profile 管理遵守以下不变量：

- 同一 ProviderType 可创建任意多套命名 Profile；ProfileKey 在作用域内稳定唯一。
- `Enabled` 只表示 Profile 可被选择，不代表所有消息自动使用。
- 非 Secret 参数可版本化存储；Secret 只保存 Reference，读取 API 仅返回 `configured/not-configured` 等状态。
- Binding 明确 ProducerKey、SceneKey、Channel、路由模式、Profile 优先级、适用条件和有效期。发布 Binding 时验证引用 Profile 均 Enabled、作用域兼容且能力匹配。
- Intent 创建时固定 BindingVersion 与 ProviderProfileVersion，避免在途消息因配置修改漂移。
- Profile 禁用阻止新 Intent 选择；是否停止、排空或转移在途 Delivery 必须由显式运维动作决定并审计。

## 8. 路由与状态语义

| 模式 | 语义 |
| --- | --- |
| `Single` | 选择一个确定 Profile；不可用则失败或进入人工处置 |
| `FanOut` | 向显式列出的多个 Profile/渠道分别创建 Delivery；每条独立追踪，禁止隐式多发 |
| `Failover` | 按稳定顺序尝试下一个 Profile；只对允许的瞬时/能力失败切换，永久内容错误不得换厂商重试 |
| `Match` | 用受控声明式条件选择唯一 Profile；多个命中或无命中的行为在 Binding 中明确 |

状态至少区分 `Persisted / Accepted / Sent / Delivered / Unknown / Read / Failed / Suppressed / DeadLettered`。内建 Inbox 可证明 Persisted 与用户 Read；外部 Provider 只有在可信回执支持时才能标记 Delivered。超时或无回执保持 Unknown，不能推断送达。

回执先验签、校验时间窗和去重，再更新 Delivery；状态只能按允许的单调图推进，乱序回执不能把终态回退。外部回调不得直接调用业务模块或 Workflow 动作。

## 9. 偏好、合规与深链

- Producer/Scene Catalog 声明 `Mandatory / Transactional / Informational / Marketing` 类别、允许渠道和降级策略。
- 安全/合规强制消息可绕过普通关闭偏好，但仍受合法渠道、费用和内容政策限制；营销消息必须有明确同意，路由不能强行开启。
- 静默时段只延迟允许延迟的 Delivery；紧急强制场景必须在 Catalog 中显式声明并审计。
- 首版可操作通知仅携带登录后深链和不可猜测资源 Id；进入目标页面后重新校验会话、租户、权限、资源归属和当前状态。

## 10. Worker、重试、频控与恢复

- Delivery Worker 使用有界 BatchSize、Poll、并发、租约和 Provider 级限流；满批立即继续，未满才等待。
- SQL Server 采用等价非阻塞领取语义，MySQL 8 在短事务内使用 `FOR UPDATE SKIP LOCKED`；领取、续租、成功/失败终态保持一致锁顺序。
- Attempt 先取得租约再在事务外调用 Provider，调用结果用 ExpectedLeaseGeneration/Revision 提交；崩溃可导致至少一次调用，Adapter 必须使用业务幂等键或回执对账。
- 失败分类为瞬时、频控、认证/配置、内容永久、收件人永久、未知。只有瞬时和明确频控错误进入有界指数退避；永久错误直接终止或死信。
- Provider、租户、Producer 和 Scene 均可配置有界配额；未知或缺失配置失败关闭，不允许无界并发。
- 重放、强制重试、切换 Profile、排空和死信解除均需独立权限、理由和 B0 审计。

## 11. 权限与管理 API

| 权限码 | 页面/操作 |
| --- | --- |
| `notifications.templates.read` | 模板页面 |
| `notifications.templates.create` | 新建模板 |
| `notifications.templates.update` | 编辑模板 Draft |
| `notifications.templates.publish` | 发布模板版本 |
| `notifications.provider_profiles.read` | Provider Profile 页面 |
| `notifications.provider_profiles.create` | 新建 Profile |
| `notifications.provider_profiles.update` | 编辑 Profile 非 Secret 配置/Secret Reference |
| `notifications.provider_profiles.publish` | 发布 Profile 版本 |
| `notifications.provider_profiles.enable` | 启用 Profile |
| `notifications.provider_profiles.disable` | 禁用 Profile |
| `notifications.bindings.read` | 场景绑定页面 |
| `notifications.bindings.create` | 新建绑定 |
| `notifications.bindings.update` | 编辑绑定 Draft |
| `notifications.bindings.publish` | 发布绑定版本 |
| `notifications.deliveries.read` | 投递与 Attempt 只读控制面 |
| `notifications.deliveries.retry` | 人工重试 |
| `notifications.deliveries.dead_letter` | 死信处置 |
| `notifications.preferences.read/update` | 本人偏好；管理员代管需要独立高权限码 |

实际实现时每个权限必须作为独立稳定目录项，不把斜杠组合文本直接当作单个权限。现有公告/Inbox 权限保持兼容；新增页面只能进入 `ui/admin`，不修改冻结 Layui。

管理 API 基路径继续位于 `/api/v1/notifications`，使用标准状态码、ProblemDetails、稳定 operationId 和 System.Text.Json 源生成。Profile API 不接受明文 Secret；回执 Endpoint 使用 Provider 专用路径、请求大小上限、原始 Body 验签和独立限流。

## 12. 双库、Native AOT、可观测性与容量

- 新表与索引成对迁移，验证部分 DDL 恢复、租约并发、回执去重、业务幂等和乱序状态；不得使用仅一库可用的 `ON CONFLICT` 假设。
- Provider Adapter 必须证明许可证、SDK 生命周期、超时/取消、Proxy、TLS、Native AOT 静态闭包和发布物 native binding。无法闭合时隔离到明确非 AOT 的 Worker 适配边界并记录状态，不能污染 Host.Api。
- 指标使用稳定 ProviderTypeKey、Channel、Scene 类别、结果和错误类别；禁止 ProfileKey、租户、用户、手机号、邮箱、外部消息 Id 或异常正文等高基数/敏感标签。
- 记录 Intent/Delivery backlog、oldest age、attempt rate、success/failure/unknown、Provider latency P50/P95/P99、频控、死信、回执延迟和 reconcile 差异。
- 开发阶段只验证边界与轻量回归；生产等价负载、厂商配额和成本认证前保持 `Capacity-not-verified`。

## 13. 分阶段交付

1. Tenant Inbox 与作用域：在保留 Host API 兼容的前提下建立可信 Tenant 数据、权限、未读数和 SignalR 组语义。
2. 模板与逻辑通知：Template/Version、Intent/Recipient、参数 Schema、策略与内建 Inbox 闭环。
3. Profile 与 Binding 控制面：多套配置、Secret Reference、版本快照、Single/FanOut/Failover/Match 和精确权限。
4. 首个外部 Provider：只选择一个真实 Provider，完成沙箱、回执、双库、AOT/隔离、限速、死信和对账。
5. 用户偏好与多 Provider：在真实政策和费用边界确认后开放偏好、静默时段、显式多发与故障转移。

每阶段必须形成独立可验证纵向切片。计划或 Spec 存在不代表实现；现有 Notifications 继续保持其已验证范围，新扩展能力保持 `Planned`，直到对应证据完成。

## 14. 阶段启动门禁

- 首个外部 Provider 的厂商/协议、凭据、沙箱、费用、频控、回执和许可证未明确时，不开始 Provider 实现。
- Producer/Scene 没有真实业务消费者时，不创建通用 Extras、占位事件或 Provider Profile。
- 共享 Host Profile 的允许租户范围、费用承担和管理员可见性必须在启用共享前配置并验证；默认不共享。
- 免登录动作、认证验证码、支付验证、营销跨渠道和 S2 内容需独立安全/合规批准，本 Spec 不自动授权。

## 15. 参考

- [批准评估](../../verification/2026-08-30-unified-notifications-workflow-design-assessment.md)
- [现有 Notifications 能力状态](../../roadmap/capability-status.md)
- [ADR-0002 模块化单体演进](../../architecture/adr/ADR-0002-modular-monolith-evolution.md)
- [ADR-0006 事务 Outbox/CDC/Kafka](../../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)
- [ADR-0008 API Native AOT](../../architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md)
