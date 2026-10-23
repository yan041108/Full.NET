# 核心业务模块详解

## 一、Tenancy 多租户模块

> 项目：[`src/Modules/Full.NET.Modules.Tenancy`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Tenancy)
> 稳定模块键：`Tenancy`

### 1.1 职责

- 租户开通（Provisioning）：创建租户记录与可选套餐绑定；租户准入读取权威状态
- 租户解析：域名 / 请求 Header / 认证 Claim → 可信 TenantId
- 租户切换：授权用户在其可用租户间切换（写审计 + 刷新会话）
- 租户包（Tenant Package）：功能套餐、配额、资源包
- 缓存失效：租户变更事件 → 清除所有关联缓存

### 1.2 核心类

| 类 | 职责 |
|----|------|
| `Tenant` (Domain) | 租户聚合根：TenantId, Name, Identifier(域名/短名), Status, PackageId |
| `TenantResolver` | 解析链：Host Header → Claim → DefaultLocalTenant |
| `TenantResolutionMiddleware` | 管道中间件：早于认证解析租户（匿名也能解析域名租户） |
| `TenantProvisioningService` | 开通编排：验证标识 → 事务写 Tenant → 播种 Seed Profile → Outbox |
| `HostTenantManagementService` | 宿主管理员 CRUD 租户 |
| `HostTenantPackageManagementService` | 租户包管理 |
| `ITenantResolver` | 每次准入读取租户权威状态，不维护 Redis 副本 |
| `TenantContextSummary` | 契约：当前租户摘要（Id, Name, Locale, TimeZone） |

### 1.3 解析流程

```
HTTP Request
  → TrustedProxyMiddleware (规范化 X-Forwarded)
  → TenantResolutionMiddleware (解析顺序)
      │
      ├── 1. 显式切换 Claim（ChangeTenantContext 写的）
      ├── 2. 认证 Identity Claim 中的 tid
      ├── 3. Host Header 域名匹配（Identifier 字段）
      └── 4. 默认 development 本地租户
  → CurrentTenantAccessor.Push(tenant)  AsyncLocal
  → 后续：SqlScopeGuard 校验 + 缓存键注入 + Outbox 携带
```

---

## 二、Organization 组织架构模块

> 项目：[`src/Modules/Full.NET.Modules.Organization`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Organization)
> 稳定模块键：`Organization`

### 2.1 职责

- 组织单元（部门）：树形层级结构、类型、排序
- 岗位：名称、职级、编制
- 职级序列：P1~P10、M1~M5 等
- 用户组织归属：主部门、兼职部门、岗位分配
- 数据范围：部门树 → Identity 的 DataScope 过滤

### 2.2 核心表

| 表 | 说明 |
|----|------|
| `fn_organization_unit` | 组织单元：Id, TenantId, ParentId, Name, UnitType, Path(物化路径), Sort |
| `fn_organization_position` | 岗位：Id, TenantId, UnitId, PositionName, LevelId |
| `fn_org_position_level` | 职级：Id, TenantId, LevelCode, LevelName, Rank |
| `fn_org_user_unit` | 用户部门：UserId, UnitId, IsPrimary(主部门) |
| `fn_org_user_position` | 用户岗位：UserId, PositionId, StartDate, EndDate |

### 2.3 发布的集成事件

| 事件 | 消费方 |
|------|--------|
| `OrganizationUnitChangedIntegrationEvent` | Identity → 本地投影用于导航/选择目录 |

---

## 三、Settings 配置管理模块

> 项目：[`src/Modules/Full.NET.Modules.Settings`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Settings)
> 稳定模块键：`Settings`

### 3.1 子功能

| 子功能 | 说明 | 硬删除前置条件 |
|--------|------|----------------|
| **Config Entry** | 参数配置项（Host/Tenant 双作用域）、分组、排序、值类型、内置标记 | 必须为"已禁用"状态 |
| **Dictionary** | 字典类型 + 字典项（Host/Tenant 双作用域），支持按编码查询 | 字典类型必须无启用项；字典项必须禁用 |
| **Enum Catalog** | C# 枚举 → 前端下拉目录（只读同步） | N/A |
| **Grid Preferences** | 用户网格列偏好、排序、筛选保存 | N/A |

### 3.2 Config Entry 契约

| 字段 | 说明 |
|------|------|
| ConfigCode | 配置编码（创建可编辑，修改只读） |
| ConfigName | 配置名称（必填） |
| ValueType | String / Integer / Boolean / Decimal / Json（创建可选，修改只读） |
| PropertyValue | 属性值（根据 ValueType 校验） |
| GroupName | 分组 |
| IsBuiltIn | 内置参数（是/否） |
| Sort / Remark / Status | 排序、备注、启用状态 |

### 3.3 批量 API

```
POST /api/v1/settings/config-entries/batch-delete   # 批量删除（仅禁用项）
PUT  /api/v1/settings/config-entries/batch-value     # 批量更新属性值
```

---

## 四、Auditing 审计模块

> 项目：[`src/Modules/Full.NET.Modules.Auditing`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Auditing)
> 稳定模块键：`Auditing`

### 4.1 日志类型

| 类型 | 触发点 | 可靠性等级 | 保留策略 |
|------|--------|------------|----------|
| `fn_auditing_operation_log` | `OperationLogMiddleware` 自动记录 HTTP 操作 | B0 (业务事实，同事务) | 默认 180 天 |
| `fn_auditing_access_log` | 认证/授权阶段（登录、登出、Refresh、租户切换） | B0 | 默认 90 天 |
| `fn_auditing_exception_log` | `ExceptionLogMiddleware` 未处理异常捕获 | B1 | 默认 90 天 |
| `fn_auditing_outbound_call_log` | 出站 HTTP/gRPC 调用（耗时、状态码） | B2 (可丢失) | 默认 30 天 |

### 4.2 写入管道

```
业务写 / 中间件捕获
  → AuditWriteBuffer (批处理缓冲)
  → 按可靠性分类：
     ├── B0：同事务写入（CommandTransaction 附加）
     ├── B1：异步有界队列（后台 Worker）
     └── B2：Fire-and-Forget（可丢弃）
```

### 4.3 查询特性

- 游标分页（Cursor Pagination）：避免深分页性能问题
- 时间范围包含策略：`ContainsTimeBoundary` 防止边界丢日志
- 数据保留 Runner：后台任务按 `AuditingRetentionOptions` 定期清理过期记录

---

## 五、Jobs 任务调度模块

> 项目：[`src/Modules/Full.NET.Modules.Jobs`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Jobs)
> 稳定模块键：`Jobs`

### 5.1 概念模型

```
HostJobDefinition（任务定义）
  ├── JobKey       稳定任务键（唯一）
  ├── DisplayName  显示名称
  ├── Description  描述
  ├── Status       启用/禁用
  ├── HandlerType  任务处理器 Assembly Qualified Name
  └── ParametersJson  默认参数
       │
       └── HostJobSchedule（调度计划，1:N）
              ├── CronExpression  Cron 表达式
              ├── StartAtUtc / EndAtUtc  有效区间
              ├── ParametersJson  本次覆盖参数
              ├── TriggerCount  触发次数
              └── ErrorCount    出错次数（>0 红色 Tag）
                   │
                   └── HostJobExecution（执行记录，1:N）
                          ├── RunAtUtc  实际执行时间
                          ├── DurationMs  耗时
                          ├── Status  Success / Failed / Cancelled
                          ├── OutputMessage  输出（截断）
                          └── ErrorDetailJson  异常详情
```

### 5.2 管理 API 权限

| 操作 | 权限码 | 动态显示条件 |
|------|--------|--------------|
| 查看执行记录 | `jobs.executions.read` | 始终 |
| 立即执行 | `jobs.definitions.trigger` | 仅启用状态 |
| 编辑 | `jobs.definitions.update` | 仅启用状态 |
| 禁用 | `jobs.definitions.disable` | 仅启用状态 |

---

## 六、Files 文件模块

> 项目：[`src/Modules/Full.NET.Modules.Files`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Files)
> 稳定模块键：`Files`

### 6.1 核心概念

- **Host File**：宿主级文件（用户上传到平台），记录元数据 + Blob 路径
- **Reference Claim**：文件引用声明。业务模块在事务内写入 Claim 表示"我正在使用这个文件"；无 Claim 的文件在软删除后由清理器物理回收
- **上传流程**：前端 → 后端流式写入存储（S3/本地/SMB） → 事务写 HostFile 记录 + Outbox → 返回 FileId

### 6.2 Blob 清理

```
DeletedHostFileBlobCleanupRunner（后台 Worker）
  1. 查出 IsDeleted=true 的文件
  2. 检查是否仍有 ReferenceClaim（事务对账）
  3. 无 Claim 且超过宽限期 → 物理删除 Blob
  4. 审计清理记录
```

---

## 七、Document 文档模块

> 项目：[`src/Modules/Full.NET.Modules.Document`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Document)
> 稳定模块键：`Document`

