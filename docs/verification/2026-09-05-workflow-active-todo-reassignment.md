# Workflow 活动待办改派验证记录

## 结论

- 状态：`Build-verified`，不提升为 `Verified`。
- 已增加 `POST /api/v1/workflow/instances/{instanceId}/reassign`，使用既有高权限码 `workflow.instances.recover`。
- 改派只接受当前可信 Host/Tenant 作用域内的活动用户。Identity 批量目录读取发生在 Workflow 本地事务之前，事务内只访问 Workflow 自有表与事务 Outbox。
- 成功路径原子推进待办和实例修订号，追加动作回执、`todo.reassign` 执行轨迹、`instance.reassign` B0 领域审计，并向新办理人发布 `TodoAssigned` 事实。

## 并发与幂等

- 请求必须携带实例 `ExpectedRevision` 与非空幂等键；待办更新同时校验原办理人、活动状态和待办修订号。
- 任一乐观锁更新未命中时，`ExecuteResultAsync` 回滚本事务中的全部状态、审计与 Outbox 写入。
- 同一操作人、幂等键和规范请求摘要可重放；复用幂等键但目标用户、修订号或原因不同会返回冲突。
- 同办理人改派、终态实例、无活动待办、无效/停用/跨作用域用户均失败关闭，不产生持久化写入。

## TDD 与本地证据

- 首轮 RED：聚焦测试因 `WorkflowInstanceRecoveryService` 和 `ReassignWorkflowInstanceRequest` 不存在而编译失败。
- 重放 RED：相同幂等语义首次只返回成功但遗漏活动待办标识；新增断言稳定复现后，改为读取当前活动待办并返回。
- 后端聚焦测试：`WorkflowInstanceRecoveryServiceTests` 6/6 通过，覆盖原子写入/通知、事务外用户校验、幂等重放、停用目标、同办理人、旧修订和端点精确权限。
- 前端 API 适配 RED：新增用例最初以 `reassignWorkflowInstance is not a function` 失败；增加生成客户端薄适配后 3/3 通过。
- Integration 项目 Release 构建：成功，0 警告、0 错误。
- 完整解决方案 Release 构建：成功，0 警告、0 错误。
- 完整 Unit：1847 项，1846 通过、1 项 Linux 专属 FIFO 用例在 Windows 按设计跳过、0 失败。
- Vue 管理端类型检查：通过。
- 双 Provider 运行时 OpenAPI 导出：SQL Server/MySQL 各 1/1 通过，快照已更新；生成客户端零漂移。
- OpenAPI 治理：首次因新增 Operation 后权威数量仍为 288 而 123/124；将唯一计数断言更新为 289 后 124/124 通过。
- 命名门禁：30/30 通过；Host.Api AOT 分析器构建 0 警告、0 错误；API Native AOT 架构选择集 73/73 通过。
- Integration 分片治理：643 项无遗漏或重复；仓库治理 52/52 通过；`git diff --check` 无错误。
- 最终受影响计划选择 `integration-matrix, Notifications, smoke, Workflow`，预计约 8 分钟；环境重型执行留给 GitHub Actions。
- 全量 Architecture 首轮发现 Notifications 的 Workflow 事件契约尚未区分可选生产者与运行时硬依赖；新增 `OptionalContractDependencies` 后，Notifications 在 Platform/Content 裁剪预设中仍可脱离 Workflow 运行，相关聚焦回归 4/4、最终全量 Architecture 200/200 通过。

## 延后验证

- SQL Server/MySQL 真实数据库 API 改派、事务回滚、审计行和 Outbox 数据断言已加入现有 Workflow Integration 场景，按当前策略不在本地运行环境重型集合，待取得提交/推送授权后由 GitHub Actions 执行。
- 管理页面的目标用户选择、交互提示和人工逐页验收继续延后；本轮只交付后端能力、生成契约和 Vue API 薄适配。
- 暂停实例强制恢复、步骤回退、超时扫描与失败作业恢复不属于本切片。

## 规则与 Skill 演进

- 规则演进未触发：OpenAPI 计数漂移是新增公开 Operation 的单次确定性维护项，不构成重复失败或新风险类别。
- Skill 演进未触发：`fullnet-module-delivery` 已覆盖跨模块最小目录、事务、Outbox、公开契约和验证边界。
