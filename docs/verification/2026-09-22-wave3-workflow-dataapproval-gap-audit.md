# Wave 3 — Workflow / DataApproval 差距核对（清单 01–12）

**日期**：2026-09-22  
**基线**：`da77e74e` 及本切片文档/测试增量  
**口径**：对照 [2026-09-05 执行清单 §7.A](2026-09-05-adminnet-module-gap-and-execution-checklist.md)；`已实现` = 源码存在；`证据` = 双库 Integration 或 admin-real-stack E2E。

| ID | 实现状态 | 源码锚点 | 测试证据 | 仍缺动作 |
|----|----------|----------|----------|----------|
| 01 | 已实现 | `DataApprovalScenarioService`、`DataApprovalScenariosView.vue` | OpenAPI + `DataApprovalApiAssertions` 双库；`data-approval-scenarios.spec.mjs` | — |
| 02 | 已实现 | 流水号强类型提案、`SerialNumberRulesView` 提交审批 | Unit + `serial-number-rules.spec.mjs`（UI 提交审批 full path，2026-09-23）+ `data-approval-requests.spec.mjs` | — |
| 03 | 已实现 | `retry` 端点、`DataApprovalRequestRecoveryHostedProcessor` | 迁移 118–120；本切片补 API 列表/读权限断言 | 进程中断恢复 full E2E |
| 04 | 已实现 | `retry-apply`、`DataApprovalRequestApplicationRecoveryBatchProcessor` | Unit + 迁移 121 | 应用冲突 real-stack |
| 05 | 已实现 | `workflowListInstances`、`/instances/mine`、`WorkflowInstancesView` | `WorkflowRuntimeApiAssertions`；`workflow-instances.spec.mjs`（main CI [36058569059](https://github.com/yan041108/Full.NET/actions/runs/36058569059) real-stack green） | — |
| 06 | 已实现 | `workflowListMyTodoHistory`、`WorkflowTodosView` history | `workflow-todos-history.spec.mjs`（待办/已办页签） | 有数据时详情断言 |
| 07 | 已实现 | `WorkflowBusinessTitleRules`、`workflowBusinessDetail` | Migration124、`WorkflowAssigneePolicyTests`；`workflow-instance-business.spec.mjs`（main CI [36058569059](https://github.com/yan041108/Full.NET/actions/runs/36058569059) real-stack green） | — |
| 08 | 已实现 | `workflowSetDefinitionStatus`、`WorkflowDefinitionsView` | Migration125、Workflow 集成套件 | 受引用删除边界 Integration 可加强 |
| 09 | 已实现 | 表单状态/归档与定义页联动 | 同上 | — |
| 10 | 已实现 | `initiator_ancestor_unit_leader` + `AncestorLevel` | Unit `WorkflowAssigneeResolverTests` | — |
| 11 | 已实现 | `WorkflowFormAttachmentCoordinator`、`WorkflowFormRenderer` | Workflow 集成/Native AOT 子集 | 本切片扩展 `workflow-forms` 附件 real-stack |
| 12 | 已实现 | `subtable` 组件目录、Designer/Renderer | Unit + `workflow-forms.spec.mjs` 子表列发布冒烟 | 设计器 UI 拖放子表（可选） |
| — | 部分 | Workflow Recovery Worker（`WorkflowRecoveryBatchProcessor`）；Workflow Outbox → Notifications 投影 | `WorkflowRecoveryWorkerTests`；Worker Native AOT Linux SQL Server/MySQL 外部进程投影测试通过（[验证记录](2026-09-05-workflow-notifications-event-projection.md)） | Recovery 专属触发/恢复 full E2E 仍缺；通知投影已通过 |

## F08a/F08b（Wave 1 关联）

| 项 | 状态 | 证据 |
|----|------|------|
| F08b 清债 | 完成 | `module-local-transaction-debt.json` 空；Unit `TenantInvitationRollbackTests` |
| F08a 对账 API | 完成 | 本切片 `TenantQuotaMetricIdReconciliationAssertions` 双库 |

## 结论

清单 01–12 **产品代码已基本到位**；Wave 3 剩余工作是 DataApproval 进程中断恢复与应用冲突 real-stack、待办历史有数据时的详情断言、Workflow 表单附件 real-stack，以及 Recovery 专属 full E2E。通知投影、Workflow 实例列表与业务单据跳转已有 fresh CI 证据；不应对 05–09 重复立项。parity `Verified` 仍受 [B2 运行手册](2026-09-22-b2-verified-gates-runbook.md) 约束。

## Phase A 程序收口（2026-09-23）

全量排期 Phase A：对 01–12 执行 **只读核对**；07 已 B1b 关闭。其余编号维持上表「已实现」；证据缺口并入 [程序跟踪 Gate0 G0-2](2026-09-23-execution-checklist-program-tracker.md)，不在此重复立项。
