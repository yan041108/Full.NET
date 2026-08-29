# 统一消息中心与 Workflow（审批流）设计调研评估

- **状态：** 审查通过；2026-08-30 经当前授权用户批准，决策已同步至对应 Spec 与实施计划
- **日期：** 2026-08-30
- **代码基线：** `main@007de9aa91dd6a31788b12a829d07b63aeab1e7a`
- **任务快照：** `unified-message-workflow-research-20260830`
- **输入材料：** 用户提供的《统一消息中心 & 工作流（审批流）功能设计说明书 v1.0》；废弃项目 `G:\wwwroot\github_fork\Admin.NET.Pro.V2.1.AI-master`
- **仓库依据：** 现有 Notifications、Messaging、Jobs、Realtime 能力；批准前的 Workflow Spec 与首切片计划；ADR-0002、ADR-0006、ADR-0008；Full.NET 项目规则
- **方法：** 仓库静态盘点、旧项目与上游源代码差异审计、现有设计交叉复核、主流引擎官方资料对比、安全与 Native AOT 边界复核
- **批准产物：** [Workflow Spec](../superpowers/specs/2026-08-20-workflow-module-design.md)、[Notifications 扩展 Spec](../superpowers/specs/2026-08-30-notifications-platform-extension-design.md)、[Workflow 核心首切片](../superpowers/plans/2026-08-20-workflow-first-vertical-slice.md)、[设计器/跨端表单计划](../superpowers/plans/2026-08-30-workflow-designer-form-runtime.md)、[Notifications 平台扩展计划](../superpowers/plans/2026-08-30-notifications-platform-extension.md)

## 1. 结论

输入材料的总体方向正确，但不能原样成为 Full.NET 的实现规格。建议保留“业务、流程、通知分离”“发布版本不可变”“事务 Outbox”“审批动作结构化留痕”“会签单独建模”等原则，并按 Full.NET 现有边界做以下修订：

1. **不是从零建设消息中心。** Full.NET 已有 Host 级公告、站内信、未读/已读、SignalR、事务 Outbox 修复链路和 Vue 页面；新增范围应是租户化、模板版本、逻辑通知、外部渠道投递与用户偏好，不能重复建设现有 Inbox/Realtime。
2. **Workflow 首版采用 Full.NET 自有审批领域内核。** 保持单主项目、Dapper 双库、不可变定义版本、显式状态机和 Worker/Jobs 恢复；不在首版引入 Elsa、Workflow Core、Flowable、Camunda、Dapr Workflow 或 Temporal。
3. **不先造通用“引擎抽象层”。** 在没有第二个真实运行时消费者前，只保留稳定的业务命令、查询和事件契约；第三方引擎适配必须通过独立 PoC、许可证、双库、恢复、Native AOT 与运维门禁。
4. **消息与待办不合表。** Workflow Todo 是可办理工作的权威状态，Notifications Inbox 是提醒与阅读状态；“统一工作台”在查询/UI 层组合，两者不得通过跨模块 JOIN 或共享事务耦合。
5. **通知不是“必达”的同义词。** 站内信只能保证已持久化并可读取；外部渠道必须区分已受理、已发送、已送达、未知与已读，不能用单一 `SENT` 冒充送达。
6. **Workflow Spec 已按本评估修订并批准。** 批准只确立长期契约与可执行计划，不代表功能已经实现；Workflow 状态保持 Designing/Planned，必须通过首切片验证后才能晋级。
7. **Notifications 是独立平台模块。** 公告和站内信是模块内建渠道；短信、邮件、企业微信、公众号、钉钉等是可选 Provider。Workflow 只是发送方之一，不是 Notifications 的建设前置条件。
8. **旧项目的 Workflow-Vue3 改造成果确定作为工作流设计器基础，但不能作为运行时或协议基线。** 项目所有者已确认该开源项目可使用并已取得作者允许，来源许可不再阻塞方案；交付时仍须把授权凭据、上游提交、本地修改范围和第三方声明归档。现有前后端定义格式不兼容、复杂节点多数没有执行语义，因此只能在 Full.NET 自有、强类型、可编译的定义模型之上升级。
9. **Workflow 采用旧 Admin.NET 已集成的 VForm3 作为后台表单设计器和受控 Web 适配器，H5/uni-app 不直接携带 VForm3。** 旧项目依赖实际为 `vform3-builds@^3.0.10`，流程定义页接入了 `v-form-designer`，任务办理页却仍让用户手填结果 JSON，服务端也未执行表单 Schema、字段权限或数据校验。Full.NET 必须补齐不可变表单版本、流程版本绑定、服务端编译校验、节点字段策略和运行时渲染闭环；移动端基于同一权威 `WorkflowFormSchema` 自研轻量渲染器，避免把 Element Plus 和 VForm3 设计态能力带入 H5、微信小程序或支付宝小程序包。

## 2. 当前仓库事实

| 能力 | 当前事实 | 本次调研含义 |
| --- | --- | --- |
| Notifications | `Build-verified`；已有公告、Host 站内信、未读/已读、SignalR 和 Vue 页面 | 保留现表与 API 兼容；未来纵向扩展，不重建第二套消息中心 |
| Realtime | 数据库事实提交后低延迟推送，Outbox 负责修复；客户端重新读取权威未读数 | SignalR 是提示通道，不是事务日志，也不承担送达证明 |
| Messaging | 已批准事务 Outbox → CDC Relay/Kafka → Inbox 的至少一次交付路径 | Workflow 结果和通知消费复用现路径，不新增 RocketMQ 事务消息分支 |
| Jobs | 已有数据库任务、租约、重试与 Worker | Jobs 只能触发 Workflow 公开命令，不能直接更新 `fn_workflow_*` |
| Workflow | 审查开始时已有待审 Spec 和不可执行 Draft plan；无运行时代码 | 本次已修订并批准既有事实源，没有创建竞争性第二份 Workflow Spec |
| Native AOT | Host.Api 已达到 `Aot-published`，Worker 正在分阶段闭合 | 新依赖、动态表达式、反射物化和 Provider native binding 都必须独立证明 |

现有 Notifications 仍是 **Host 作用域**，虽然表含 `TenantId`，生产 SQL 和授权均按 Host-only 运行。租户内审批提醒不能在设计上假设已经可用。

## 3. 对输入材料的吸收与修正

### 3.1 可直接吸收

- 业务单据、流程运行时、通知投递各自拥有数据；禁止互相直写表。
- 流程定义草稿可变，发布版本不可变，运行实例固定绑定启动时版本。
- 重要业务状态与 Integration Event 在同一本地事务写入 Outbox。
- 用户可见消息与外部渠道投递状态分离。
- 审批动作、转办、加签、驳回原因使用结构化记录，不靠字符串拼接审计历史。
- 会签、或签、`N_OF_M` 需要明确完成条件、并发控制、幂等与重算能力。
- 外部渠道调用必须异步、有界重试、死信/人工处置和可观测性。

### 3.2 必须修正

