# Workflow 到 Notifications 异步提醒投影验证记录

## 结论

- 状态：`Build-verified`，不提升为 `Verified`。
- Workflow 在首次待办到达、后续待办到达、实例完成、实例驳回和实例取消的本地状态事务中，分别追加版本 1 的 MemoryPack Outbox 事件。
- Notifications Worker 注册四个闭合 Handler，使用可信 Envelope TenantId 构造作用域，并把 Outbox MessageId 映射为 `workflow-{messageId:N}` Intent 幂等键；重复交付由现有 `(TenantScopeKey, ProducerKey, IdempotencyKey)` 唯一约束收敛。
- 事件只携带实例、待办、收件人、业务类型/标识和发生时间，不携带表单全文、任意 HTML、Secret 或外部 Provider 参数。
- Notifications 已为 `workflow.todo.assigned`、`workflow.instance.completed`、`workflow.instance.rejected`、`workflow.instance.cancelled` 提供闭合的安全默认模板；首次事件消费会在对应 Host/Tenant 作用域内原子创建模板、不可变首版与发布指针，管理员无需预配置即可形成站内信。
- 已发布的同名租户模板保持权威并原样复用；已存在但未发布的同名草稿不会被系统覆盖或代为发布，Handler 失败后进入既有 Outbox 重试/死信链路。
- 模板补齐的 TDD、构建与未验证边界见[内建通知模板验证](2026-09-05-workflow-built-in-notification-templates.md)。

## 架构边界

- 首个真实编译期消费者出现后，新建 `Full.NET.Modules.Workflow.Contracts`，只承载稳定事件契约；Notifications 不引用 Workflow 主项目。
- Notifications 将 Workflow 登记为 `OptionalContractDependencies` 事件生产者而非运行时硬依赖；裁剪部署未启用 Workflow 时，通知中心仍可独立提供公告、站内信和渠道投递，且不会尝试同步解析 Workflow 服务。
- Workflow 只写自身状态和通用 Outbox；Notifications 只写 `fn_notifications_*`，双方不跨模块 JOIN、外键或共享本地事务。
- Workflow 事务成功不等待 Notifications、SignalR 或外部渠道；通知投影失败不会回滚审批事实。
- Worker 仅追加 Identity 的最小 `IHostUserDirectory`，用于投影前确认收件人仍为活动用户，不装配完整 Identity HTTP 域。

## Fresh 本地证据

- `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter FullyQualifiedName~MemoryPackControlledProtocolRulesTests`：3/3 通过。
- `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~NotificationsModuleRegistrationTests|FullyQualifiedName~WorkflowNotificationRequestFactoryTests|FullyQualifiedName~WorkflowNotificationOutboxPublisherTests"`：6/6 通过。
- `dotnet build Full.NET.slnx -c Release`：成功，0 警告、0 错误。
- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release`：成功，0 警告、0 错误；双库测试新增四类 Outbox 载荷和幂等回放断言。
- `pnpm test:naming`：30/30 通过。
- `pnpm test:aot:analyzers`：成功，0 警告、0 错误。
- `pnpm test:dotnet:architecture -- --selection api-native-aot`：73/73 通过。
- `pnpm test:integration:partitions`：api-sqlserver 66、api-mysql 66、migrations 326、infrastructure 128、messaging-heavy 57，合计 643，无遗漏或重复。
- `pnpm test:governance`：52/52 通过。
- `pnpm test:inner -- --snapshot workflow-notification-projection-20260905`：选择器进入 Identity/MySQL 环境聚焦集后长时间无输出，按功能优先策略人工停止；不得表述为通过。

## Actions 与后续边界

- `pnpm test:integration:affected:plan -- --snapshot workflow-notification-projection-20260905 --phase slice` 已识别 Identity、Notifications、Workflow 和 Workflow 双库 Integration；提交推送后由 GitHub Actions 承担环境重型验证。
- 四个内建提醒模板已采用事件消费时的幂等作用域初始化，覆盖既有租户与新租户，同时不属于 API/Worker 启动播种，也不跨模块枚举租户。Worker 双库端到端投影测试仍为下一切片。
- 页面级真实栈 E2E 和逐页人工验收按 `R-20260905-feature-first-page-acceptance` 延后到功能建设完成后统一执行。
- Worker 卡住实例恢复、超时提醒、重新指派、生产容量和外部渠道送达回执仍开放。

## 规则与 Skill 演进

- 规则演进未再次触发；本轮直接执行已新增的功能优先、页面后验收规则。
- Skill 演进未触发；`fullnet-module-delivery` 已覆盖 Contracts、Outbox、消费幂等和 AOT 静态闭包要求。
