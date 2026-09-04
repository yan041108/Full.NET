# Workflow 到 Notifications 异步提醒投影 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 工作流待办到达及实例完成、驳回、取消时，以事务 Outbox 发布版本化事件，并由 Notifications 幂等创建通知 Intent 与站内信。

**Architecture:** 新建有真实跨模块消费者的 `Full.NET.Modules.Workflow.Contracts`，只承载 MemoryPack 事件与稳定消息类型。Workflow 在状态事务内写 Outbox；Notifications Handler 只读取事件和自身表，通过显式可信作用域调用现有 Intent 管道，不读取 Workflow 表、不在 Workflow 事务中同步调用通知模块。

**Tech Stack:** .NET 10、MemoryPack、Dapper、事务 Outbox/Inbox、MSTest、SQL Server/MySQL 既有 Notifications 数据模型。

## Global Constraints

- Workflow 生产 SQL 只访问 `fn_workflow_*`，Notifications 生产 SQL 只访问 `fn_notifications_*`。
- 稳定消息类型采用 `fullnet.workflow.<entity>.<event>`；SchemaVersion 固定为 `1`。
- 事件只携带稳定标识、接收人、业务键和有界模板参数，不携带表单全文、任意 HTML 或 Secret。
- 通知失败不得回滚已提交的工作流事实；至少一次投递通过 Notifications 的 `(Scope, ProducerKey, IdempotencyKey)` 收敛。
- 本阶段不以页面真实栈 E2E 全绿为退出条件，能力状态保持 `Build-verified`。
- 所有新增/修改后端类型、构造函数、方法、参数与关键业务代码块使用中文 XML/块注释。

---

### Task 1: 冻结跨模块提醒事件契约

**Files:**
- Create: `src/Modules/Full.NET.Modules.Workflow.Contracts/Full.NET.Modules.Workflow.Contracts.csproj`
- Create: `src/Modules/Full.NET.Modules.Workflow.Contracts/WorkflowNotificationIntegrationEvents.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Full.NET.Modules.Workflow.csproj`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Full.NET.Modules.Notifications.csproj`
- Modify: `Full.NET.slnx`
- Test: `tests/Full.NET.ArchitectureTests/MemoryPackControlledProtocolRulesTests.cs`

**Interfaces:**
- Produces: `WorkflowNotificationIntegrationEventTypes` 与四个具体 `[MemoryPackable] partial record`：待办分配、实例完成、实例驳回、实例取消。
- Consumes: `MemoryPack` 生成器与现有 `IIntegrationEventSerializer`。

- [ ] **Step 1: Write the failing test**

在 `ProductionIntegrationEventTypes` 和 round-trip 样本中加入四个 Workflow 事件，并断言消息类型分别为 `fullnet.workflow.todo.assigned`、`fullnet.workflow.instance.completed`、`fullnet.workflow.instance.rejected`、`fullnet.workflow.instance.cancelled`。

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter FullyQualifiedName~MemoryPackControlledProtocolRulesTests`

Expected: FAIL，原因是 Workflow Contracts 项目和事件类型尚不存在。

- [ ] **Step 3: Write minimal implementation**

事件的共同字段为 `InstanceId`、`RecipientUserId`、`BusinessType`、`BusinessId`、`OccurredAtUtc`；待办事件额外包含 `TodoId`。每个公开类型、常量和 record 参数写中文 XML 注释。

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter FullyQualifiedName~MemoryPackControlledProtocolRulesTests`

Expected: PASS。

### Task 2: Workflow 与状态事务原子发布提醒事件

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageInstances/WorkflowInstanceManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyTodos/WorkflowTodoManagementService.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowNotificationOutboxPublisherTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Workflow/WorkflowRuntimeApiAssertions.cs`

**Interfaces:**
- Consumes: Task 1 的四种事件及 `IOutboxWriter.AddAsync(...)`。
- Produces: 与 Workflow 本地状态同事务持久化的 Outbox 记录。

- [ ] **Step 1: Write the failing tests**