| 输入材料观点 | 问题 | Full.NET 修正 |
| --- | --- | --- |
| `MessageRequest 1 → UserMessage 1 → DeliveryTask N` | 一次通知可有多个接收人；一次渠道投递也可能有多次尝试 | `Intent 1 → Recipient N → Inbox 0..1 / Delivery N → Attempt 1..N` |
| 用户偏好最高，覆盖全部路由 | 安全、合规、交易通知不能被普通偏好关闭；营销同意也不能被路由强开 | 强制业务政策/合规 → 租户场景政策 → 同意与静默时段 → 用户偏好 → 成本/降级 |
| 站内信兜底“必达” | 持久化、实时展示、用户打开是三个不同事实 | 使用 `Persisted/Accepted/Delivered/Read` 分层语义，站内信只承诺权威可读 |
| Redis `HINCRBY` 作为未读数 | 重放、撤回、补偿和并发可能造成漂移 | 数据库为权威；可选缓存必须可按数据库重建，实时事件只触发刷新 |
| `(channel, ext_id)` 唯一且 `ON CONFLICT` | `ext_id` 常是服务商回执，不是业务幂等；SQL 不具备双库通用性 | 唯一键使用 `(TenantId, ProducerKey, IdempotencyKey)`；双库分别实现等价 SQL |
| 通知“必须走 MQ” | Full.NET 已有批准的 Outbox/CDC/Kafka 路径；模块内部投递任务不必再绕一层 Broker | 跨模块可靠事实走现有 Outbox；渠道任务由 Notifications 自有表和租约 Worker 处理 |
| 业务表必须保存 `process_instance_id` | 可能诱导跨模块外键、共享事务和强耦合 | 业务模块可保存稳定关联快照但无 FK；事务创建场景优先用 StartRequested/Started 事件收敛 |
| Workflow 与 Notifications 绝不互调 | 绝对化；同进程即时权威读取允许最小 Contract Port | 通知提醒采用事件；Workflow 启动/即时查询可按用例使用最小同步 Port，禁止跨模块事务 |
| 消息中心回调直接调用 `approve` | 服务商回调只证明渠道回执，不能代表审批人身份与当前资源权限 | 回执只更新 Delivery；审批动作必须进入 Workflow 授权端点并再次校验任务归属、租户和状态 |
| Adapter 与 Strategy 二选一 | GoF 名称不是架构不变量，容易把实现细节写成合同 | Provider Adapter 统一异构 SDK；Route Policy 负责选择，两者可以同时存在 |
| 1–4 周完成全部阶段 | 未基于迁移、双库、AOT、真实栈与安全门禁估算 | 使用能力门禁和停止条件，不在评估阶段承诺日历周期 |

验证码、登录挑战和支付验证不应直接降级为普通 Notifications 模板。它们的生成、有效期、尝试次数和验证状态仍由 Identity/支付领域拥有；Notifications 只可复用经批准的渠道 Provider。

## 4. 推荐目标边界

```text
业务模块
  ├─ 本地事务：业务状态 + B0 Domain Audit + Outbox(StartRequested/BusinessChanged)
  └──────────────────────────────┐
                                 v
Workflow（定义、实例、步骤、待办、审批动作、恢复）
  ├─ 本地事务：状态迁移 + 执行历史 + B0 Audit + Outbox
  ├─ 结果事件 ───────────────────────────────┐
  └─ 提醒事件 ───────────────┐               │
                             v               v
Notifications（意图、模板、收件箱、路由、渠道任务、回执）   业务模块 Inbox 消费
  ├─ 数据库权威 Inbox
  ├─ 提交后 SignalR 快速提示 + Outbox 修复
  └─ 租约 Worker → Provider Adapter → 外部渠道/回执
```

### 4.1 数据所有权

- Workflow 只读写 `fn_workflow_*`；Notifications 只读写 `fn_notifications_*`。
- Workflow 的办理人解析使用消费方定义的最小用户/组织目录 Port，禁止查询 Identity/Organization 表。
- Notifications 的接收人可达性使用批量目录/投影，禁止发送列表逐行同步反查。
- 业务详情链接只保存稳定业务类型、业务键和受信路由键；不保存任意外部 URL。
- 统一工作台可以并行调用 Workflow Todo 与 Notifications Inbox API，或由消费方本地投影组合；禁止数据库跨模块 JOIN。

### 4.2 事务与一致性

- 审批动作、任务关闭、步骤/实例推进、执行历史、B0 审计和必要 Outbox 在 Workflow 同一事务提交。
- Notifications 消费 Workflow 事件时，以消息 Id/业务幂等键在自己的事务中创建通知事实。
- 外部渠道调用永不进入数据库事务；Delivery 使用租约、幂等键和至少一次尝试语义。
- SignalR 只发布已经提交的事实；直接发布失败不回滚业务，Outbox 消费负责最终刷新。
- 普通 HTTP Operation Log、渠道诊断日志和打开率指标不写 Outbox。

## 5. Notifications 增量设计

### 5.1 推荐逻辑模型

下列表名只是评估级候选，实施前仍需 Naming Profile、迁移号和双库恢复审查。

| 候选实体 | 责任 |
| --- | --- |
| `fn_notifications_template` | 模板稳定键、作用域、状态和最新发布版本 |
| `fn_notifications_template_version` | 不可变的渠道/语言变体、参数 Schema、内容哈希和发布审计 |
| `fn_notifications_intent` | 生产者的一次通知意图、业务幂等键、场景、优先级和关联业务摘要 |
| `fn_notifications_recipient` | 每个接收人的作用域、用户标识、地址快照和个性化参数摘要 |
| 现有 `fn_notifications_inbox_message` | 用户站内可见事实；扩展 Intent/业务关联、归档与撤回语义时保持兼容 |
| `fn_notifications_delivery` | 一个接收人通过一个渠道的投递状态、下一尝试时间、租约和终态 |
| `fn_notifications_delivery_attempt` | 每次 Provider 调用、响应分类、回执、耗时、错误码和重试判断 |
| `fn_notifications_preference` | 用户在允许范围内的渠道、静默时段和营销同意 |
| `fn_notifications_provider_profile` | 一套可独立启停、轮换和审计的外部平台账号/应用参数实例 |
| `fn_notifications_provider_profile_version` | 不可变的非 Secret 配置、SecretReference、AdapterVersion 与 Hash；Intent/Delivery 固定绑定 |
| `fn_notifications_binding` / `fn_notifications_binding_version` | Notifications 所有的强类型场景政策与不可变版本；绑定 Producer/Scene、渠道、路由模式和一个或多个 ProfileVersion，不放通用 Settings 字符串仓库 |
| `fn_notifications_recipient_endpoint` | Provider Profile 命名空间下的 OpenId、外部 UserId 等受保护端点；手机号/邮箱仍由 Identity 权威目录解析并按发送需要形成地址快照 |
| `fn_notifications_receipt` | 验签、去重后的外部回执与处理状态；乱序回执不得回退 Delivery 终态 |
| `fn_notifications_domain_audit` | Profile/Binding 发布、启停、重试、排空和死信处置的模块自有 B0 领域审计 |

所有主键使用应用端 UUID v7；Tenant/Host 作用域、稳定机器码、时间 UTC、乐观并发和游标排序必须显式。Provider 密钥不得保存在 `channel_config.ak/sk` 普通列中；数据库只保存 ProviderKey、公开元数据和 Secret Reference，真实密钥由部署 Secret 注入。

### 5.2 状态语义

| 对象 | 推荐状态 |
| --- | --- |
| Inbox | `Unread / Read / Archived / Revoked` |
| Delivery | `Pending / Leased / Accepted / Delivered / FailedTransient / FailedPermanent / Unknown / Cancelled` |
| Attempt | 追加式；记录 Provider 接受、拒绝、超时、回执与主动查询结果，不覆盖历史 |

“撤回”只能停止尚未开始的投递、在 UI 隐藏/标记站内信，并保留审计；无法承诺收回已发送的短信或邮件。Provider 回执是外部不可信输入，必须校验签名、时间窗、事件幂等和允许的单调状态迁移。

### 5.3 模板与路由

- 模板采用 Draft → Published immutable version；发送时固定具体版本并保存渲染快照/摘要，避免运营修改历史内容。
- 语言使用规范 BCP 47 标签；模板参数使用有界、版本化 Schema，不接受任意 `object` 或业务传入成品 HTML。
- 首版使用受限占位符，不支持 C#/JavaScript/Python 等动态执行；HTML 仍需服务端净化。
- 路由输出不是简单渠道列表，而是带并行/串行、延迟、最大尝试、终止条件和静默策略的执行计划。
- 频控、预算和黑名单是路由约束；不得让 Redis 成为不可恢复的唯一业务事实。

### 5.4 渠道、Provider 与配置实例必须分层

“渠道”和“渠道配置”不是同一个概念。推荐五层模型：

