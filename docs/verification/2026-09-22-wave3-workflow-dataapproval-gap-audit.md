# Wave 3 — Workflow / DataApproval 差距核对（清单 01–12）

**日期**：2026-09-22  
**基线**：`9ca8f003`；CI [36077487558](https://github.com/yan041108/Full.NET/actions/runs/36077487558) 全绿
**口径**：对照 [2026-09-05 执行清单 §7.A](2026-09-05-adminnet-module-gap-and-execution-checklist.md)；`已实现` = 源码存在；`证据` = 双库 Integration 或 admin-real-stack E2E。

| ID | 实现状态 | 源码锚点 | 测试证据 | 仍缺动作 |
|----|----------|----------|----------|----------|
| 01 | 已实现 | `DataApprovalScenarioService`、`DataApprovalScenariosView.vue` | OpenAPI + `DataApprovalApiAssertions` 双库；`data-approval-scenarios.spec.mjs` | — |
| 02 | 已实现 | 流水号强类型提案、`SerialNumberRulesView` 提交审批 | Unit + `serial-number-rules.spec.mjs`（UI 提交审批 full path，2026-09-23）+ `data-approval-requests.spec.mjs` | — |
| 03 | 已实现 | `retry` 端点、`DataApprovalRequestRecoveryHostedProcessor` | 迁移 118–120；`DataApprovalRecoveryRestartAssertions` 在 SQL Server/MySQL 验证请求失败后关闭并重启 API、启动独立 Worker 后完成关联；CI 36077487558 双库通过 | — |
| 04 | 已实现 | `retry-apply`、`DataApprovalRequestApplicationRecoveryBatchProcessor` | Unit + 迁移 121；`data-approval-application-conflict.spec.mjs` 覆盖双审批快照冲突、UI 重试及持久化重试计数 | 等待本轮 real-stack CI |
| 05 | 已实现 | `workflowListInstances`、`/instances/mine`、`WorkflowInstancesView` | `WorkflowRuntimeApiAssertions`；`workflow-instances.spec.mjs`（main CI [36058569059](https://github.com/yan041108/Full.NET/actions/runs/36058569059) real-stack green） | — |
| 06 | 已实现 | `workflowListMyTodoHistory`、`WorkflowTodosView` history | `workflow-todos-history.spec.mjs` 覆盖有数据的已办详情、动作快照与只读控件 | 等待本轮 real-stack CI |
| 07 | 已实现 | `WorkflowBusinessTitleRules`、`workflowBusinessDetail` | Migration124、`WorkflowAssigneePolicyTests`；`workflow-instance-business.spec.mjs`（main CI [36058569059](https://github.com/yan041108/Full.NET/actions/runs/36058569059) real-stack green） | — |
| 08 | 已实现 | `workflowSetDefinitionStatus`、`WorkflowDefinitionsView` | Migration125、Workflow 集成套件 | 受引用删除边界 Integration 可加强 |
| 09 | 已实现 | 表单状态/归档与定义页联动 | 同上 | — |
| 10 | 已实现 | `initiator_ancestor_unit_leader` + `AncestorLevel` | Unit `WorkflowAssigneeResolverTests` | — |
| 11 | 已实现 | `WorkflowFormAttachmentCoordinator`、`WorkflowFormRenderer` | 新增 `workflow-forms.spec.mjs`：待办 UI 上传附件、审批前下载拒绝、审批后实例上下文下载内容校验 | 等待本轮 real-stack CI 双库矩阵 |
| 12 | 已实现 | `subtable` 组件目录、Designer/Renderer | Unit + `workflow-forms.spec.mjs` 子表列发布冒烟 | 设计器 UI 拖放子表（可选） |
| — | 已有双库 E2E | Workflow Recovery Worker（`WorkflowRecoveryBatchProcessor`）；Workflow Outbox → Notifications 投影 | `WorkflowRecoveryWorkerTests`；新增 `Recovery_worker_scans_and_suspends_unrecoverable_instance`，本地隔离 Release 构建后 SQL Server/MySQL 2/2 通过；CI [36085785659](https://github.com/yan041108/Full.NET/actions/runs/36085785659) 的 API MySQL/SQL Server 分片均 159/159 通过；Worker Native AOT Linux SQL Server/MySQL 外部进程投影测试已有通过记录（[验证记录](2026-09-05-workflow-notifications-event-projection.md)） | 等待本轮整体 CI；通知投影已通过 |

## F08a/F08b（Wave 1 关联）

| 项 | 状态 | 证据 |
|----|------|------|
| F08b 清债 | 完成 | `module-local-transaction-debt.json` 空；Unit `TenantInvitationRollbackTests` |
| F08a 对账 API | 完成 | 本切片 `TenantQuotaMetricIdReconciliationAssertions` 双库 |

## 结论

清单 01–12 **产品代码已基本到位**；DataApproval 请求在 API 主机重启后的恢复已有 SQL Server/MySQL 集成证据。应用冲突重试、已办详情、Workflow 附件上传与受控下载均已补入 real-stack E2E；当前提交 CI [36091033293](https://github.com/yan041108/Full.NET/actions/runs/36091033293) 尚未结束，相关覆盖暂不记为通过。Recovery Worker 专属触发/恢复 E2E 已在本地及 CI SQL Server/MySQL 分片通过。通知投影、Workflow 实例列表与业务单据跳转已有 fresh CI 证据；不应对 05–09 重复立项。parity `Verified` 仍受 [B2 运行手册](2026-09-22-b2-verified-gates-runbook.md) 约束。

## Phase A 程序收口（2026-09-23）

全量排期 Phase A：对 01–12 执行 **只读核对**；07 已 B1b 关闭。其余编号维持上表「已实现」；证据缺口并入 [程序跟踪 Gate0 G0-2](2026-09-23-execution-checklist-program-tracker.md)，不在此重复立项。
