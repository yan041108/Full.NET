# Workflow 超时、催办与升级策略实施计划

**目标：** 为 `human.approval` 节点增加不可变超时策略，让 Worker 对运行中逾期待办按确定序列发布催办/升级事件，由 Notifications 幂等生成通知，并在定义编辑页和实例详情页呈现该能力。

**基线：** `feat/workflow-recovery-worker@5a55562f`；实施分支 `codex/workflow-timeout-escalation`。

**语义边界：** 超时不自动办理或改派待办。暂停实例时停止扫描但 SLA 使用绝对 UTC 时间，恢复后若已逾期会立即进入下一次扫描；改派保留截止时间、催办次数和固定升级接收人，后续催办发送给新办理人；实例或待办终态不再产生信号；条件更新与 Outbox 同事务提交，重复扫描不会重复发布同一序号；Notifications 继续以 Outbox MessageId 做 Inbox 幂等，事件重放不重复创建通知。

## 1. RED：固化定义策略和运行时截止时间

- 修改 `tests/Full.NET.UnitTests/Workflow/WorkflowDefinitionCompilerTests.cs`，增加合法策略规范化、非法分钟数/收件人/多余字段失败测试。
- 修改 `tests/Full.NET.UnitTests/Workflow/WorkflowInstanceManagementServiceTests.cs`，证明启动时从不可变定义计算 `DueAtUtc`、`NextReminderAtUtc`、`EscalateAtUtc`。
- 运行新增测试并确认因缺少实现而失败。
- 新增 `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowTodoTimeoutPolicy.cs`，仅允许 `dueAfterMinutes`、`reminderIntervalMinutes`、`maxReminderCount`、`escalationAfterMinutes`、`escalationRecipientUserId` 的闭合配置，并把规范值写回发布 IR。
- 修改编译器、启动服务、持久化记录和 SQL，使待办创建时固化截止与下一信号时间。

## 2. RED：Worker 扫描与可靠事件

- 新增 `tests/Full.NET.UnitTests/Workflow/WorkflowTodoTimeoutProcessorTests.cs`，覆盖催办、升级、重复扫描、暂停/终态跳过、改派后催办接收人。
- 扩展 `tests/Full.NET.UnitTests/Workflow/WorkflowNotificationOutboxPublisherTests.cs`，覆盖两个新事件。
- 运行新增测试并确认失败。
- 新增 `WorkflowTodoTimeoutSql`、候选投影、`WorkflowTodoTimeoutProcessor` 和 Hosted Processor；按 SQL Server/MySQL 各自的有界领取语句扫描，逐条在 Workflow 本地事务内条件推进信号序号、写执行日志和 Outbox。
- Worker 按候选 `TenantId` 使用受信 `IActiveTenantContextResolver` 与 `ICurrentTenantContextWriter` 建立作用域，Host 候选使用 Host 上下文；每条处理后清理上下文。
- 扩展 Workflow Contracts 与 Outbox Publisher，新增 `fullnet.workflow.todo.reminded` 和 `fullnet.workflow.todo.escalated` MemoryPack 事件。

## 3. RED：Notifications 消费与重放幂等

- 扩展 Notifications 工厂、Handler、注册和模板测试，证明催办发给当前办理人、升级发给固定接收人，深链仍指向 Todo。
- 运行新增测试并确认失败。
- 实现两个 Handler、请求映射和默认模板；保持 MessageId 去重策略，不跨模块读 Workflow 表。

## 4. 双库迁移与契约

- 新增成对迁移 `111_WorkflowTodoTimeoutPolicy.sql`：为 `fn_workflow_todo` 增加截止、下一催办、升级、计数及最后信号列，并建立 `(StatusKey, NextTimeoutSignalAtUtc, Id)` 有界扫描索引。
- 新增迁移恢复测试，覆盖 SQL Server/MySQL 的部分 DDL 恢复、数据保留和二次执行。
- 更新 Dapper AOT 物化与全局 SQL 清单，保证新增投影和语句进入静态闭包。
- 扩展实例详情响应，返回活动待办的 `dueAtUtc`、`timeoutStatusKey`、`reminderCount`、`escalatedAtUtc`。

## 5. Vue 定义编辑与实例详情

- 先扩展 `workflow-vue3-adapter.test.ts`、`WorkflowVue3Designer.test.ts` 和 `WorkflowInstancesView.test.ts`，证明闭合策略往返、审批节点超时抽屉保存、详情显示正常/逾期/已升级状态。
- 修改适配器允许 `human.approval.timeoutPolicy`，拒绝未知/危险字段。
- 在 `WorkflowVue3Designer.vue` 的审批节点抽屉中配置截止分钟、催办间隔/次数、升级分钟和升级接收人。
- 在 `WorkflowInstancesView.vue` 展示截止时间、催办次数和升级状态；补齐中英文 i18n。
- 重新生成 OpenAPI 客户端契约，禁止手改后留下漂移。

## 6. 验证、文档与提交

- 运行 Workflow/Notifications 定向单元测试、架构测试、Vue 定向 Vitest、admin typecheck、Release 全解决方案构建、SQL/命名治理、`git diff --check`。
- 使用任务快照执行 affected integration plan；页面真实栈 E2E、视觉调整、人工逐页验收和环境重型双库/Native AOT 留到最终页面收敛或 GitHub Actions。
- 新增 `docs/verification/2026-09-05-workflow-timeout-escalation.md`，状态只标 `Build-verified`，记录已执行与延期证据。
- 检查规则演进：若无新类别或重复失败，只记录“不更新规则候选”。
- 在 `codex/workflow-timeout-escalation` 上创建单一独立提交，停止，不合并、不删除分支、不提前执行编号 4。