| 层 | 示例 | 是否稳定代码目录 |
| --- | --- | --- |
| `ChannelCode` | `announcement`、`inbox`、`sms`、`email`、`wecom`、`wechat_official`、`dingtalk` | 是；表达用户能感知的媒介 |
| `ProviderTypeKey` | `internal.announcement`、`internal.inbox`、`smtp`、`microsoft_graph`、`aliyun_sms`、`tencent_sms`、`wecom_app`、`wechat_official_account`、`dingtalk_app` | 是；表达具体实现与配置 Schema |
| `ProviderProfile` | `host.email.finance`、`tenant-a.wecom.hr`、`tenant-a.sms.primary` | 否；管理员创建的参数实例，可有多套 |
| `ProducerApplicationKey` | `workflow`、`identity.security`、`document`、`notifications.console` | 是；表达哪个业务应用有权发哪些场景 |
| `ApplicationBinding` | `workflow + task_assigned → inbox + tenant-a.wecom.hr` | 否；表达业务场景使用哪些 Profile 及编排方式 |

必须区分两种容易混淆的“应用”：

1. **业务发送方应用**由模块在代码目录中注册稳定 Key、场景、参数 Schema 和允许的消息分类。未知 Producer/Scene 失败关闭，业务调用只提交 `ProducerApplicationKey + SceneKey + Recipient + IdempotencyKey + typed parameters`。
2. **外部平台应用实例**通常对应一个独立安全/身份命名空间，例如一个企业微信 Agent、一个公众号、一个钉钉应用或一个发件邮箱。即使凭据部分相同，也建议每个外部 App/Agent 建一个 Profile，便于授权、路由、轮换和审计。

业务模块不得提交任意 `ProviderProfileId`、AppId、AgentId 或 Secret 来绕过路由。只有受控的管理测试 Endpoint 可以显式选择 Profile；正常发送由 Notifications 根据绑定解析。

### 5.5 多套配置与多应用绑定

`ProducerApplication ↔ ProviderProfile` 是多对多关系：

- 一个模块应用可同时绑定 Inbox、邮件和多个企业 IM Profile。
- 一个 Profile 可被多个模块应用复用，但必须通过显式 Binding 授权，不能因为 Profile 已激活就允许所有模块使用。
- 同一渠道可配置多套 Profile，例如财务与人事使用不同企业微信 Agent，不同租户使用不同公众号，短信使用主/备厂商。
- 接收地址必须带 Profile 命名空间。公众号 OpenId、企业微信/钉钉 UserId 不能当作跨 App 通用用户标识；建议唯一边界为 `(TenantId, UserId, ProviderProfileId, EndpointKind)`。

一个 Binding 至少需要：作用域、ProducerApplicationKey、SceneKey、ChannelCode、ProviderProfileId（内部渠道可空）、TemplateBinding、优先级、DispatchMode、启用状态、生效时间和版本。Profile 与 Binding 均不可物理删除已有历史引用，只能 Disable/Retire。

Profile 和 Binding 都必须显式区分 Host/Tenant 作用域。租户路由优先精确匹配租户自有 Binding；只有 Host 管理员把某个 Profile 标记为“可共享”且租户显式采用时，才允许回退到 Host Profile。租户管理员只能看到共享能力、公开身份摘要、配额和费用归属，不能读取 Host Secret Reference 或其他租户配置。未命中合法 Binding 时失败关闭，不能静默使用任意“默认账号”。

多个 Profile “同时启用”不能自动解释为“全部发送”，必须显式选择编排语义：

| `DispatchMode` | 语义 | 适用场景 |
| --- | --- | --- |
| `Single` | 只选择一个确定 Profile | 模块固定使用某个品牌/应用 |
| `FanOut` | 所有匹配 Profile 都生成 Delivery | 确需多应用同时通知，允许用户收到多份 |
| `Failover` | 按序尝试主备 Profile | 短信/邮件等语义等价 Provider；超时未知时先查询，避免双发 |
| `Match` | 按租户、组织、品牌或接收地址命中一个 Profile | 财务/人事 Agent 分流、多公众号 OpenId 命名空间 |

加权/轮询只适合经过容量和重复风险验证的等价 Provider，首版不实现。因“用户尚未阅读”而升级到另一渠道属于 Reminder/Escalation Policy，不是 Provider 失败降级。

路由在 Intent 物化时固定 `PolicyVersion + BindingId + ProviderProfileId/ProfileVersion + TemplateVersion`。普通重试不得重新读取最新路由，否则管理员改配置后可能把同一消息发到新旧两套应用；显式人工 Reroute 必须产生新的 Delivery 并留下原因。

### 5.6 参数与 Secret 管理

Provider 参数差异很大，不应为每个厂商在公共表上加一组可空列，也不能接受无约束 `object`：

- Provider 代码注册 `ProviderTypeKey`、ChannelCode、能力声明、非敏感配置 Schema、Secret Slot、源生成 JSON 类型、校验器、健康检查和发送实现。
- `ProviderProfile` 只持久化经具体 Provider 校验和规范化的非敏感 JSON、Secret Reference、配置版本及外部应用身份摘要；事件和普通日志不携带配置全文。
- AppSecret、AccessKey、SMTP 密码、证书私钥、Token 等不进入业务表、API 读取响应、审计正文或日志。默认采用部署管理 Secret：后台只填写/选择 Secret Reference。
- 如果未来要求在后台直接录入和托管 Secret，必须先建立独立密钥管理 ADR，覆盖信封加密、主密钥轮换、备份恢复、双人控制和泄漏处置，不能只做“数据库字段加密”。
- Provider Type、外部账号/App 身份等边界字段改变时创建新 Profile；同一应用的密钥轮换更新 Secret Reference 的版本，不伪造为新业务 Profile。

Profile 建议具有 `Draft / Active / Disabled / Retired` 管理状态，并把运行健康/circuit 状态作为单独遥测，避免第三方短暂故障自动改写人工启停状态。真正可发送需同时满足：Provider 代码已安装、Profile Active、Binding Enabled、模板映射已发布、Recipient Endpoint 可达。

禁用 Profile 时必须由管理员选择：仅阻止新 Delivery 并排空在途，或紧急停止/取消待发送任务。疑似凭据泄漏使用紧急停止并轮换 Secret；普通维护优先 Drain。两者都必须使用独立权限和审计。

### 5.7 内部渠道与外部渠道的差异

- **公告**是广播/拉取型聚合，拥有受众、发布、撤回、有效期和可选阅读回执；不能为大受众机械展开成逐用户外部 Delivery。
- **站内信**是逐用户持久化 Inbox，数据库为权威，SignalR 只提示刷新；它应作为 Full.NET 安装即可用的基础渠道。
- **外部渠道**通过 Provider Profile 发送，需要接收地址解析、凭据、模板映射、配额、第三方回执和失败恢复。
- 统一消息中心统一管理 Intent、模板、路由、审计和查询，但保留各渠道真实能力差异；不要强行让所有渠道实现“撤回、送达、已读”同一承诺。

外部 Provider 可按实际交付拆成可选项目，以隔离 SDK、许可证、Secret 和 Native AOT 风险；例如先交付 SMTP 或某一个企业 IM Provider。没有真实配置和测试环境时不得预建所有空 Provider 项目。内部 Announcement/Inbox 继续留在 Notifications 主项目。

### 5.8 管理后台建议

Vue 管理端建议分为六个页面，而不是一张“渠道配置”万能表：

1. **消息总览：** Intent、Inbox、Delivery、Attempt 的聚合状态与失败分类。
2. **模板中心：** 逻辑模板、渠道/语言变体、草稿、发布版本和参数预览。
3. **Provider Profiles：** 新建多套参数、Secret Reference、校验、启停、轮换状态和健康摘要。
4. **应用与场景绑定：** Producer/Scene → Channel/Profile，多选时显式选择 `Single/FanOut/Failover/Match`。
5. **接收地址与偏好：** 用户可达性、绑定来源、静默时段与允许范围内的偏好；敏感地址默认脱敏。
6. **投递运维：** 重试、取消、Reroute、死信、对账和测试发送；每项使用独立权限码。

“测试连接”与“发送测试消息”必须分开。测试发送要选择明确接收人、显示可能产生费用/外部副作用、记录操作者和 Profile，但不能泄漏 Secret 或把测试结果提升为生产送达保证。邮件等应用权限可能允许代表大量用户发送，因此 Profile 激活前必须验证最小权限与资源范围；例如 Microsoft Graph 的应用级 `Mail.Send` 需要管理员同意，并应进一步限制可用邮箱范围。

### 5.9 废弃项目消息中心审计