新增 Publisher 单元断言和双库 API Integration 断言：首次启动发布 Assigned；多级批准发布下一待办 Assigned；终态批准发布 Completed 给发起人；驳回发布 Rejected 给发起人；取消发布 Cancelled 给发起人；幂等回放不重复写入匹配实例的事件。

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~WorkflowNotificationOutboxPublisherTests`

Expected: FAIL，收到的 Workflow Outbox 调用为 0。

- [ ] **Step 3: Write minimal implementation**

向两个服务注入 `IOutboxWriter`；仅在首次状态写入成功后，使用实例 Id 作为 partition key、`fullnet.workflow` 作为 producer 写入对应事件。TenantId 继续由当前事务上下文的 Outbox writer 固化到 Envelope；载荷不复制表单内容。

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~WorkflowNotificationOutboxPublisherTests`

Expected: PASS。

### Task 3: Notifications 幂等消费 Workflow 提醒

**Files:**
- Create: `src/Modules/Full.NET.Modules.Notifications/Features/ProjectWorkflowNotifications/WorkflowNotificationIntegrationEventHandlers.cs`
- Create: `src/Modules/Full.NET.Modules.Notifications/Features/ProjectWorkflowNotifications/WorkflowNotificationProjectionService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/NotificationInboxScope.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/CreateNotificationIntents/NotificationIntentService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/NotificationsModule.cs`
- Test: `tests/Full.NET.UnitTests/Notifications/WorkflowNotificationRequestFactoryTests.cs`
- Test: `tests/Full.NET.UnitTests/Notifications/NotificationsModuleRegistrationTests.cs`

**Interfaces:**
- Consumes: Task 1 的四种事件、`IntegrationEventContext.MessageId/TenantId`、已发布模板键 `workflow.todo.assigned`、`workflow.instance.completed`、`workflow.instance.rejected`、`workflow.instance.cancelled`。
- Produces: ProducerKey=`workflow`、场景键与模板键一致、IdempotencyKey=`workflow-{MessageId:N}` 的 Notification Intent；Inbox 模板由管理员或种子数据在对应 Host/Tenant 作用域发布。

- [ ] **Step 1: Write the failing tests**

构造四种具体事件，断言映射模板/场景、收件人、有界参数和稳定 MessageId 幂等键；模块注册测试断言 Worker 显式注册四个闭合 Handler。

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~WorkflowNotificationRequestFactoryTests|FullyQualifiedName~NotificationsModuleRegistrationTests"`

Expected: FAIL，原因是 Handler 与显式作用域 Intent 入口尚不存在。

- [ ] **Step 3: Write minimal implementation**

为 `NotificationInboxScope` 增加从可信 Envelope `TenantId` 构造的工厂；为 `NotificationIntentService` 增加 internal 显式 scope 入口并让现有 HTTP 入口复用。四个 Handler 使用 `MessageIdDeduplication`，将 MessageId 映射到 Intent 业务幂等键，调用投影服务；失败 Result 抛出异常交给 Outbox 重试/死信，不吞错。

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~WorkflowNotificationRequestFactoryTests|FullyQualifiedName~NotificationsModuleRegistrationTests"`

Expected: PASS。

### Task 4: 架构、双库影响集与交付记录

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/WorkflowModuleArchitectureTests.cs`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Create: `docs/verification/2026-09-05-workflow-notifications-event-projection.md`

**Interfaces:**
- Consumes: Tasks 1-3 的代码与 fresh 验证输出。
- Produces: 可审计的 Build-verified 证据和仍开放的模板配置、页面人工验收、Worker 恢复、容量边界。

- [ ] **Step 1: Run fast local verification**

Run: `pnpm test:naming`

Run: `dotnet build Full.NET.slnx -c Release`

Run: `pnpm test:inner -- --snapshot workflow-notification-projection-20260905 --plan`

Run: `pnpm test:inner -- --snapshot workflow-notification-projection-20260905`

Expected: 选择器命中的非容器测试通过；页面 E2E 不属于本切片退出条件。

- [ ] **Step 2: Review heavy affected verification**

Run: `pnpm test:integration:affected:plan -- --snapshot workflow-notification-projection-20260905 --phase slice`

Expected: 输出 SQL Server/MySQL、Outbox/Worker 与 Native AOT 的 GitHub Actions 影响集；本地不运行完整重型集合。

- [ ] **Step 3: Record evidence and status**

验证记录必须写明实际通过命令、未运行的重型门禁、模板前置配置和 `Build-verified` 状态；路线图移除“Notifications 异步提醒投影尚未关闭”，保留 Worker 恢复、逐页人工验收和生产容量。

- [ ] **Step 4: Final checks**

Run: `git diff --check`

Run: `git status --short --branch`

Expected: 无空白错误；仅包含本计划影响集。