### 7.1 子功能

| 子功能 | 说明 |
|--------|------|
| **Document Item** | 文档项：文件/文件夹、父级、所有者、扩展属性 |
| **Category** | 文档分类：Code、Icon、Color、Description、UseCount |
| **Tag** | 文档标签：字段同 Category，多对多关联 |
| **Permissions** | 细粒度权限：按用户/角色设置读/写/删除/分享/管理 |
| **Shares** | 分享链接：ShareCode(随机 16 进制)、有效期、访问次数 |
| **Statistics** | 文档统计：数量、大小、类型分布、访问趋势 |
| **Recycle Bin** | 回收站：软删除文档、恢复、永久删除 |

### 7.2 分享安全

- ShareCode：`RandomNumberGenerator.GetHexString(8)` 安全随机
- **不支持分享密码**（密码请求返回 400）
- ShareResponse 中 Password 字段 `[JsonIgnore]`，永不返回到客户端

---

## 八、Messaging 消息模块

> 项目：[`src/Modules/Full.NET.Modules.Messaging`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Messaging)
> 稳定模块键：`Messaging`
>
> 本节只描述运维控制面。业务事件的发布、消费和默认 Legacy 轮询见 [可靠事件交付指南](./reliable-event-delivery.md)。

### 8.1 职责

- 事件流所有权注册与切换：LegacyPolling ↔ ShadowCdcKafka ↔ CdcKafka
- 死信（DLQ）查询与人工重放
- Kafka 范围重放运维 API
- Inbox 积压监控
- CDC 启用/禁用脚本（SQL Server CDC / MySQL Binlog 验证）

### 8.2 所有权切换 CAS

```
切换请求: StreamId, TargetOwner
  1. 读取 EventStreamOwnershipRecord: { CurrentOwner, PreviousOwner, Version }
  2. 检查目标积压（DueRetryCount / ActiveLeaseCount）只看目标流，其他流不阻塞
  3. SQL UPDATE ... 
     WHERE StreamId = @StreamId AND PreviousOwner = @ExpectedPreviousOwner
     （原子 Compare-And-Swap）
  4. 受影响行数 = 0 → 并发冲突异常
  5. 成功后：
     - 旧 Worker 立即停止领取该流
     - CDC Relay 开始（或停止）捕获该流追加 INSERT
     - Kafka Consumer Group 开始（或停止）消费
```

---

## 九、CodeGeneration 代码生成模块

> 项目：[`src/Modules/Full.NET.Modules.CodeGeneration`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.CodeGeneration)
> 稳定模块键：`CodeGeneration`

### 9.1 生命周期

```
模板管理
  → 预览（基于 Schema 元数据生成代码片段预览，不写文件）
  → 执行：
      1. 创建 GenerationManifest + Checkpoint
      2. 声明所有权（Path + Hash → 对目标文件 claim → 无覆盖 rename）
      3. 写入产物 + 更新墓碑
      4. 提交 Manifest（最后）
  → 回滚链（按 Checkpoint 逆向恢复）：
      1. 每个 Checkpoint 记录写入前的原始状态
      2. 回滚 = 恢复墓碑 → 删除新文件 → restore 原始
```

### 9.2 Git 集成

- 执行前：确保工作区干净或在专用分支
- 执行后：自动 commit + push 到临时分支 → 创建 PR（可选）
- 远程 Git 操作：专用 `ICodeGenerationGitCommandRunner` 抽象

---

## 十、SerialNumbers 流水号模块

> 项目：[`src/Modules/Full.NET.Modules.SerialNumbers`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.SerialNumbers)
> 稳定模块键：`SerialNumbers`

### 10.1 规则引擎

```text
规则表达式示例：
  PO-{yyyyMMdd}-{seq:6,reset:daily}
  INV-{branch:2}-{year:2}-{seq:4,reset:yearly}
```

### 10.2 并发安全

- 使用 `sp_getapplock` (SQL Server) / `GET_LOCK` (MySQL) 按规则键加命名锁
- 或使用原子 `UPDATE ... SET CurrentValue = CurrentValue + 1 OUTPUT` 分配区间
- 分配器按批量预取（Chunk Size = 10~100），内存内部分配减少数据库压力

### 10.3 核心表

| 表 | 说明 |
|----|------|
| `fn_serial_numbers_rule` | 规则定义：RuleKey, FormatTemplate, ResetMode, CurrentValue, ChunkSize |
| `fn_serial_numbers_allocation` | 分配记录：RuleId, AllocatedValue, IssuedAtUtc |

### 10.4 依赖

- `Identity`：解析受信用户与作用域
- `DataApproval`：硬依赖。启用流水号时必须同时启用数据审批
- `DataApproval` 仅依赖自己定义的 `IDataApprovalScenarioHandler`，流水号注册场景适配器；未启用流水号时审批服务仍可解析，缺失场景提供者的创建或应用明确失败

---

## 十一、Notifications 通知中心模块

> 项目：[`src/Modules/Full.NET.Modules.Notifications`](../src/Modules/Full.NET.Modules.Notifications)
> 稳定模块键：`Notifications`
> 公开契约：[`src/Modules/Full.NET.Modules.Notifications.Contracts`](../src/Modules/Full.NET.Modules.Notifications.Contracts)

### 11.1 职责

- Host 公告：发布、受众范围（用户/组织）、已读回执、统计
- 站内信收件箱：Host 与 Tenant 双作用域共用一张表，按 `TenantScopeKey` 隔离
- 通知模板：草稿/版本/发布、参数 Schema；Inbox 与外部渠道均按收件人/渠道选择语言版本；外部投递冻结 Locale、TemplateVersionId、内容哈希与渲染快照，重试复用
- 通知 Intent：业务模块生成投递意图；当前按模板的单一 ChannelKey 投影 Inbox 或创建外部 Delivery，尚无一次 Intent 跨渠道编排
- 渠道适配：SMTP、AliyunSms、DingTalk、WeCom、WeChatMiniProgram
- 收件端点管理：邮箱/手机号绑定、验证码确认（限流策略独立）
- SignalR 实时推送：基于可靠事件触发，由 Outbox 投递
- Workflow 可靠提醒投影：监听 Workflow TodoAssigned/Reminder/Escalation/Completed/Rejected/Cancelled
- Identity 账号挑战邮件投递：实现 `IIdentityChallengeDeliveryPort`（账号注册/找回密码）
- DingTalk 审批同步：定时拉取审批模板与回调校验

### 11.2 核心类

| 类 | 职责 |
|----|------|
| `NotificationsModule` | 模块入口；声明 `Dependencies: Identity, Organization, Files`、`OptionalContractDependencies: Workflow` |
| `NotificationDeliveryHostedProcessor` | Worker 后台领取循环（仅在 `AddBackgroundServices` 注册） |
| `NotificationDeliveryBatchProcessor` | 领取外部 Delivery 租约，事务外调用 Adapter，条件提交 Attempt 与结果 |
| `NotificationQuotaGovernor` | 四维跨实例额度预留、核销、确定未调用释放与 Unknown 对账 |
| `NotificationSubscriptionGovernor` | 小程序一次性本地许可串行预留；重新绑定和撤回不能复活旧许可 |
| `NotificationLifecycleService` | 审计业务撤回，与 Worker 最终外发边界以意图行锁竞争 |
| `InboxIntentProjectionService` | Intent → 收件箱行投影 |
| `WorkflowNotificationProjectionService` | Workflow 提醒事件 → Intent；内建模板当前投影 Inbox |
| `NotificationRealtimeDelivery` | SignalR 推送器（由 Outbox 事件触发） |
| `NotificationTemplateSelector` | 按可信作用域、模板键和偏好语言选择已发布版本；外部发送固定收件人实际版本与正文 |
| `NotificationProviderTypeCatalog` | 渠道适配器目录 |
| `RecipientEndpointVerificationService` | 收件端点验证码生成与校验 |
| `IIdentityChallengeDeliveryPort` | Identity 调用的账号挑战邮件 Port（`Identity.Contracts`） |
| `AnnouncementPublishedIntegrationEvent` | 公告发布实时事件 |
| `InboxMessageReceivedIntegrationEvent` | 站内信送达实时事件 |
| `InboxReadStateChangedIntegrationEvent` | 已读状态变更实时事件 |

### 11.3 核心表