`Admin.NET.Pro.V2.1.AI-master` 的消息中心不作为 Full.NET 目标模型。它可以提供页面清单和失败用例，但以下实现不得复制：

- `SysMsgChannel` 同时承担 Channel 定义、Provider 实现、外部应用实例和参数 Profile；原本独立的 `SysMsgChannelConfig` 被整类注释，导致通道目录与多套配置实例重新耦合。
- `ChannelConfigJson` 直接包含公众号 `AppSecret`、企业微信 `agentSecret` 等值；分页接口直接返回实体，没有 Secret Reference、脱敏读取和轮换边界。
- `Test` 只返回 `Task.CompletedTask`，并未验证凭据、远端权限或真实发送；“测试成功”没有证据意义。
- `ChannelPriorityJson` 只改变遍历顺序，所有渠道仍逐个发送；没有 `Single/FanOut/Failover/Match` 的明确语义，`FallbackPolicy` 与 `ConditionsJson` 没有进入实际路由判断。
- `BizKey` 只作为普通字段查询，没有唯一约束或幂等消费；投递的 `RetryCount/NextRetryTime` 没有执行路径。
- 任务抢占只把状态从 `0` 改成 `1`，结束后不写 Completed/Failed；无接收人或无渠道时直接返回，任务永久停在处理中。站内信/弹窗分支直接返回成功，也没有建立可查询的内部 Inbox 事实。
- 即时发送在 HTTP 调用内直接执行第三方请求；通道、模板、路由均允许物理删除，没有把路由、Profile 和模板版本固化到 Delivery 快照。
- Workflow 调用 `Raw` 时没有设置 `ChannelTypes`；Raw 任务因此解析不到任何渠道，状态变成处理中后直接返回，而且异常被空 `catch` 吞掉。
- 用户偏好查询接收任意 `UserId`；发送 API 接收调用方提交的 `AppId/SceneCode/Target`。旧框架可能另有全局过滤，但这些服务本身没有表达 Full.NET 所需的 Producer/Scene 授权和资源归属复验。
- 消息中心单元测试在服务解析失败时直接 `return`，前端 Playwright 脚本没有业务断言；不能作为可靠性、权限、幂等或真实投递证据。

可吸收内容仅限：管理后台的信息架构、Template/Task/Message/Delivery 的概念启发、渠道枚举候选和用于建立回归测试的失败场景。Full.NET 的表、状态机、权限、租户、Secret、路由和发送链路继续以第 5 节目标模型为准。

## 6. Workflow 设计修订

### 6.1 定义与运行时

建议把现有 Spec 的“定义头 + 不可变版本”细化为：

| 候选实体 | 责任 |
| --- | --- |
| `fn_workflow_definition` | 稳定 DefinitionKey、作用域、显示元数据、最新草稿/发布指针 |
| `fn_workflow_definition_draft` | 唯一可变草稿、DraftRevision、乐观并发、规范化内容哈希 |
| `fn_workflow_definition_version` | 发布后不可变的规范 JSON 快照、版本、哈希、发布人/时间 |
| `fn_workflow_instance` | 固定绑定发布版本、业务关联、运行结果、并发版本和挂起原因 |
| `fn_workflow_step` | 节点的一次激活，不等同于定义节点 |
| `fn_workflow_approval_slot` | 一票/一个审批席位；转办不新增票，加签才新增席位 |
| `fn_workflow_todo` | 当前可办理工作及办理人/候选人；不是审批历史本身 |
| `fn_workflow_action_record` | 追加式同意、拒绝、转办、加签、撤回和系统动作 |
| `fn_workflow_cc` | 知会事实，不参与通过阈值 |
| `fn_workflow_execution_log` | 引擎推进、幂等命令和恢复事实；与平台 Operation Log 分工 |
| `fn_workflow_domain_audit` | 发布、审批、取消和强制恢复的模块自有 B0 领域审计；与业务状态同事务 |

定义图推荐使用**规范 JSON 文本**，理由是可审查、可差异比较、可由 Vue 设计器读取、可用 System.Text.Json 源生成，并能在双库中保持相同语义。MemoryPack 继续用于已批准的跨进程 Integration Event，不作为首选定义存储格式。发布前必须限制图大小、节点数、边数和表达式复杂度，并对规范 JSON 计算哈希。

实例状态只表达流程运行结果，例如 `Running / Completed / Rejected / Cancelled / Suspended / Faulted`；`APPROVED → EXECUTED → ARCHIVED` 属于业务单据状态，不应写死为通用 Workflow 实例状态。

### 6.2 审批人解析与资源授权

- 定义版本保存 `ResolverKind + typed arguments`，例如指定用户、发起人、角色候选、组织负责人；不保存可执行脚本。
- 节点激活时通过受信目录解析并快照审批席位。组织变化是否影响已激活任务必须由版本化政策决定，默认不追溯。
- 空审批人策略、发起人与审批人相同策略均属于定义版本，不属于通用 Settings。
- 每次办理同时检查 Endpoint 权限、当前租户、任务归属/候选关系、任务状态和业务资源范围；拥有 `workflow.todos.act` 不等于可以办理任意人的任务。
- 转办目标必须来自受信目录并满足作用域；委托链有深度上限且完整留痕。

### 6.3 会签并发

会签权威事实应是 Approval Slot + Action Record，聚合计数只作可重建投影，不应成为唯一真相：

- `ALL`：`approved == total` 通过；`rejected >= 1` 拒绝。
- `ANY`：`approved >= 1` 通过；`rejected == total` 拒绝。
- `N_OF_M`：`approved >= N` 通过；`rejected > M - N` 拒绝。
- 节点激活时快照 `M` 和 `N`；动态加签只允许在节点未终态时改变席位集合。
- 办理命令先以任务/命令 Id 幂等，原子关闭席位，再用 Step/Instance 版本 CAS 判定终态；并发失败重读后重算。
- 终态一旦提交，剩余 Todo 统一取消；与最终办理并发的加签必须失败关闭。
- `reconcile` 从 Slot/Action 重建投影并只由高权限运维入口触发，必须审计且不能篡改原始动作。

首个纵向切片继续只做单节点单审批人；会签、回退、转办和加签必须在后续独立 RED/双库切片实现，不能为了演示塞入首版。

### 6.4 驳回、撤回、取消

- 首版 `Reject` 直接形成实例 `Rejected` 终态。
- “重走全流程”“回原驳回节点”“回任意历史节点”是三种不同版本化政策，不能共用一个布尔字段。
- 撤回只允许发起方在明确窗口与无不可逆副作用时请求；已完成副作用必须走补偿或人工处置。
- Cancelled、Rejected、Faulted 是不同结果，均应发布独立版本化结果事件。

### 6.5 租约与恢复

不建议给等待人工审批数天的整个 Instance 长期持有租约。实例/步骤用乐观并发保护；只有短时“可运行工作项”、超时扫描批次或自动节点领取时使用有限租约。过期租约可被其他 Worker 重新领取，所有外部动作仍以稳定幂等键保护。

## 7. Workflow 与 Notifications 联动

### 7.1 推荐事件

稳定机器码在 Spec 批准时最终命名，候选语义包括：

- Task Assigned / Reassigned / Reminder Due / CC Created
- Instance Completed / Rejected / Cancelled
- Workflow Started（业务模块需要回填关联时）

事件只携带通知所需的稳定标识、租户作用域、接收人、业务路由键和有界模板参数；不携带任意 HTML、动态类型或敏感表单全文。Notifications 选择模板版本和渠道。首次落地 Workflow → Notifications 编译期消费者时，才评估拆出 `Full.NET.Modules.Workflow.Contracts`；此前不创建空 Contracts 项目。

### 7.2 一键审批安全边界

外部渠道的 Provider callback 只能更新投递回执，不能直接代表用户同意/拒绝。可操作通知采用以下边界：

1. 默认发送深链，用户进入 Full.NET 后完成身份验证与 POST 确认。
2. 若确需限时动作 Token，Token 必须绑定 TenantId、UserId、TaskId、Action、任务世代、Nonce 和过期时间，并原子消费一次。
3. Token 有效仍要重新检查任务归属、状态和服务端权限；GET 请求不得产生审批副作用。
4. 高风险/资金类审批应要求重新认证或二次确认，不能把“15 分钟签名有效”视为充分授权。

