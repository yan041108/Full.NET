# Workflow 到 Notifications 异步提醒投影验证记录

## 结论

- 状态：`Build-verified`，不提升为 `Verified`。
- Workflow 在首次待办到达、后续待办到达、实例完成、实例驳回和实例取消的本地状态事务中，分别追加版本 1 的 MemoryPack Outbox 事件。
- Notifications Worker 注册四个闭合 Handler，使用可信 Envelope TenantId 构造作用域，并把 Outbox MessageId 映射为 `workflow-{messageId:N}` Intent 幂等键；重复交付由现有 `(TenantScopeKey, ProducerKey, IdempotencyKey)` 唯一约束收敛。
- 事件只携带实例、待办、收件人、业务类型/标识和发生时间，不携带表单全文、任意 HTML、Secret 或外部 Provider 参数。
- 对应 Host/Tenant 作用域必须预先发布 `workflow.todo.assigned`、`workflow.instance.completed`、`workflow.instance.rejected`、`workflow.instance.cancelled` 模板；缺失模板时 Handler 失败并进入既有 Outbox 重试/死信链路，不静默丢消息。

## 架构边界

- 首个真实编译期消费者出现后，新建 `Full.NET.Modules.Workflow.Contracts`，只承载稳定事件契约；Notifications 不引用 Workflow 主项目。
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
- 下一切片补齐四个内建提醒模板的生产安全 Baseline/租户 Overlay 策略及 Worker 端到端投影测试；在此之前不能宣称开箱即发送。
- 页面级真实栈 E2E 和逐页人工验收按 `R-20260905-feature-first-page-acceptance` 延后到功能建设完成后统一执行。
- Worker 卡住实例恢复、超时提醒、重新指派、生产容量和外部渠道送达回执仍开放。

## 规则与 Skill 演进

- 规则演进未再次触发；本轮直接执行已新增的功能优先、页面后验收规则。
- Skill 演进未触发；`fullnet-module-delivery` 已覆盖 Contracts、Outbox、消费幂等和 AOT 静态闭包要求。