| 表 | 说明 |
|----|------|
| `fn_notifications_announcement` | 公告：Id, TenantId, Title, Body, Audience |
| `fn_notifications_announcement_target_user` | 公告目标用户 |
| `fn_notifications_announcement_target_organization` | 公告目标组织 |
| `fn_notifications_announcement_read_receipt` | 公告已读回执 |
| `fn_notifications_template` | 通知模板：TemplateKey, LocaleTag, ChannelKey, LatestPublishedVersionId |
| `fn_notifications_template_version` | 模板已发布版本 |
| `fn_notifications_inbox_message` | 站内信收件箱：RecipientUserId, Title, TenantScopeKey |
| `fn_notifications_recipient_endpoint` | 收件端点：UserId、ProviderProfileVersionId、EndpointKindKey、ProtectedValue（加密）与 MaskedValue |
| `fn_notifications_recipient_endpoint_challenge` | 端点验证码：EndpointId, Code, ExpiresAtUtc |
| `fn_notifications_provider_profile` | 渠道配置版本 |
| `fn_notifications_provider_profile_version` | 渠道配置已发布版本 |
| `fn_notifications_binding` | Producer/Scene/Channel 的路由绑定草稿及最新发布版本引用 |
| `fn_notifications_delivery` | 渠道投递：IntentId、RecipientId、ProviderProfileVersionId、StatusKey、租约和 Revision |
| `fn_notifications_intent` | 通知意图：ProducerKey、SceneKey、幂等键及模板/Binding/路由/参数快照 |
| `fn_notifications_preference` | 用户渠道关闭、营销同意与静默 JSON；已接入物化和最终外发许可；渠道关闭、营销同意、时区静默与抑制原因保留版本快照 |
| `fn_notifications_intent_attachment` | Intent 附件引用（Files 模块） |
| `fn_notifications_wechat_miniprogram_binding` | 小程序绑定 |
| `fn_notifications_wechat_miniprogram_subscription` | 小程序订阅 |
| `fn_notifications_subscription_grant` | 单次本地许可、原始上报、预留租约与使用/撤回/待对账状态；不是厂商库存 |
| `fn_notifications_dingtalk_approval_sync` | DingTalk 审批同步记录 |

### 11.4 发布的集成事件

| 事件 | 消费方 |
|------|--------|
| `AnnouncementPublishedIntegrationEvent` | SignalR 实时推送（本模块内部） |
| `InboxMessageReceivedIntegrationEvent` | SignalR 实时推送（本模块内部） |
| `InboxReadStateChangedIntegrationEvent` | SignalR 实时推送（本模块内部） |

### 11.5 消费的集成事件（可选，仅 Workflow 启用时）

| 事件 | 来源 | 处理 |
|------|------|------|
| `WorkflowTodoAssignedIntegrationEvent` | Workflow | 通过内建模板生成 Inbox；外部适配器存在不等于自动跨渠道提醒 |
| `WorkflowTodoReminderRequestedIntegrationEvent` | Workflow | 催办提醒 |
| `WorkflowTodoEscalationRequestedIntegrationEvent` | Workflow | 升级提醒 |
| `WorkflowInstanceCompletedIntegrationEvent` | Workflow | 完成提醒通知 |
| `WorkflowInstanceRejectedIntegrationEvent` | Workflow | 驳回提醒通知 |
| `WorkflowInstanceCancelledIntegrationEvent` | Workflow | 取消提醒通知 |

### 11.6 依赖

- `Identity`：用户目录、`IdentityAccountChallengePurpose` 与由消费方拥有的 `IIdentityChallengeDeliveryPort`
- `Organization`：公告受众校验、机构作用域
- `Files`：通知附件（`IHostFileReferenceClaimProbe` 实现）

### 11.7 本次整改实现与证据边界（2026-10-23）