这与 OWASP 的“每次请求重新授权、默认拒绝、资源归属复验”一致；ASP.NET Core 可提供有限时保护载荷，但单次消费状态仍需数据库记录。

## 8. 工作流引擎选型

| 方案 | 优点 | 与 Full.NET 的主要缺口 | 结论 |
| --- | --- | --- | --- |
| **自有审批领域内核** | 精确覆盖人审、权限、租户、Dapper 双库、Outbox 与 AOT；可最小纵向交付 | 首期没有 BPMN 全量节点和成熟设计器 | **首版推荐** |
| **Elsa 3** | .NET、MIT；支持代码/JSON/设计器、版本、Dapper 持久化和多租户 | 动态表达式与广泛活动扩大攻击面；持久化/Studio 边界、双库迁移和 Native AOT 尚未在本仓库证明 | 后续首选 PoC 候选，不进入核心默认依赖 |
| **Workflow Core** | MIT、轻量嵌入、JSON/YAML、可插拔 SQL Server/MySQL 持久化 | DSL 可绑定程序集类型；审批权限、版本治理、表所有权、AOT 和恢复需大量外包裹 | 仅比较性 PoC，不作为首版基础 |
| **Flowable OSS** | Apache-2.0；成熟 BPMN、人任务、多实例和完成条件 | Java 17+ 运行时或独立 REST 服务；身份授权仍由应用负责；引入跨运行时运维和数据边界 | 仅在必须 BPMN 互操作且接受独立服务时重评 |
| **Camunda 8** | 成熟 BPMN、User Task、Tasklist、Operate 和集群能力 | 独立平台运维；8.6+ 生产 Self-Managed 需要 Enterprise 许可，不满足 Full.NET 默认 MIT 发布路线 | 不作为默认/内嵌方案 |
| **Dapr Workflow** | Apache-2.0；耐久执行、计时器、恢复、.NET SDK | 面向跨应用代码编排，不提供本项目所需的人审领域、表单权限和定义治理；增加 Sidecar/State Store | 微服务编排门禁后再评估 |
| **Temporal** | MIT SDK；成熟耐久执行与长流程恢复 | 外部 Temporal 集群；.NET SDK 含 native bridge；不是审批任务产品，Provider native binding/AOT 未证明 | 不适合当前模块化单体首版 |

任何第三方引擎 PoC 必须同时通过：实际许可证与再发布审计、SQL Server/MySQL 数据语义、租户隔离、权限资源校验、定义升级/在途实例恢复、重复命令、Worker 崩溃、Linux Native AOT publish/进程测试、运行拓扑与回滚。仅“能跑 Demo”不能改变选型。

### 8.1 废弃项目设计器资产事实

旧项目实际存在两套编辑模型：

1. `workflow-vue3` 递归树模型：页面目前把 `isLogicFlowGraph` 固定返回 `false`，所以新增和编辑强制使用该模型，保存的是 `nodeName/type/childNode/conditionNodes`。
2. LogicFlow 图模型：遗留代码和后端运行时使用 `nodes/edges`，但当前编辑入口已经被关闭。后端 `Start` 仍只查找 `type == "start-node"` 的图节点。

因此当前页面保存的树定义在运行时会反序列化成空 `Nodes/Edges`，启动时报告“流程缺少起始节点”。这不是少补一个转换器即可上线的问题：两套模型对网关、并行、回退、子流程和节点状态的语义都尚未统一。

设计器目录共有 48 个文件、约 1.31 万行。与 `Workflow-Vue3` 上游 `8d81e61` 做无索引差异比较，32 个文件发生变化，约新增 7,818 行、删除 505 行，说明本地确实积累了可观的改造成果。面板当前提供 18 种用户可新增节点：

- 人工节点：审批人、分组审批、办理人、投票、抢单、跨租户审批、抄送人。
- 路由节点：条件、并行、包容、动态路由、空节点。
- 集成/过程节点：触发器、异步触发器、延时器、子流程、修改数据、删除数据。

其中审批人、条件、子流程等 Drawer 已有较丰富的配置与前端校验；这部分产品交互和需求枚举值得保留。但旧后端只会沿 LogicFlow 的出边创建普通任务，不解释上述数字节点类型，也没有会签汇聚、条件计算、子流程、延时、投票、数据写入或跨租户执行器。现有自动化测试只有菜单可见性 Smoke，没有定义保存→发布→启动→节点执行闭环。

### 8.2 采用裁决

建议采用“**保留产品资产，重建技术内核**”，而不是整体复制旧模块：

| 资产 | 裁决 | 原因 |
| --- | --- | --- |
| 树形审批画布、插入/删除/分支交互、缩放和错误定位 | 保留并重构 | 符合 OA 审批配置习惯；授权已确认，但仍须迁入 Full.NET 风格组件和单一 IR 链路 |
| 18 类节点及 Drawer 字段 | 作为需求池 | 字段设计有价值，但“界面可配置”不等于“运行时已支持” |
| Pinia Store、递归 `nodeWrap.vue`、大体量全局 CSS | 重构迁移 | 大量 JavaScript/`any`、共享可变状态、远程字体/图片和样式污染，不宜原目录搬运 |
| 数字 `NodeType` 与无版本 FlowJson | 废弃 | 稳定协议不可依赖易冲突数字；没有 SchemaVersion、节点版本或规范化哈希 |
| `Math.random()` 节点 Id | 废弃 | 不能承担稳定持久化、引用、差异比较和审计身份 |
| 浏览器 `new Function` 投票脚本 | 禁止 | CSP、注入、可重复执行与服务端一致性均不可接受 |
| 任意 `remoteUrl/headers/body` | 禁止直通 | 会形成 SSRF、Secret 泄漏和无边界外部副作用；应改成受控 Connector/Business Command |
| 修改数据/删除数据节点 | 改造成业务命令节点 | Workflow 不能直写其他模块表；由拥有者模块 Port/事件执行并声明幂等、补偿和权限 |
| LogicFlow 编辑器 | 不作为首版定义编辑器 | 当前形成第二套协议；如后续需要，可用于只读实例轨迹或通过独立 ADR 引入自由图编辑 |
| 旧 C#/SqlSugar 运行时与表结构 | 不迁移 | 与 Full.NET Dapper、双库、UUID v7、租户、Outbox、租约恢复和模块边界冲突 |

设计器首先放在 `ui/admin/src/features/workflow/designer/` 内作为 Workflow 页面的一部分；没有第二个真实客户端消费者前，不创建独立 npm 包。旧目录内误放的 `.code-workspace`、独立 demo 入口、Mock API、远程阿里字体/图片、残留 Debug 覆盖层和已关闭的 LogicFlow 编辑路径均不迁入。

### 8.3 目标定义链路

目标链路应固定为：

```text
Vue Designer Draft
  → 客户端强类型结构校验
  → Workflow Publish API
  → 服务端授权 + Schema 校验 + 语义校验 + 规范化
  → 编译为不可变 WorkflowDefinitionVersion IR
  → 内容哈希
  → Runtime 只执行已发布 IR
```

设计器 JSON 只是草稿输入，不能直接成为运行时解释器的任意对象。建议每个节点采用稳定字符串 `NodeTypeKey + NodeSchemaVersion + NodeKey + Config`，例如 `human.approval`、`notify.cc`、`gateway.exclusive`、`timer.delay`、`workflow.subprocess`。前端使用 TypeScript 判别联合，服务端使用闭合的 System.Text.Json 源生成类型与显式编译器；未知节点、未知字段版本、悬空引用、不可达节点、无终点、非法回边和不受支持能力在发布时失败关闭。

树形编辑模型可以继续承载审批友好的结构化流程，但发布器必须编译到统一的规范 IR。未来若增加 LogicFlow/BPMN 编辑器，也只能生成同一 IR，不能让运行时长期兼容两套相互独立的 FlowJson。

### 8.4 节点升级顺序

旧设计器节点不能一次全部标为可用，应由服务端 `NodeTypeCatalog` 返回能力状态：`Designable / Publishable / Executable`，只有当前部署已安装且通过验证的节点才允许发布。

