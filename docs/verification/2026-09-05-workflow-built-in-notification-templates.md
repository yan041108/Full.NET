# Workflow 内建通知模板验证记录

## 结论

- 状态：`Build-verified`，不提升为 `Verified`。
- 四类 Workflow 生命周期事件均有闭合的安全默认站内信模板：`workflow.todo.assigned`、`workflow.instance.completed`、`workflow.instance.rejected`、`workflow.instance.cancelled`。
- 首次事件消费发现模板缺失时，Notifications 在 Envelope 派生的可信 Host/Tenant 作用域内，用一个本地事务创建模板、不可变版本 1 和发布指针；随后 Intent 才固定该版本。
- 常态路径只查询并复用已有发布版本，不开启补齐事务。已有同名未发布草稿保持管理员权威，系统不覆盖也不自动发布。

## 边界说明

- 这不是 API/Worker 启动播种；只有真实 Workflow 事件缺少模板时才执行，因而覆盖历史租户和后续新租户，无须跨模块枚举 `fn_tenancy_*`。
- 所有写入仅涉及 Notifications 自有表，TenantRequired SQL 继续由 Worker 已建立的受信租户上下文绑定 `TenantId`。
- 默认正文只使用业务类型、业务标识等有界参数，不接收 HTML、脚本、外部 URL、Secret 或表单全文。
- 当前默认内容为中文；Notifications 业务模板翻译表和接收者语言选择尚未交付，不把本切片表述为多语言完成。

## TDD 证据

- 首轮 RED：`WorkflowNotificationTemplateProvisioner` 与所需行为不存在，聚焦测试因类型缺失编译失败。
- 注册 RED：后台服务测试明确因未注册 provisioner 失败。
- 热路径 RED：已发布模板场景最初仍打开事务，新增零事务断言失败；实现事务外只读快路径后转绿。
- 最终聚焦命令：`dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~WorkflowNotificationTemplateProvisionerTests|FullyQualifiedName~NotificationsModuleRegistrationTests|FullyQualifiedName~WorkflowNotificationRequestFactoryTests" --no-restore --verbosity minimal`，12/12 通过。

## Fresh 本地证据

- `dotnet build Full.NET.slnx -c Release --no-restore -m:1 -nodeReuse:false --verbosity minimal`：成功，0 警告、0 错误。
- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore`：成功，0 警告、0 错误。
- `pnpm test:naming`：30/30 通过。
- `pnpm test:aot:analyzers`：成功，0 警告、0 错误。
- `pnpm test:dotnet:architecture -- --selection api-native-aot`：73/73 通过。
- `pnpm test:integration:partitions`：643 项，无遗漏或重复。
- `pnpm test:governance`：52/52 通过。
- `pnpm test:integration:affected:plan -- --snapshot workflow-built-in-notification-templates-20260905 --phase slice`：选择 `integration-matrix, Notifications`，预计约 3 分钟；环境重型执行留给提交后的 GitHub Actions。

## 未关闭项

- 尚未通过 SQL Server/MySQL Worker 外部进程证明“Workflow Outbox → 模板首次初始化 → Intent/Recipient → Inbox”完整链路。
- 并发首次初始化的真实唯一约束竞态、Worker 死信恢复、通知模板多语言、页面级真实栈和人工产品验收仍开放。
- 按 `R-20260905-feature-first-page-acceptance`，当前继续推进后端功能闭环，不强制逐页面验收。

## 规则与 Skill 演进

- 规则演进未触发：本轮落实已有功能优先与种子数据边界，没有出现新的重复失败或规则冲突。
- Skill 演进未触发：`fullnet-module-delivery` 已覆盖模块归属、租户 SQL、事务与验证要求。
