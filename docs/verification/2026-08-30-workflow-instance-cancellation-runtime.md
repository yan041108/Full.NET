# Workflow 实例取消运行时验证

## 范围

- 为活动 Workflow 实例增加 `POST /api/v1/workflow/instances/{instanceId}/cancel`。
- 使用 `workflow.instances.cancel` 独立动作权限，并继续校验发起人或参与人资源关系。
- 请求携带 `ExpectedRevision`、`IdempotencyKey` 与可选取消原因；取消原因最大 512 字符。
- 在同一本地事务内把实例、活动步骤和活动待办更新为 `cancelled`，并追加动作记录、执行日志和领域审计。
- Vue 管理端只为拥有独立取消权限且实例仍为 `active` 的用户创建确认入口。

## 一致性与失败语义

- 实例、步骤和待办分别使用当前修订号做乐观并发保护；任一更新未命中时整笔事务回滚并返回 409。
- 同一实例、同一幂等键、同一操作者和同一请求摘要可稳定重放；复用幂等键但改变请求返回 409。
- 已进入终态的实例拒绝新的取消请求；取消不会发布 Integration Event，因为当前没有已批准的外部消费者。
- SQL Server 与 MySQL 复用既有 `cancelled` 状态和 `CancellationReason` 字段，不新增数据库迁移。

## 验证证据

- `WorkflowStateMachineTests`：取消状态转换、修订递增、重放、过期修订和终态拒绝。
- `WorkflowApiSqlServerTests` / `WorkflowApiMySqlTests`：精确权限、资源边界、事务取消、重放、冲突和执行轨迹。
- OpenAPI 运行时快照：SQL Server/MySQL 文档一致，并生成 `CancelWorkflowInstanceRequest` 与 `workflowCancelInstance`。
- Vue 单元测试：无权限不创建入口；有权限时确认后携带当前 revision 与幂等键调用生成客户端。

最终命令和结果以本次提交交付说明中的新鲜验证输出为准。