1. **MVP：** 发起、单人审批、抄送、排他条件分支、结束；设计器可在首个后端切片完成后接入。
2. **人工审批增强：** 角色/组织负责人、办理人、抢单、会签/或签/N-of-M、分组审批、转办/加签/驳回。
3. **耐久控制流：** 延时、超时、并行/包容汇聚、子流程和恢复检查点。
4. **受控集成：** Connector、Business Command、动态路由；必须使用允许列表、强类型输入输出、幂等键、超时、重试、补偿、审计和 SSRF 防护。
5. **高风险能力：** 跨租户审批、投票自定义规则、外部回调。跨租户不是普通节点配置，必须先有独立授权/数据边界决策；投票只允许声明式阈值或受控表达式 AST，不执行 JavaScript。

`Empty` 节点只作为编辑器布局占位并在发布编译时消除；“修改数据/删除数据”不允许转换成任意 SQL 或跨模块表操作。

### 8.5 表单引擎采用裁决

#### 8.5.1 旧项目的真实完成度

旧 Admin.NET 项目不是自研了一套完整表单运行时，而是全局注册 `vform3-builds@^3.0.10`；官方组件同时提供 `v-form-designer` 与 `v-form-render`。本地实现已经具备一些值得保留的产品资产：

- 流程定义向导内直接嵌入 `v-form-designer`，并能从设计器读取 `FormJson`。
- 另有表单模板、表单版本和设置当前版本的管理页面。
- 能从 VForm3 JSON 尝试提取字段，并按全局/节点配置 `编辑 / 只读 / 隐藏 / 必填`。

但现有实现没有形成可信执行闭环：

- 任务发起和审批页面仍使用 textarea 让用户手工输入“表单结果 JSON”，没有使用 `v-form-render`。
- `Start` 直接信任客户端提交的 `FormJson` 并写入记录；`Approve` 直接把 `FormResult` 覆盖到流程结果，没有加载已发布表单版本，也没有进行 Schema、类型、必填、字段权限或并发版本校验。
- 字段权限挂在可变的流程定义头上，运行时没有读取；已发布实例可能因后续编辑而漂移。
- 表单模板版本可以继续更新或删除，流程版本只存另一份 `FormJson`，没有稳定的 `FormVersionId` 绑定和内容哈希。
- 旧 `Web` 没有锁文件，依赖使用 caret；当前上游 3.0.10 源码基于较早的 Vue/Element Plus，必须先证明其与 Full.NET 当前 Vue、Element Plus、TypeScript 和 Vite 版本兼容。

因此裁决是：**采用 VForm3 的设计器交互与渲染组件，重建 Full.NET 的表单领域、发布编译器和安全适配层；不迁移旧项目的原样 JSON 直存直执行链。**

#### 8.5.2 目标表单链路

```text
VForm3 Designer（受限组件目录）
  → WorkflowForm Draft
  → Workflow Publish API
  → 服务端授权 + 表单 Schema/语义/安全校验
  → 编译为不可变 WorkflowFormVersion + WorkflowFormSchema + WebRenderSchema + Hash
  → WorkflowDefinitionVersion 固定绑定 FormVersionId
  → Instance 固定绑定定义版本与表单版本
  → 三个消费者固定读取同一已发布版本
      ├─ 管理后台：VForm3 Web Adapter / 受限 v-form-render
      ├─ H5、微信小程序、支付宝小程序：FullNetFormRenderer（uni-app + uni-ui/原生组件）
      └─ 服务端：WorkflowFormSchema Validator
  → 客户端提交字段 Patch + ExpectedRevision + IdempotencyKey
  → 服务端按当前节点字段策略重新校验
  → 表单数据、Todo 动作、步骤状态、审计和必要 Outbox 同事务提交
```

VForm3 JSON 只能是设计器输入，不能直接成为服务端领域契约。发布器应把允许的子集单向编译为 Full.NET 自有、带 `SchemaVersion` 和 `AdapterVersion` 的 `WorkflowFormSchema`；`WebRenderSchema` 只是后台 VForm3 Adapter 的派生产物，不能反向成为权威协议。所有运行时只接收已发布的安全 Schema，不接受客户端替换 `FormJson`。

#### 8.5.3 版本与数据模型

在只有 Workflow 一个真实消费者时，表单能力先作为 `Full.NET.Modules.Workflow` 主项目内的 `Forms` 垂直切片，不提前创建独立 Forms 模块或 npm 包。以下模型已同步到批准后的 Workflow Spec：

- `fn_workflow_form_definition`：稳定表单标识和草稿生命周期。
- `fn_workflow_form_version`：不可变 Schema、渲染 Schema、内容哈希、适配器/组件目录版本和发布审计。
- `fn_workflow_form_submission`：实例表单数据、并发修订号、Schema 版本引用和数据分级信息。
- `WorkflowDefinitionVersion.FormVersionId`：发布时固定绑定，禁止运行时读取“当前表单版本”。

节点字段策略建议固化在 Workflow 定义版本 IR 中，键为稳定 `NodeKey + FieldKey`，权限至少表达 `Hidden / ReadOnly / Editable / Required`。隐藏字段不仅不渲染，也不得随任务详情 API 返回；只读和不可见字段若出现在提交 Patch 中必须失败关闭。若后续出现第二个独立业务消费者，再通过 ADR 评估抽取通用 Forms 模块。

#### 8.5.4 安全与兼容边界

VForm3 官方示例 Schema 包含 `cssCode`、`functions`、`onFormCreated`、`onFormMounted`、`onFormDataChange` 等可执行扩展位。Full.NET 默认禁止发布或执行任意 JavaScript、自定义 HTML/iframe、任意远程数据源、任意 CSS 和未知组件；动态条件使用服务端可验证的声明式规则/AST。组件目录首批只开放文本、数字、金额、日期时间、单选、多选、下拉、文本域、开关等基础字段，并逐项定义服务端类型、长度、精度、时区、空值和序列化语义。

文件/图片只保存 Files 模块的受控引用，不把 Base64 或任意 URL 写入表单 JSON；用户、角色、组织、字典等选择器通过最小受信 Port/投影获取候选项，提交时服务端重新验证主体、租户和有效性。敏感字段必须声明分级、脱敏、审计和保留策略，禁止把表单全文放入通知、执行日志或 Integration Event。

`vform3-builds` 使用自定义 Variant Form 许可，不是 MIT。其条款允许个人/公司商业使用并允许分发构建代码，分发源代码时要求保留作者声明；因此它可以作为 Full.NET 第三方依赖，但不得被标记为 Full.NET 自有 MIT 源码。真正引入时应精确锁定版本、提交锁文件、归档许可证/来源并更新 `THIRD-PARTY-NOTICES`，同时完成依赖漏洞、包体积、CSP、可访问性和浏览器 E2E 审计。

#### 8.5.5 H5 与 uni-app 轻量渲染器

确认采用以下边界：后台继续用 VForm3 设计表单，并可通过受限 Adapter 使用 `v-form-render`；`clients/uniapp` 为 H5、微信小程序和支付宝小程序实现 Full.NET 自有 `FullNetFormRenderer`。移动端可以利用表单 JSON 所表达的数据，但只能读取服务端发布编译后的 `WorkflowFormSchema`，不得直接解释 VForm3 原始 JSON、事件函数、CSS、Element Plus 属性或设计器布局元数据。

首版组件目录建议保持静态、可审计并跨端同语义：

| 规范字段 | uni-app 渲染建议 | 必须固化的服务端语义 |
| --- | --- | --- |
| 单行文本、文本域 | `input` / `textarea` 或对应 uni-ui 组件 | 长度、空值、字符规范化 |
| 整数、小数、金额 | 数字输入外观；金额值按十进制定点字符串传输 | 范围、Scale、舍入；不得以 JavaScript 浮点作为金额权威值 |
| 单选、多选 | `radio-group` / `checkbox-group` | 稳定 OptionKey、选项有效性 |
| 下拉选择 | `picker` 或受控 uni-ui 选择器 | 静态/受控数据源、分页和提交重验 |
| 日期、时间、日期时间 | `picker` | 时区、精度、序列化格式 |
| 开关 | `switch` | 严格 Boolean |
| 文件、图片 | 受控上传组件 | 只提交 Files 模块引用，不接收 Base64 或任意 URL |
| 用户、组织、角色、字典 | 后续受控选择器 | 租户、主体状态、权限和引用有效性重验 |