历史 `49345c3c` 的 N01–N08 缺口与逐轮失败记录保留在[同一实施计划](../docs/superpowers/plans/2026-08-30-notifications-platform-extension.md#review-2026-10-19)；不能把历史缺陷表当作当前实现状态。当前基线为 `main@5093b6f4` 加任务工作区差异。

- 投递正确性：不可变候选路由与首选激活、唯一 Match 条件物化、提前/重复/乱序回执补偿及 CAS 复判、每轮最多五十条的空闲回执恢复、持久化尝试预算和 Unknown 暂挂；发送网络丢失及模糊响应保持 Unknown，不触发 Failover。
- 发送治理：Producer/Scene 目录控制分类与渠道，偏好页面与收件端点分离；静默时区、同意撤回、消息过期、四维配额预留/核销/释放/Unknown 台账接入 Worker。负面回执可能是受理后的退信，不作为未发送证明释放额度。
- 小程序：一次客户端交互标识幂等登记一份本地授权，按用户/应用/模板/配置版本串行预留；成功核销、确定失败释放、撤回不复活旧许可、崩溃与 Unknown 暂挂。它不是厂商可信库存，不制造送达证明。
- 产品：结构化 Workflow 资源深链由目标 API 重新授权；个人/批量归档不改变已读，业务撤回单独建模并审计，与 Worker 外发边界以意图行锁串行。已跨边界或原本 Unknown 的消息不承诺收回；撤回事务释放当前世代确定未调用的预留，小程序预留使用同一意图锁阻止事后登记。路径缓存下切换深链会重新调用权威 API，旧资源迟到响应不能覆盖新资源；待办、公告与 Inbox 在 Vue 查询层组合，无跨模块 SQL。
- 控制面：按渠道/厂商/状态显示数量、最早创建时间和回执模式，保留邮件/短信 Sent 提示，提供独立权限的转死信、Intent 撤回及证据引用人工对账。确认受理只记 Sent，确认未调用释放配额但不复活旧订阅许可；积压年龄、Unknown 与死信告警独立。
- 验收状态：最新双库通知纵向用例 2/2，覆盖并发、提前/重复/乱序及后台回执恢复、预算、治理、撤回预留释放和 283/284 部分失败恢复；真实 OpenAPI 各库 1/1；通知单测 195/195，架构 87 项覆盖通过（86 项及修正后 1 项），Vue 16 文件 99 项覆盖通过；真实双库基础页面各 4/4，Match SQL Server 桌面另 1/1、MySQL 桌面及手机/键盘/登录后控制台另 1/1。窄屏抽屉和单列编辑、路径缓存深链均已整改；最新类型检查、生产构建及 Host.Api AOT 分析通过。此前 OOM/139/134 和两阶段对照保留；最新在用户授权稳定核心约束下，相同输入经 Linux SDK/ILC 10.0.402/10.0.13 完整编译、链接与 publish 成功，新 ELF 的 Notifications 原生双库 2/2、零跳过，4 分 32.140 秒（HTTP/JSON/SignalR、启动停止），没有复用两阶段对象。进程及全部 92 线程已恢复原亲和性，本机后续仍需稳定 CPU 执行条件；首页 JS 已降至 1,467,584/gzip 382,260 字节，SignalR 延迟包、Chart、VForm 独立预算均通过；完整命令和历史失败登记在原计划，本次默认 Linux ILC 发布及运行验收已取得实际证据；前次 ILC 对照未确认编译器缺陷。后续真实运行发现并修复 Worker 权益端口注册缺失；新 Worker 默认 Linux 发布成功，通知治理/预算恢复、Workflow 投影与 PDF 恢复双库 6/6、零跳过、正常停止，保留首轮 Worker 28/29 的失败记录。真实数据库采样经 Collector/Prometheus，三条通知告警保持五分钟窗口，触发与恢复 1/1 通过，未覆盖外部接收端。共享组件修复后的 API 重发曾遇 WSL VM 整体退出中断，默认链路重跑尚未完成，不能用旧 API 二进制证据关闭当前门禁；真实厂商、不可访问的外部原报告和容量验证仍单列。
- 真实厂商与容量仍单列：SMTP 历史认证失败证据保留，当前无凭据不宣称真实送达；SMTP、企微和小程序没有可信送达回执。容量保持 `Capacity-not-verified`，未新增厂商、移动推送、HTML 邮件、摘要或跨渠道编排。

---

## 十二、Workflow 工作流模块

> 项目：[`src/Modules/Full.NET.Modules.Workflow`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Workflow)
> 稳定模块键：`Workflow`
> 公开契约：[`src/Modules/Full.NET.Modules.Workflow.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Workflow.Contracts)

### 12.1 职责

- 工作流定义：草稿 / 版本 / 发布 / 业务标题模板
- 表单定义：草稿 / 版本 / 字段 Schema
- 工作流实例：启动、转交、会签、并签、自动流转、撤销、驳回、取消
- 待办：受信办理人解析（基于角色/组织/具体用户）、超时扫描、催办、升级
- 抄送：办理过程中抄送与查看
- 恢复任务：实例与待办的恢复扫描 + 处理（Worker HostedService）
- 表单附件：通过 `IHostFileReferenceClaimProbe` 引用 Files 模块
- 终端事件投影：发布 WorkflowInstance Completed/Rejected/Cancelled 给下游模块
- 通知可靠提醒：发布 TodoAssigned/Reminder/Escalated 事件

### 12.2 核心类

| 类 | 职责 |
|----|------|
| `WorkflowModule` | 模块入口；`Dependencies: Files, Identity, Notifications, Organization` |
| `WorkflowFormManagementService` | 表单定义 CRUD 与版本发布 |
| `WorkflowDefinitionManagementService` | 工作流定义 CRUD 与版本发布 |
| `WorkflowInstanceManagementService` | 实例生命周期：启动、转交、驳回、取消 |
| `WorkflowInstanceRecoveryService` | 恢复扫描与处理 |
| `WorkflowRecoveryHostedProcessor` | Worker 后台领取循环 |
| `WorkflowTodoTimeoutHostedProcessor` | 超时待办扫描与处理 |
| `WorkflowTodoTimeoutScanCursor` | 扫描游标（避免重复扫描） |
| `WorkflowAssigneeResolver` | 办理人解析：角色→组织→用户 |
| `WorkflowParallelJoinCoordinator` | 并签汇合协调 |
| `WorkflowApprovalTransitionExecutor` | 审批转交执行器 |
| `WorkflowApprovalActivationWriter` | 审批激活写入器 |
| `WorkflowCcTransitionWriter` | 抄送写入器 |
| `WorkflowNotificationOutboxPublisher` | 通知事件 Outbox 发布 |
| `IWorkflowInstanceCompletedSink` | 终端事件 Sink（供 Notifications、DataApproval、Webhooks 实现） |
| `IWorkflowInstanceRejectedSink` | 终端事件 Sink |
| `IWorkflowInstanceCancelledSink` | 终端事件 Sink |
| `IWorkflowPublishedDefinitionDirectory` | 已发布定义读取 Port |
| `IWorkflowInstanceStarter` | 实例启动 Port（供 DataApproval 等业务模块） |
| `IWorkflowInstanceCanceller` | 实例取消 Port |

### 12.3 核心表

| 表 | 说明 |
|----|------|
| `fn_workflow_definition` | 工作流定义：DefinitionKey, StatusKey, LatestPublishedVersionId |
| `fn_workflow_definition_draft` | 定义草稿：DraftJson, DraftRevision, ContentHash |
| `fn_workflow_definition_version` | 定义已发布版本：VersionNumber, DefinitionJson |
| `fn_workflow_form_definition` | 表单定义：FormKey |
| `fn_workflow_form_version` | 表单已发布版本：FieldSchemaJson |
| `fn_workflow_instance` | 工作流实例：DefinitionVersionId, StatusKey, InitiatorUserId, BusinessType, BusinessId |
| `fn_workflow_step` | 实例步骤：InstanceId, StepKey, StatusKey |
| `fn_workflow_todo` | 待办：InstanceId, RecipientUserId, StatusKey, DueAtUtc |
| `fn_workflow_cc` | 抄送：InstanceId, RecipientUserId |
| `fn_workflow_recovery_task` | 恢复任务：InstanceId, ReasonKey |
| `fn_workflow_form_submission_attachment` | 提交附件：SubmissionId, FileId |
| `fn_workflow_domain_audit` | 领域审计：InstanceId, OperationKey, ActorUserId |

### 12.4 发布的集成事件

| 事件 | 消费方 |
|------|--------|
| `WorkflowTodoAssignedIntegrationEvent` | Notifications（Inbox + 渠道提醒） |
| `WorkflowTodoReminderRequestedIntegrationEvent` | Notifications（催办） |
| `WorkflowTodoEscalationRequestedIntegrationEvent` | Notifications（升级） |
| `WorkflowInstanceCompletedIntegrationEvent` | Notifications、DataApproval、Webhooks、EnterpriseRequest（通过 Sink） |
| `WorkflowInstanceRejectedIntegrationEvent` | Notifications、DataApproval、Webhooks、EnterpriseRequest（通过 Sink） |
| `WorkflowInstanceCancelledIntegrationEvent` | Notifications、DataApproval、Webhooks、EnterpriseRequest（通过 Sink） |

### 12.5 依赖

- `Files`：表单附件（实现 `IHostFileReferenceClaimProbe`）
- `Identity`：办理人解析（用户、角色目录）、`IWorkflowRoleMemberDirectory`
- `Notifications`：可靠提醒事件 Port（发布 WorkflowTodo* 事件）
- `Organization`：组织作用域办理人候选（`WorkflowOrganizationUnitCandidateQueryService`）
- `Tenancy`：租户上下文与权益读取。权益读取放在本地事务之外

---

## 十三、Ai 智能模块

> 项目：[`src/Modules/Full.NET.Modules.Ai`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ai)
> 稳定模块键：`Ai`
> 公开契约：[`src/Modules/Full.NET.Modules.Ai.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ai.Contracts)

### 13.1 职责

- 模型配置管理：模型 Provider（OpenAI / AzureOpenAI / Ollama）、凭据保护、连通性测试
- 嵌入向量测试：Embedding 调用 + 验证
- 聊天会话：会话 CRUD、消息持久化、流式补全（SSE/AgUi）
- MCP 远程连接：注册 MCP Server、Tool 同步与审批
- MCP 工具目录：本地工具 + 远程工具（动态注册）
- Agent 运行：运行生命周期、检查点、事件、步骤、工具调用、心跳
- Agent 审批：工具调用前人工审批门禁
- Agent 委托：用户委托 Agent 操作
- 预算与配额：操作预算（按月/运行）、租户配额、模型价格
- 知识库：目录、文档版本、导入、索引任务、检索问答、引用与反馈。原文在 Files，Ai 保存版本与派生向量
- Provider 服务：`AddAiProviderServices` 由 Composition 在 API/Worker Profile 统一注册

### 13.2 核心类

| 类 | 职责 |
|----|------|
| `AiModule` | 模块入口；`Dependencies: Identity, Tenancy, Files, Organization` |
| `AiModelConfigManagementService` | 模型配置 CRUD |
| `AiModelConnectivityTester` | 模型连通性测试 |
| `AiEmbeddingTestService` | 嵌入向量测试 |
| `AiChatSessionManagementService` | 聊天会话 CRUD |
| `AiChatStreamService` | 流式聊天补全（SSE） |
| `AgUiStreamService` | AgUi 流式端点 |
| `AiMcpRemoteConnectionManagementService` | MCP 远程连接 CRUD |
| `AiMcpRemoteToolCatalog` | MCP 远程工具目录 |
| `AiAgentRunManagementService` | Agent 运行 CRUD |
| `AiAgentRunHostedProcessor` | Worker 后台 Agent 运行循环 |
| `AiAgentWorkerHeartbeatService` | Worker 心跳（API 侧读取就绪门禁） |
| `AiAgentRunReadiness` | 就绪校验（心跳 + StaleThreshold） |
| `AiAgentApprovalService` | Agent 工具调用审批 |
| `AiAgentDelegationService` | Agent 委派 |
| `AiOperationBudgetStore` | 预算存储（OpenTelemetry Metrics） |
| `AiTenantQuotaManagementService` | 租户配额管理 |
| `AiKnowledgeAskService` | 带授权范围的知识库提问入口 |
| `AiKnowledgeRetrievalService` | 先按租户、授权、活动代次和模型空间过滤，再做有界余弦排序 |
| `AiKnowledgeAnswerPipeline` | 把检索结果整理成带引用的回答 |
| `AiKnowledgeIndexHostedProcessor` | 默认关闭的文本/PDF/DOCX 索引 Worker |
| `AiKnowledgeCleanupHostedProcessor` | 默认关闭的派生索引清理 Worker |
| `AiModelBindingScope` | 凭据作用域（Scoped） |
| `IProtectedModelCredentialStore` | 凭据保护 Port |

### 13.3 核心表

| 表 | 说明 |
|----|------|
| `fn_ai_model_config` | 模型配置：ProviderKey, ModelName, EndpointUrl, CredentialProtected |
| `fn_ai_model_price` | 模型价格：ModelConfigId, Currency, InputPerMillion, OutputPerMillion |
| `fn_ai_chat_session` | 聊天会话：UserId, Title |
| `fn_ai_chat_message` | 聊天消息：SessionId, Role, Content |
| `fn_ai_mcp_remote_connection` | MCP 远程连接：ConnectionKey, EndpointUrl, ServiceTokenProtected |
| `fn_ai_mcp_remote_tool_approval` | MCP 远程工具审批 |
| `fn_ai_agent_run` | Agent 运行：StatusKey, InitiatorUserId |
| `fn_ai_agent_checkpoint` | Agent 运行检查点 |
| `fn_ai_agent_event` | Agent 事件流 |
| `fn_ai_agent_step` | Agent 步骤 |
| `fn_ai_agent_tool_call` | Agent 工具调用：ToolName, InputJson, OutputJson, ApprovalStatusKey |
| `fn_ai_agent_approval` | Agent 审批记录 |
| `fn_ai_agent_delegation` | Agent 委派记录 |
| `fn_ai_agent_worker_instance` | Worker 实例心跳注册 |
| `fn_ai_operation_budget` | 操作预算：TenantId, MonthlyRequests, MonthlyTokens |
| `fn_ai_knowledge_base` | 知识库目录、嵌入模型版本与数据分级 |
| `fn_ai_knowledge_document` / `fn_ai_knowledge_version` | 文档与不可变文件版本 |
| `fn_ai_knowledge_import` | 从 Files 导入的状态与内容摘要 |
| `fn_ai_knowledge_index_task` / `fn_ai_knowledge_index_session` | 索引任务与原会话绑定 |
| `fn_ai_knowledge_index_batch` / `fn_ai_knowledge_index_vector` | 分批向量；首版存在 SQL Server 与 MySQL，查询侧做有界精确余弦 |
| `fn_ai_knowledge_text_manifest` / `fn_ai_knowledge_docx_manifest` | 文本与 DOCX 的完整清单 |
| `fn_ai_knowledge_active_generation` | 当前可检索代次指针 |
| `fn_ai_knowledge_answer_feedback` / `fn_ai_knowledge_answer_review` | 回答反馈与独立复核 |

### 13.4 依赖

- `Identity`：用户目录、`IAgentToolApprovalBindingReader`
- `Tenancy`：租户上下文、配额作用域
- `Files`：原文与导入文件。模型调用和文件读取都在 Ai 本地事务之外
- `Organization`：知识库成员与机构授权范围
- 外部：`src/AI/Full.NET.AI.Providers.*`（OpenAI、Ollama、AzureOpenAI）
- 外部：`src/AI/Full.NET.AgenticWeb.*`（AgUi、MCP）
- 外部：`src/AI/Full.NET.Agents`（Agent 运行时）

### 13.5 知识库代码入口

实现集中在 `src/Modules/Full.NET.Modules.Ai/Knowledge/`。支持的来源类型是 UTF-8 文本、Markdown、PDF 和 DOCX。PDF 与 DOCX 走独立解析进程，不在 API 请求线程里直接解析不可信文件。

检索选型见 [ADR-0013](../docs/architecture/adr/ADR-0013-ai-retrieval-provider.md)：两库存派生向量，SQL 先按授权和代次过滤，再在代码中精确余弦排序。Qdrant 仍是候选，不是当前生产依赖。索引和缓存不代替权限检查。

本页只说明代码位置。某条知识库能力是否已经验收，以 [能力状态矩阵](../docs/roadmap/capability-status.md) 为准。操作步骤见 [知识库运行说明](../docs/operations/ai-knowledge-bases.md)。Vue 管理入口在 `ui/admin/src/views/AiKnowledgeBasesView.vue` 与 `AiKnowledgeDocumentsView.vue`。

---

## 十四、Calendar 日历模块

> 项目：[`src/Modules/Full.NET.Modules.Calendar`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Calendar)
> 稳定模块键：`Calendar`

### 14.1 职责

- 当前用户个人日程 CRUD：标题、开始/结束时间、全天、提醒、地点、备注
- 完成状态切换：标记完成 / 撤销完成
- Host 与 Tenant 会话共用权限码，行通过 `TenantId` 可空列隔离

### 14.2 核心类

| 类 | 职责 |
|----|------|
| `CalendarModule` | 模块入口；`Dependencies: Identity` |
| `PersonalScheduleQueryService` | 个人日程查询（按时间范围） |
| `PersonalScheduleManagementService` | 个人日程 CRUD + 完成切换 |

### 14.3 Features 切片

| 切片 | 说明 |
|------|------|
| `ManageMyPersonalSchedules` | 当前用户个人日程 CRUD + 完成 |

### 14.4 依赖

- `Identity`：受信用户上下文

---

## 十五、Cryptography 国密控制面模块

> 项目：[`src/Modules/Full.NET.Modules.Cryptography`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Cryptography)
> 稳定模块键：`Cryptography`

### 15.1 职责

- 国密 SM2 密钥目录：受控查看密钥状态（不暴露私钥）
- 受控签名：SM2 签名服务（业务模块通过受控 Port 调用）
- 验签服务：SM2 验签（外部数据验签）
- 不负责密钥生成与轮转（属宿主运维职责）

### 15.2 核心类

| 类 | 职责 |
|----|------|
| `CryptographyModule` | 模块入口；`Dependencies: Identity` |
| `CryptographyStatusService` | 密钥状态查询 |
| `CryptographyKeyQueryService` | 密钥元数据查询 |
| `Sm2SignatureService` | SM2 签名服务（受控调用） |
| `Sm2VerifyService` | SM2 验签服务 |

### 15.3 核心表

| 表 | 说明 |
|----|------|
| `fn_cryptography_gm_key` | 国密密钥元数据：KeyId, AlgorithmKey, StatusKey, ActivationAtUtc |

### 15.4 依赖

- `Identity`：受信用户上下文与权限校验

---

## 十六、DataApproval 数据审批模块

> 项目：[`src/Modules/Full.NET.Modules.DataApproval`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.DataApproval)
> 稳定模块键：`DataApproval`

### 16.1 职责

- 跨模块变更审批请求：业务模块发起审批 → 关联工作流实例 → 审批完成后回写
- 审批场景管理：场景 Key、关联 Workflow DefinitionKey、提交适配器
- 审批请求生命周期：提交、链接工作流、恢复（Worker HostedService）
- 工作流结果投影：监听 Workflow Completed/Rejected/Cancelled → 更新审批请求状态
- 流水号对接：可选契约依赖 `SerialNumbers`，未启用时使用降级生成

### 16.2 核心类

| 类 | 职责 |
|----|------|
| `DataApprovalModule` | 模块入口；`Dependencies: Identity, Workflow`；通过自身场景端口接收业务适配器 |
| `DataApprovalRequestService` | 审批请求 CRUD |
| `DataApprovalRequestLinkService` | 请求 → 工作流实例链接 |
| `DataApprovalRequestApplicationService` | 业务应用层（外部发起方） |
| `DataApprovalScenarioService` | 场景 CRUD |
| `DataApprovalSubmissionAdapter` | 实现 `IDataApprovalScenarioPolicyPort` + `IDataApprovalSubmissionPort` |
| `DataApprovalWorkflowOutcomeService` | 工作流终端事件 → 审批请求状态 |
| `DataApprovalRequestRecoveryHostedProcessor` | Worker 请求恢复循环 |
| `DataApprovalRequestApplicationRecoveryHostedProcessor` | Worker 应用恢复循环 |

### 16.3 核心表

| 表 | 说明 |
|----|------|
| `fn_data_approval_request` | 审批请求：ScenarioKey, BusinessType, BusinessId, InstanceId, StatusKey |
| `fn_data_approval_scenario` | 场景：ScenarioKey, WorkflowDefinitionKey |
| `fn_data_approval_application` | 业务应用关联 |
| `fn_data_approval_link` | 请求 ↔ 工作流实例链接 |
| `fn_data_approval_recovery_task` | 恢复任务 |

### 16.4 消费的集成事件

| 事件 | 来源 | 处理 |
|------|------|------|
| `WorkflowInstanceCompletedIntegrationEvent` | Workflow（通过 Sink） | 审批通过 |
| `WorkflowInstanceRejectedIntegrationEvent` | Workflow（通过 Sink） | 审批驳回 |
| `WorkflowInstanceCancelledIntegrationEvent` | Workflow（通过 Sink） | 审批取消 |

### 16.5 依赖

- `Identity`：用户上下文、权限
- `Workflow`：通过 `IWorkflowInstanceStarter` 启动实例、通过 Sink 接收终端事件
- `SerialNumbers`（可选）：审批单号生成

---

## 十七、GoView 大屏模块

> 项目：[`src/Modules/Full.NET.Modules.GoView`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.GoView)
> 稳定模块键：`GoView`
> 公开契约：[`src/Modules/Full.NET.Modules.GoView.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.GoView.Contracts)

### 17.1 职责

- 大屏项目草稿保存：JSON Schema 内容、版本、所有者
- 发布快照：草稿 → 发布版本（不可变快照）
- 只读预览：通过公开预览端点（无需登录，基于 ProjectId）

### 17.2 核心类

| 类 | 职责 |
|----|------|
| `GoViewModule` | 模块入口；`Dependencies: Identity, Tenancy` |
| `GoViewProjectQueryService` | 项目查询 |
| `GoViewProjectManagementService` | 项目 CRUD + 草稿/发布 |
| `GoViewProjectPreviewService` | 只读预览（公开端点） |

### 17.3 核心表

| 表 | 说明 |
|----|------|
| `fn_goview_project` | 大屏项目：OwnerId, Title |
| `fn_goview_project_draft` | 草稿：ProjectId, ContentJson, Revision |
| `fn_goview_project_published` | 发布快照：ProjectId, Version, ContentJson |

### 17.4 依赖

- `Identity`：用户上下文
- `Tenancy`：租户作用域

---

## 十八、ImportExport 导入导出模块

> 项目：[`src/Modules/Full.NET.Modules.ImportExport`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.ImportExport)
> 稳定模块键：`ImportExport`
> 公开契约：[`src/Modules/Full.NET.Modules.ImportExport.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.ImportExport.Contracts)

### 18.1 职责

- 静态 Schema 目录：业务模块通过 `IStaticImportSchemaHandler` 注册自己的导入 Schema
- 模板下载：按 SchemaKey 下载导入模板（Excel）
- 预校验：上传文件 → Schema 校验 → 返回错误行清单
- 批量执行：异步任务（Worker HostedService），处理进度、错误回执、成功导出
- 租户文件所有权：实现 `ITenantResourceFileOwner`，按 Files 模块契约管理产物

### 18.2 核心类

| 类 | 职责 |
|----|------|
| `ImportExportModule` | 模块入口；`Dependencies: Identity, Files, Tenancy` |
| `StaticImportSchemaRegistry` | Schema 注册目录（业务模块通过 `IStaticImportSchemaHandler` 贡献） |
| `StaticImportSchemaQueryService` | Schema 查询 |
| `ImportExportTaskManagementService` | 导入任务 CRUD |
| `ImportExportTaskExecutionService` | 任务执行（Schema 校验 + 行处理） |
| `ImportExportTaskRunner` | Worker 领取与执行循环 |
| `ImportExportTaskHostedProcessor` | Worker HostedService |
| `ImportExportResourceFileOwner` | 实现 `ITenantResourceFileOwner`（Files 模块 Port） |
| `IStaticImportSchemaHandler` | 业务模块实现以贡献 Schema（如 Organization 提供岗位导入） |

### 18.3 核心表

| 表 | 说明 |
|----|------|
| `fn_import_export_task` | 导入任务：SchemaKey, SourceFileId, StatusKey, ErrorReceiptFileId |
| `fn_import_export_template` | 模板元数据 |

### 18.4 依赖

- `Identity`：用户上下文
- `Files`：源文件与产物（实现 `ITenantResourceFileOwner`、`IHostFileReferenceClaimProbe`）
- `Tenancy`：租户作用域
- 被以下业务模块作为 Schema 贡献者：`Organization`（岗位导入）

---

## 十九、K3Cloud 金蝶 K3Cloud 模块

> 项目：[`src/Modules/Full.NET.Modules.K3Cloud`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.K3Cloud)
> 稳定模块键：`K3Cloud`
> 公开契约：[`src/Modules/Full.NET.Modules.K3Cloud.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.K3Cloud.Contracts)

### 19.1 职责

- K3Cloud 连接配置：ValidateUser 凭据管理（密码 DPAPI 保护）、连接测试
- 文档同步：固定销售订单 Save/Submit 同步到 K3Cloud
- 远程客户端：`IK3CloudWebApiClient` 接口暴露以便事务外调用可测试

### 19.2 核心类

| 类 | 职责 |
|----|------|
| `K3CloudModule` | 模块入口；`Dependencies: Identity, Tenancy` |
| `K3CloudPasswordProtector` | DPAPI 密码保护 |
| `K3CloudWebApiClient` | K3Cloud WebApi 客户端 |
| `K3CloudConnectionManagementService` | 连接配置 CRUD |
| `K3CloudConnectionOperationsService` | 连接测试 |
| `K3CloudDocumentSyncService` | 文档同步（Save/Submit） |

### 19.3 Features 切片

| 切片 | 说明 |
|------|------|
| `ManageConnectionConfigs` | K3Cloud 连接配置 CRUD + 测试 |
| `ManageDocumentSyncs` | 销售订单同步 |

### 19.4 依赖

- `Identity`：用户上下文
- `Tenancy`：租户作用域
- 外部：K3Cloud WebApi（事务外 HTTP 调用）

---

## 二十、Mqtt MQTT 控制面模块

> 项目：[`src/Modules/Full.NET.Modules.Mqtt`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Mqtt)
> 稳定模块键：`Mqtt`

### 20.1 职责

- MQTT Broker 控制：状态查询
- 客户端目录：在线客户端列表
- 消息记录：按主题、时间范围查询历史消息
- 受控发布：通过 API 发布消息到指定主题（限流策略）
- 不负责 Broker 本身（外部部署，如 EMQX）

### 20.2 核心类

| 类 | 职责 |
|----|------|
| `MqttModule` | 模块入口；`Dependencies: Identity` |
| `MqttBrokerPublisher` | Broker 发布器 |
| `MqttPublishRateLimiter` | 发布限流 |
| `MqttBrokerStatusService` | Broker 状态查询 |
| `MqttClientQueryService` | 客户端目录查询 |
| `MqttMessageQueryService` | 消息记录查询 |
| `MqttMessagePublishService` | 受控发布 |

### 20.3 Features 切片

| 切片 | 说明 |
|------|------|
| `ManageControlPlane` | 状态、客户端、消息、发布 |

### 20.4 依赖

- `Identity`：用户上下文
- 外部：MQTT Broker（HTTP API）

---

## 二十一、ObservabilityAdmin 可观测性管理模块

> 项目：[`src/Modules/Full.NET.Modules.ObservabilityAdmin`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.ObservabilityAdmin)
> 稳定模块键：`ObservabilityAdmin`

### 21.1 职责

- Host 运行日志文件只读查询（按文件名、时间范围、关键字）
- 服务器运行时监控：CPU、内存、GC、线程池指标
- 缓存策略控制面：FusionCache 策略查询与（受控）调整
- Elasticsearch 日志管道健康监控：HTTP 探针（5s 超时）

### 21.2 核心类

| 类 | 职责 |
|----|------|
| `ObservabilityAdminModule` | 模块入口；`Dependencies: Identity` |
| `LogFileControlPlane` | 日志文件控制面 |
| `ServerRuntimeReader` | 服务器运行时读取 |
| `ServerMonitorService` | 服务器监控 |
| `CachePolicyControlPlane` | 缓存策略控制面 |
| `ElasticsearchLogPipelineHealthService` | Elasticsearch 健康探针 |

### 21.3 Features 切片

| 切片 | 说明 |
|------|------|
| `ManageLogFiles` | 日志文件查询 |
| `MonitorServer` | 服务器监控 |
| `ManageCachePolicies` | 缓存策略管理 |
| `MonitorElasticsearchLogPipeline` | ES 健康监控 |

### 21.4 依赖

- `Identity`：用户上下文与精确权限
- `Settings`：诊断策略读取
- 外部：Elasticsearch（HTTP 探针，不参与业务事务）

---

## 二十二、Ocr OCR 识别模块

> 项目：[`src/Modules/Full.NET.Modules.Ocr`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ocr)
> 稳定模块键：`Ocr`
> 公开契约：[`src/Modules/Full.NET.Modules.Ocr.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Ocr.Contracts)

### 22.1 职责

- OCR Provider 配置：PaddleOCR 等渠道，API Key 保护
- 身份证识别任务：上传图片 → 识别 → 返回结构化字段
- 人工确认流程：识别结果供用户校验与确认
- 远程客户端：`IPaddleOcrIdCardClient` 接口暴露以便事务外调用可测试

### 22.2 核心类

| 类 | 职责 |
|----|------|
| `OcrModule` | 模块入口；`Dependencies: Identity, Tenancy, Files` |
| `OcrApiKeyProtector` | API Key DPAPI 保护 |
| `PaddleOcrIdCardClient` | PaddleOCR HTTP 客户端 |
| `OcrProviderManagementService` | Provider 配置 CRUD |
| `OcrProviderOperationsService` | Provider 连接测试 |
| `OcrProviderSecretResolver` | 凭据解析 |
| `OcrIdCardTaskService` | 身份证识别任务 CRUD + 调度 |
| `OcrIdCardTaskQueryService` | 任务查询 |

### 22.3 核心表

| 表 | 说明 |
|----|------|
| `fn_ocr_provider` | Provider 配置：ProviderKey, EndpointUrl, ApiKeyProtected |
| `fn_ocr_id_card_task` | 身份证识别任务：ImageFileId, StatusKey, ResultJson |

### 22.4 依赖

- `Identity`：用户上下文
- `Tenancy`：租户作用域
- `Files`：识别图片存储（`IHostFileReferenceClaimProbe`）
- 外部：PaddleOCR 服务

---

## 二十三、Payments 支付模块

> 项目：[`src/Modules/Full.NET.Modules.Payments`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Payments)
> 稳定模块键：`Payments`
> 公开契约：[`src/Modules/Full.NET.Modules.Payments.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Payments.Contracts)

### 23.1 职责

- 商户配置：微信支付、支付宝商户凭据（DPAPI 保护）
- 订单管理：Native 支付下单、Page Pay 下单、查询、对账
- 退款：退款单 CRUD、状态同步
- 微信回调：异步通知接收、验签、订单状态更新
- 渠道客户端：`IWeChatNativePayClient`、`IAlipayPagePayClient` 接口暴露以便事务外调用可测试
- 微信平台证书解析：`IWeChatPayPlatformCertificateResolver`

### 23.2 核心类

| 类 | 职责 |
|----|------|
| `PaymentsModule` | 模块入口；`Dependencies: Identity, Tenancy` |
| `PaymentSecretProtector` | 商户凭据保护 |
| `WeChatNativePayClient` | 微信 Native 支付 HTTP 客户端 |
| `AlipayPagePayClient` | 支付宝 Page Pay HTTP 客户端 |
| `WeChatPayPlatformCertificateResolver` | 微信平台证书解析 |
| `PaymentMerchantConfigManagementService` | 商户配置 CRUD |
| `PaymentOrderManagementService` | 订单 CRUD |
| `PaymentOrderReconciliationService` | 订单对账 |
| `PaymentWeChatNotifyService` | 微信异步通知处理 |
| `PaymentRefundManagementService` | 退款单 CRUD |
| `TenantSubscriptionFulfillment` | 租户订阅履约（供 Tenancy 调用） |

### 23.3 核心表

| 表 | 说明 |
|----|------|
| `fn_payments_merchant_config` | 商户配置：ChannelKey, MerchantId, CredentialProtected |
| `fn_payments_order` | 订单：ChannelKey, OutTradeNo, Amount, StatusKey |
| `fn_payments_refund` | 退款：OrderId, RefundNo, Amount, StatusKey |
| `fn_payments_notify_receipt` | 渠道通知回执：OrderId, RawPayload, ProcessedAt |

### 23.4 Features 切片

| 切片 | 说明 |
|------|------|
| `ManageMerchantConfigs` | 商户配置 CRUD |
| `ManageOrders` | 订单 CRUD + 对账 |
| `ManageRefunds` | 退款 CRUD |
| `ReceiveWeChatNotify` | 微信异步通知 |
| `TenantSubscriptionFulfillment` | 租户订阅履约（内部 Port） |

### 23.5 依赖

- `Identity`：用户上下文
- `Tenancy`：租户作用域
- 外部：微信支付 API、支付宝 API（事务外 HTTP 调用）

---

## 二十四、Platform 平台模块

> 项目：[`src/Modules/Full.NET.Modules.Platform`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Platform)
> 稳定模块键：`Platform`

### 24.1 职责

- Host 更新日志：发布、版本、内容、已读状态
- 用户已读：终端用户标记已读、查询未读
- 备份执行器：宿主备份任务状态查询、运行记录、产物管理

### 24.2 核心类

| 类 | 职责 |
|----|------|
| `PlatformModule` | 模块入口；`Dependencies: Identity` |
| `HostReleaseNoteQueryService` | 更新日志查询 |
| `HostReleaseNoteManagementService` | 更新日志 CRUD |
| `MyReleaseNoteQueryService` | 用户已读查询 |
| `MyReleaseNoteManagementService` | 标记已读 |
| `BackupExecutorStatusService` | 备份执行器状态 |
| `BackupTaskQueryService` | 备份任务查询 |
| `BackupRunQueryService` | 备份运行记录 |
| `BackupRunArtifactService` | 备份产物管理 |

### 24.3 核心表

| 表 | 说明 |
|----|------|
| `fn_platform_release_note` | 更新日志：Version, Title, Body, PublishedAtUtc |
| `fn_platform_release_note_read` | 用户已读 |
| `fn_platform_backup_task` | 备份任务 |
| `fn_platform_backup_run` | 备份运行记录 |
| `fn_platform_backup_run_artifact` | 备份产物 |

### 24.4 依赖

- `Identity`：用户上下文

---

## 二十五、Printing 打印模板模块

> 项目：[`src/Modules/Full.NET.Modules.Printing`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Printing)
> 稳定模块键：`Printing`
> 公开契约：[`src/Modules/Full.NET.Modules.Printing.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Printing.Contracts)

### 25.1 职责

- 固定表单 Schema：内置表单字段目录（不可变）
- 打印模板：版本管理、内容（HTML/JSON）、字段绑定
- 浏览器预览：基于绑定数据预览打印模板（无后端渲染）
- 租户绑定：供 Tenancy 模块通过 `IPrintingTenantProfileBindingSource` 读取打印配置

### 25.2 核心类

| 类 | 职责 |
|----|------|
| `PrintingModule` | 模块入口；`Dependencies: Identity, Tenancy` |
| `PrintingFormSchemaQueryService` | 表单 Schema 查询 |
| `PrintingTemplateQueryService` | 模板查询 |
| `PrintingTemplateManagementService` | 模板 CRUD + 版本 |
| `PrintingFormBindingService` | 模板 ↔ 表单 Schema 绑定 |
| `PrintingTemplatePreviewService` | 浏览器预览 |
| `IPrintingTenantProfileBindingSource` | 供 Tenancy 读取打印配置的 Port（`Printing.Contracts`） |

### 25.3 核心表

| 表 | 说明 |
|----|------|
| `fn_printing_template` | 打印模板：TemplateKey, FormSchemaKey, LatestPublishedVersionId |
| `fn_printing_template_version` | 模板已发布版本 |
| `fn_printing_form_schema` | 表单 Schema 元数据（内置） |

### 25.4 依赖

- `Identity`：用户上下文
- `Tenancy`：租户作用域
- 被 `Tenancy` 作为可选契约依赖（`OptionalContractDependencies: Printing`）

---

## 二十六、Regions 行政区划模块

> 项目：[`src/Modules/Full.NET.Modules.Regions`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Regions)
> 稳定模块键：`Regions`

### 26.1 职责

- 行政区划参考数据：省、市、区县、乡镇（Host 级只读目录）
- 级联查询：按 ParentCode 查询下级
- 数据集导入：可审查的批量导入（审计）
- Baseline 种子：`RegionsAdministrativeBaselineSeedContributor` 提供 Production 安全 Baseline

### 26.2 核心类

| 类 | 职责 |
|----|------|
| `RegionsModule` | 模块入口；`Dependencies: Identity` |
| `AdministrativeRegionQueryService` | 区划查询（按 Parent、Level） |
| `AdministrativeRegionManagementService` | 区划 CRUD（受控） |
| `AdministrativeRegionImportService` | 批量导入 |
| `RegionsAdministrativeBaselineSeedContributor` | Production 安全 Baseline 种子 |

### 26.3 核心表

| 表 | 说明 |
|----|------|
| `fn_regions_administrative_region` | 行政区划：Code, ParentCode, Name, LevelKey, Sort |

### 26.4 依赖

- `Identity`：用户上下文
- 在 Migrator Profile 通过 `AddMigrationServices` 注册 Baseline 种子

---

## 二十七、Reporting 报表模块

> 项目：[`src/Modules/Full.NET.Modules.Reporting`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Reporting)
> 稳定模块键：`Reporting`
> 公开契约：[`src/Modules/Full.NET.Modules.Reporting.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Reporting.Contracts)

### 27.1 职责

- 报表数据源：JDBC/ODBC 连接配置、Secret 保护、连通性测试
- 报表分组：树形组织报表定义
- 报表定义：SQL/JSON 定义、参数 Schema、布局配置
- 报表执行：按定义执行 SQL → 返回结构化结果
- 报表导出：异步任务（Worker HostedService），导出 Excel 文件
- 查询端口目录：暴露可执行的查询端口（受控）

### 27.2 核心类

| 类 | 职责 |
|----|------|
| `ReportingModule` | 模块入口；`Dependencies: Identity, Files, Tenancy` |
| `ReportingDataSourceSecretProtector` | 数据源凭据保护 |
| `ReportingDataSourceConnectionFactory` | 数据源连接工厂 |
| `ReportingDataSourceConnectionTester` | 连通性测试 |
| `ReportingDataSourceManagementService` | 数据源 CRUD |
| `ReportingGroupManagementService` | 分组 CRUD |
| `ReportingDefinitionManagementService` | 报表定义 CRUD |
| `ReportingDefinitionExecutionService` | 报表执行 |
| `ReportingExportTaskRunner` | Worker 导出循环 |
| `ReportingExportTaskHostedProcessor` | Worker HostedService |
| `ReportingResourceFileOwner` | 实现 `ITenantResourceFileOwner`（导出产物） |
| `ReportingQueryPortQueryService` | 查询端口目录 |
| `IReportingExportWorkbookSource` | 导出 Workbook 源 |

### 27.3 核心表

| 表 | 说明 |
|----|------|
| `fn_reporting_data_source` | 数据源：Name, ConnectionStringProtected, DriverKey |
| `fn_reporting_group` | 分组：ParentId, Name |
| `fn_reporting_definition` | 报表定义：GroupKey, DefinitionKey, SqlOrJson, ParameterSchemaJson |
| `fn_reporting_export_task` | 导出任务：DefinitionId, StatusKey, OutputFileId |
| `fn_reporting_query_port` | 查询端口元数据 |

### 27.4 Features 切片

| 切片 | 说明 |
|------|------|
| `ManageDataSources` | 数据源 CRUD + 测试 |
| `ManageGroups` | 分组 CRUD |
| `BrowseQueryPorts` | 查询端口浏览 |
| `ManageDefinitions` | 报表定义 CRUD |
| `ExecuteDefinitions` | 报表执行 |
| `ManageExportTasks` | 导出任务管理 |

### 27.5 依赖

- `Identity`：用户上下文
- `Files`：导出产物（实现 `ITenantResourceFileOwner`）
- `Tenancy`：租户作用域
- 外部：业务数据库（仅读，凭据保护）

---

## 二十八、Webhooks 模块

> 项目：[`src/Modules/Full.NET.Modules.Webhooks`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Webhooks)
> 稳定模块键：`Webhooks`

### 28.1 职责

- Webhook 订阅：URL、签名密钥（DPAPI 保护）、事件类型、状态
- 事件入队：业务事件 → Webhook 待投递队列
- 投递 Worker：批量领取（Worker HostedService），重试、超时、签名（HMAC-SHA256）
- HTTP 客户端：固定 30s 超时、64KB 响应缓冲、禁止自动重定向
- Workflow 事件订阅：可选契约依赖 `Workflow`，监听 InstanceCompleted 事件触发 Webhook

### 28.2 核心类

| 类 | 职责 |
|----|------|
| `WebhooksModule` | 模块入口；`Dependencies: Identity, Tenancy`、`OptionalContractDependencies: Workflow` |
| `WebhookSigningSecretProtector` | 签名密钥保护 |
| `WebhookSubscriptionManagementService` | 订阅 CRUD |
| `WebhookEventEnqueueService` | 事件入队 |
| `WebhookDeliveryBatchProcessor` | 投递批处理 |
| `WebhookDeliveryHostedProcessor` | Worker HostedService |
| `WorkflowInstanceCompletedWebhookHandler` | 监听 Workflow 终端事件 → 触发 Webhook |

### 28.3 核心表

| 表 | 说明 |
|----|------|
| `fn_webhooks_subscription` | 订阅：Url, EventTypeKey, SigningSecretProtected, StatusKey |
| `fn_webhooks_delivery` | 投递记录：SubscriptionId, Payload, StatusKey, AttemptCount |

### 28.4 消费的集成事件（可选）

| 事件 | 来源 | 处理 |
|------|------|------|
| `WorkflowInstanceCompletedIntegrationEvent` | Workflow | 触发匹配订阅的 Webhook 投递 |

### 28.5 依赖

- `Identity`：用户上下文
- `Tenancy`：租户作用域
- `Workflow`（可选）：监听终端事件

---

## 二十九、EnterpriseRequest 企业请求样板模块（sample）

> 项目：[`samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest`](file:///G:/wwwroot/github_fork/Full.NET/samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest)
> 稳定模块键：`EnterpriseRequest`；应用显式装配，官方 Full / Enterprise 预设不包含该示例，迁移资源由示例程序集拥有。

### 29.1 职责

- 跨模块业务请求样板：演示如何组合 Identity + Tenancy + Organization + Files + Workflow + ImportExport 编排一个完整的"提交审批 → 工作流 → 导入"流程
- 提交审批：`SubmitEnterpriseRequestForApprovalService` 创建请求并启动工作流
- 工作流结果投影：监听 Workflow Completed/Rejected/Cancelled → 更新请求状态
- 静态导入 Schema：实现 `IStaticImportSchemaHandler` 向 ImportExport 贡献导入 Schema

### 29.2 关键事实

- 位于 `samples/` 目录，不是 `src/Modules/`，但已注册到 `FullNetModuleCatalog.CreateAllModules()` 与 `OfficialModuleNames`
- 被架构测试 `DependencyRulesTests` 验证依赖闭包
- `Dependencies: Identity, Tenancy, Organization, Files, Workflow, ImportExport`
- 是脚手架生成产物（`AddFullNetGeneratedModuleFeatures`、`MapFullNetGeneratedModuleFeatures`）

### 29.3 跨模块 Port 实现

| Port | 来源 Contracts | 实现 |
|------|---------------|------|
| `IStaticImportSchemaHandler` | ImportExport.Contracts | `EnterpriseRequestStaticImportSchemaHandler` |
| `IWorkflowInstanceCompletedSink` | Workflow.Contracts | `WorkflowInstanceCompletedEnterpriseRequestSink` |
| `IWorkflowInstanceRejectedSink` | Workflow.Contracts | `WorkflowInstanceRejectedEnterpriseRequestSink` |
| `IWorkflowInstanceCancelledSink` | Workflow.Contracts | `WorkflowInstanceCancelledEnterpriseRequestSink` |

---

## 附录：模块依赖速查

### A.1 模块依赖矩阵（按 Dependencies 字段）

| 模块 | 硬依赖 | 可选契约依赖 |
|------|--------|--------------|
| `Identity` | （根，无） | Files |
| `Tenancy` | Identity | Files, Printing |
| `Organization` | Identity, Tenancy | ImportExport |
| `Settings` | Identity | （无） |
| `Auditing` | Identity | （无） |
| `Files` | Identity | （无） |
| `Document` | Identity, Files | （无） |
| `Notifications` | Identity, Organization, Files | Workflow |
| `Calendar` | Identity | （无） |
| `Platform` | Identity | （无） |
| `Regions` | Identity | （无） |
| `Jobs` | Identity, Settings | （无） |
| `Messaging` | Identity | （无） |
| `CodeGeneration` | Identity | （无） |
| `SerialNumbers` | Identity, DataApproval | （无） |
| `ImportExport` | Identity, Files, Tenancy | （无） |
| `Reporting` | Identity, Files, Tenancy | （无） |
| `Printing` | Identity, Tenancy | （无） |
| `Ai` | Identity, Tenancy, Files, Organization | （无） |
| `Payments` | Identity, Tenancy | （无） |
| `GoView` | Identity, Tenancy | （无） |
| `K3Cloud` | Identity, Tenancy | （无） |
| `Ocr` | Identity, Tenancy, Files | （无） |
| `ObservabilityAdmin` | Identity, Settings | （无） |
| `Workflow` | Files, Identity, Notifications, Organization, Tenancy | （无） |
| `DataApproval` | Identity, Workflow | （无） |
| `Mqtt` | Identity | （无） |
| `Webhooks` | Identity, Tenancy | Workflow |
| `Cryptography` | Identity | （无） |
| `EnterpriseRequest`（sample） | Identity, Tenancy, Organization, Files, Workflow, ImportExport | （无） |

### A.2 模块预设速查（与 `FullNetModuleSelection` 一致）

| 预设 | 包含模块 |
|------|----------|
| `Minimal` | Identity, Tenancy, Settings, Organization |
| `Platform` | Minimal + Auditing, Files, Notifications, Calendar, Platform, Regions, Jobs, Messaging, ObservabilityAdmin, Mqtt, Cryptography |
| `Content` | Platform + Document |
| `Saas` | Platform + Payments, Webhooks |
| `Enterprise` | Platform + Webhooks, Workflow, ImportExport, Reporting, Printing |
| `Full`（默认） | OfficialModuleNames 全集（29 个官方稳定键） |

> 预设裁剪仅决定运行时注册子集；模块源码、迁移脚本、Contracts 项目编译闭包不受预设影响。

### A.3 跨模块 Port 实现速查

| Port | 定义 Contracts | 实现模块 | 用途 |
|------|---------------|----------|------|
| `IHostFileReferenceClaimProbe` | Files.Contracts | Identity、Tenancy、Document、Workflow、Notifications、Ocr、Reporting | 事务内声明文件引用 |
| `ITenantResourceFileOwner` | Files.Contracts | ImportExport、Reporting | 租户文件所有权（产物） |
| `IHostFileRetentionContributor` | Files.Contracts | Tenancy | 租户 Logo 文件保留 |
| `IIdentityChallengeDeliveryPort` | Identity.Contracts | Notifications | Identity 账号挑战邮件投递 |
| `IWorkflowInstanceStarter` | Workflow.Contracts | Workflow（主项目） | 业务模块启动工作流 |
| `IWorkflowInstanceCanceller` | Workflow.Contracts | Workflow（主项目） | 业务模块取消工作流 |
| `IWorkflowInstanceCompletedSink` | Workflow.Contracts | Notifications、DataApproval、Webhooks、EnterpriseRequest | 完成事件回调 |
| `IWorkflowInstanceRejectedSink` | Workflow.Contracts | Notifications、DataApproval、Webhooks、EnterpriseRequest | 驳回事件回调 |
| `IWorkflowInstanceCancelledSink` | Workflow.Contracts | Notifications、DataApproval、Webhooks、EnterpriseRequest | 取消事件回调 |
| `IStaticImportSchemaHandler` | ImportExport.Contracts | Organization、EnterpriseRequest | 业务模块贡献导入 Schema |
| `IPrintingTenantProfileBindingSource` | Printing.Contracts | Tenancy | 租户打印配置读取 |
| `ITenantSubscriptionPaymentFulfillmentPort` | Tenancy.Contracts | Payments | 租户订阅履约 |
| `IDataApprovalSubmissionPort` | DataApproval.Contracts | DataApproval（主项目） | 业务模块提交审批 |
| `IDataApprovalScenarioPolicyPort` | DataApproval.Contracts | DataApproval（主项目） | 审批场景策略 |