子表格/明细、富文本、手写签名、公式、任意远程数据源和关联流程不进入首版白名单。Schema 使用 `sections/groups/fields` 等语义布局；后台网格在手机上可以确定性折叠为单列，不能把 Element Plus 的栅格属性当作跨端协议。

微信和支付宝小程序对 Vue 动态组件/异步组件能力存在限制，因此渲染器不能依赖 Web 常见的 `<component :is>` 插件注册方式。首版使用静态组件目录和显式 `v-if/v-else` 或编译期映射，使构建工具能够确定组件闭包；共享包只承载 Schema 类型、纯校验规则和无框架状态机，不依赖 Vue、VForm3、Element Plus 或 uni-ui。

包体与加载策略必须作为实现门禁，而不是先验宣称“更快”：

- 工作流表单页面和渲染组件放入 uni-app `subPackages`，不进入微信/支付宝小程序主包；H5 使用路由/页面级懒加载。已有 easycom 自动引入继续用于裁剪未使用组件。
- Schema 以 `FormVersionId + Hash/ETag` 缓存；已发布版本不可变。大表单按 Section 渐进展示，字典/人员等候选项按需分页加载，不把全部选项写进 Schema。
- 建立 30 字段和 100 字段基准表单，记录 H5 的 minified、gzip、Brotli 体积与初始/懒加载 Chunk，另行记录微信/支付宝主包与分包字节数，以及低端设备冷/热启动、首次可交互、首次校验和候选项加载耗时。
- 首次测量形成基线后再设置相对回归预算；当前没有测量数据，不承诺固定 KB 或毫秒指标，也不能仅凭“自研”认定性能达标。

跨端验证至少包含 Schema Golden Fixture、服务端与客户端校验语义一致性、节点字段权限、缓存版本切换，以及 `build:h5`、`build:mp-weixin`、`build:mp-alipay` 和 `test:e2e:uniapp`。同一 `FormVersionId` 在三端允许布局适配，但字段类型、必填、隐藏、只读、选项和值序列化必须一致。

## 9. 建议分阶段交付

### 阶段 0：修订并批准设计（已完成）

- 已按本评估第 11 节批准裁决更新 Workflow 与 Notifications 唯一权威 Spec。
- Workflow-Vue3 使用授权已由项目所有者确认；实现前把作者允许、上游提交、本地修改范围和再分发条件归档到来源记录与第三方声明。
- 确认 VForm3 采用方式、精确版本和允许组件目录，完成兼容/许可 PoC；禁止沿用旧项目的 caret 依赖和无锁文件状态。
- 已修订现有 Workflow Spec，没有创建竞争性 Workflow Spec；Notifications 扩展另有独立主题 Spec。
- 重新核对首切片计划中的迁移号、Notifications/Jobs 前置事实和任务快照。
- 后续只能按已批准的独立实施计划开工；Spec Approved 不等于已授权跳过任务快照、RED、双库、AOT、许可或真实栈门禁。

### 阶段 1：Workflow 最小纵向切片

- 草稿 → 规范 JSON 校验 → 发布不可变版本。
- 增加 Workflow 内部表单定义/不可变表单版本/Submission；流程版本固定绑定表单版本，首批只支持基础字段和声明式校验。
- 管理后台接入受限 VForm3 Web Adapter，以服务端返回的安全 `WebRenderSchema` 展示发起/办理表单；权威校验只读取同版本 `WorkflowFormSchema`，不允许客户端提交或替换 FormJson。
- 启动实例 → 单人工节点 → 我的 Todo → 同意/拒绝终态。
- 表单 Patch、Todo 动作、步骤推进、B0 审计和必要 Outbox 原子提交；服务端执行字段可写、必填、类型和 ExpectedRevision 校验。
- SQL Server/MySQL、权限 403、任务资源授权、幂等/并发、Vue 按钮门控和真实栈 E2E。
- 不含会签、外部渠道、动态脚本、可视化全量设计器。

### 阶段 2：租户 Inbox 与可靠联动

- Notifications 从 Host-only 扩展为受信任 Tenant/Host 作用域，保留旧 Host API 兼容。
- Workflow 发布 Assigned/Completed/Rejected/Cancelled 事件；Notifications Inbox 幂等消费。
- 数据库权威未读数、SignalR 提示与 Outbox 修复继续沿用现有模型。
- 业务模块用 Inbox 幂等消费 Workflow 结果，不跨模块事务。

### 阶段 3：模板与第一个外部渠道

- Template Draft/Published version、参数 Schema、Intent/Recipient/Delivery/Attempt。
- Provider Type Catalog、可创建多套 Provider Profile，以及 Producer/Scene 与多个 Profile 的显式 Binding/DispatchMode。
- 只选择一个真实 Provider（建议邮件或企业 IM，而非同时铺开所有渠道）。
- Secret 注入、回执验签、退避/死信、租约恢复、频控、租户配额和可观测性。
- Provider 替换通过配置/Composition 完成，但不承诺跨厂商完全同语义。

### 阶段 4：设计器 MVP 与单一 IR

- 基于已获授权的 Workflow-Vue3 本地改造成果选择性迁移树形交互和已批准 Drawer；使用 Full.NET 自有受限节点 Schema、TypeScript 类型和服务端发布编译器重建，不复制旧运行时协议。
- 在同一发布向导接入受限 VForm3 表单设计器；表单 Draft 与流程 Draft 分开保存，但发布时原子固化并建立 DefinitionVersion → FormVersion 绑定。
- 在 `clients/uniapp` 交付 `FullNetFormRenderer` 基础字段目录；H5、微信小程序和支付宝小程序读取同一 `WorkflowFormSchema`，使用移动端布局并通过分包隔离，不引入 VForm3/Element Plus。
- 首版只呈现发起、单人审批、抄送、排他条件和结束，以及已批准的基础表单字段，完成“表单/流程设计→校验→发布→启动→办理→轨迹”的同版本闭环。
- 设计器每次只开放后端已经 Publishable/Executable 的节点；高级节点可以保留在需求池，但不能用灰色“即将支持”冒充已交付能力。
- LogicFlow 如保留，先只承担已发布版本/实例轨迹的只读展示，并读取同一规范 IR。

### 阶段 5：复杂节点与引擎决策门

- 顺序节点、角色/组织负责人解析、提醒与超时。
- 会签 `ALL/ANY/N_OF_M`、串行会签、转办、前/后加签和明确驳回回退政策。
- 并发投票、加签竞态、超时竞态、恢复重放和 reconcile 双库测试。
- 只有出现 BPMN 交换、复杂网关、子流程或跨系统编排的真实需求，才执行 Elsa/Flowable 等 PoC。
- PoC 通过且收益超过迁移、AOT、许可和运维成本后，再提交 ADR；否则继续自有内核。

## 10. 验收重点

| 类别 | 最低证据 |
| --- | --- |
| 定义 | 同输入规范化哈希稳定；非法图失败关闭；发布版本不可更新；在途实例不漂移 |
| 表单 | 流程版本固定 FormVersionId；未知/危险组件发布失败；节点字段隐藏/只读/必填在 API 端执行；越权字段 Patch、旧 Revision 和客户端替换 FormJson 均失败关闭 |
| 表单跨端 | 同一 FormVersionId 的 Golden Fixture 在后台、H5、微信和支付宝端字段语义一致；移动端不解释 VForm3 原始 JSON；三种 uni-app 构建通过并记录初始包/分包/懒加载 Chunk 与关键渲染性能基线 |
| 租户/权限 | Host/Tenant 隔离；未知权限失败；按钮不创建；越权办理 403；直接构造任务 Id 失败 |
| 事务 | 审批状态、Action、B0 Audit、Outbox 原子；Result Failure 回滚；无外部调用在事务内 |
| 并发 | 重复提交、并发同意/拒绝、加签/终态竞态、租约过期重领均确定收敛 |
| 通知 | Intent/Recipient/Delivery/Attempt 幂等；回执乱序不回退终态；未读数可重建 |
| 双库 | 成对迁移、半完成 DDL 恢复、索引访问路径、真实 SQL Server/MySQL Integration |
| AOT | 静态 JSON 元数据、Dapper 参数/物化、Provider native binding；Linux 发布后外部进程 E2E |
| 运维 | backlog/oldest age、失败分类、死信、重放、对账、强制恢复审计和停止条件 |

容量仍应标记 `Capacity-not-verified`，直到在生产等价环境完成 Workflow 激活/办理、Notifications 扇出、渠道限速与尾延迟测试。

## 11. 已批准裁决与后续阶段门禁

项目所有者已确认：

- Workflow-Vue3 是可使用的开源项目，且已取得作者允许；以旧项目改造成果作为工作流设计器升级基础。
- Workflow 需要表单引擎；采用旧 Admin.NET 已集成的 VForm3 设计器/渲染器能力。
- VForm3 原始 JSON 只作为后台设计输入；发布时编译为 Full.NET 权威 `WorkflowFormSchema`。后台使用受控 VForm3 Web Adapter，H5/uni-app 基于该 Schema 自研轻量渲染器，不把 VForm3/Element Plus 带入移动端包。

本次授权批准以下默认裁决：

1. Workflow 定义、实例和表单采用 Host/Tenant 双作用域；首版不允许租户实例引用 Host 定义，真实 E2E 各覆盖一条。
2. 同一业务键允许历史多实例，但同一作用域最多一个 Active；批准后重开由业务模块显式发起新实例。
3. 规范 JSON 是定义权威格式；MemoryPack 仅用于有真实消费者的可靠 Integration Event。
4. 首版 Reject 为终态；节点回退、重走全程和复杂驳回策略后续版本化扩展。
5. 可操作通知首版只提供登录后深链，不提供免登录一键同意/拒绝。
6. Profile 路由首版为 `Single/FanOut/Failover/Match`；多个 Profile Enabled 不自动 FanOut。
7. Provider Profile 只保存部署 Secret Reference；直接托管 Secret 需独立安全 ADR。
8. Host Profile 默认不向租户共享；只有显式允许范围与 Binding 才能使用，租户不得读取 Host Secret。
9. 树形审批 Draft 由服务端编译为单一 IR；LogicFlow 首版仅作为候选只读轨迹视图。
10. 首批 Workflow 节点为发起、单人审批、抄送、排他条件和结束；其他节点不随 UI 迁移获得实现承诺。
11. 表单能力先留在 Workflow 主项目；第二个独立消费者出现后再通过 ADR 评估抽取 Forms 模块。
12. 首批表单只开放基础字段；文件/主体选择器需受信 Port 后开放，富文本、签名、公式、脚本、HTML/iframe、远程数据源和自定义 CSS 首版禁止。

以下事项保留为对应阶段的启动门禁，不阻塞本次 Spec 批准：

- **首个真实 Workflow 结果消费者：** 在结果事件切片前选择；未选择时不创建独立 Contracts 程序集或业务结果占位事件。
- **首个外部通知 Provider：** 在厂商纵向切片前明确协议/SDK、许可证、Secret 来源、沙箱、费用、频控和回执；未选择时生产 Provider Type Catalog 可以为空。
- **高风险数据与动作：** S2 表单字段、免登录动作、验证码/认证挑战、支付验证和营销跨渠道分别通过安全/合规批准后才能开放。

## 12. 未验证项与限制

- 本次没有修改或执行 Workflow/Notifications 运行时代码，没有进行 SQL Server/MySQL、Native AOT、负载或第三方 Provider 实测。
- 废弃项目不是 Git worktree且 `Web` 没有锁文件或可用 test/typecheck 脚本；本次只做静态源代码、上游差异和现有测试审计，没有安装依赖或把旧项目构建成功当作证据。
- Workflow-Vue3 可使用与作者允许由项目所有者明确确认，本评估据此移除许可阻塞；本次没有检查授权原件。真正迁入源码前仍须在仓库归档授权、来源提交、本地修改说明和第三方 Notices。
- VForm3 仅完成旧项目集成、官方仓库、3.0.10 包信息和许可条款的静态审计；尚未在 Full.NET 当前 Vue/Element Plus/TypeScript/Vite 组合下安装、构建、渲染或执行浏览器 E2E。`FullNetFormRenderer` 目前也是已确认的目标设计，尚未实现或取得 H5/微信/支付宝包体与运行性能数据。
- Elsa、Workflow Core、Flowable、Camunda、Dapr Workflow、Temporal 只完成官方文档级评估；没有在 Full.NET 中安装依赖或运行 PoC。
- 输入材料未提供明确业务首个消费者、并发规模、渠道合同、费用、法规留存和通知内容分级，因此不做容量、成本和工期承诺。
- 本评估本身仍是批准证据而非运行时规格；本次授权已把决策同步到 Workflow/Notifications Spec、独立实施计划和路线图。没有修改运行时代码，能力状态不得因文档批准升为 Implemented/Verified。

## 13. 外部资料

- [Elsa 3 Architecture](https://docs.elsaworkflows.io/guides/architecture)、[Persistence](https://docs.elsaworkflows.io/guides/persistence)、[Multitenancy](https://docs.elsaworkflows.io/multitenancy/introduction)、[Elsa Core](https://github.com/elsa-workflows/elsa-core)
- [Workflow Core](https://github.com/danielgerlag/workflow-core)
- [Flowable BPMN constructs](https://www.flowable.com/open-source/docs/bpmn/ch07b-BPMN-Constructs/)、[Flowable Engine](https://github.com/flowable/flowable-engine)
- [Camunda 8 User Tasks](https://docs.camunda.io/docs/components/modeler/bpmn/user-tasks/)、[Camunda 8 Licensing](https://docs.camunda.io/docs/reference/licenses/)
- [Dapr Workflow overview](https://docs.dapr.io/developing-applications/building-blocks/workflow/workflow-overview/)、[Workflow versioning](https://docs.dapr.io/developing-applications/building-blocks/workflow/workflow-versioning/)
- [Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet)
- [Workflow-Vue3 repository](https://github.com/StavinLi/Workflow-Vue3)、[package.json](https://github.com/StavinLi/Workflow-Vue3/blob/master/package.json)、[commit history](https://github.com/StavinLi/Workflow-Vue3/commits/master/)
- [Variant Form 3 repository and integration guide](https://github.com/vform666/variant-form3-vite)、[Variant Form 许可条款 1.0](https://github.com/vform666/variant-form3-vite/blob/master/license.txt)、[3.0.10 package source](https://github.com/vform666/variant-form3-vite/blob/master/package.json)
- [uni-app Vue 3 组件与小程序限制](https://uniapp.dcloud.net.cn/tutorial/vue3-components.html)、[easycom 组件自动引入](https://uniapp.dcloud.net.cn/component/README)、[uni-ui 快速上手](https://uniapp.dcloud.net.cn/component/uniui/quickstart.html)、[uni-app subPackages](https://uniapp.dcloud.net.cn/collocation/pages#subpackages)
- [LogicFlow repository and Apache-2.0 license](https://github.com/didi/LogicFlow)、[Extension architecture](https://github.com/didi/LogicFlow/blob/master/packages/extension/ARCHITECTURE.md)
- [Microsoft Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference)、[Send mail API](https://learn.microsoft.com/en-us/graph/api/user-sendmail?view=graph-rest-1.0)
- [ASP.NET Core Native AOT](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0)、[Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [OWASP Authorization Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html)、[Business Logic Security](https://cheatsheetseries.owasp.org/cheatsheets/Business_Logic_Security_Cheat_Sheet.html)、[ASP.NET Core limited-lifetime protected payloads](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/limited-lifetime-payloads?view=aspnetcore-10.0)

## 14. 治理结论

- **规则演进：** 未触发；本次发现均可由现有模块边界、文档分层、Native AOT、安全和验证规则覆盖。
- **Skill 演进：** 未触发；`fullnet-module-delivery` 已覆盖未来 Workflow/Notifications 纵向切片所需流程。
