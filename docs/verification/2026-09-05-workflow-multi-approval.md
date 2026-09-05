# Workflow 会签与或签 — Build-verified

**状态：** Build-verified  
**分支：** `feat/workflow-multi-approval`（基于 `feat/workflow-countersign` / 加签迁移 114）  
**范围：** 编号 7 — 会签 `all`、或签 `any`、法定票数 `nOfM`；发布校验、待办生成、完成条件；设计器配置、待办与实例详情进度展示。

## 交付摘要

- **迁移 113**：`fn_workflow_approval_slot` 与步骤上 `ApprovalModeKey` / `RequiredApprovalCount` / `ApprovalSlotCount`（SqlServer + MySql）
- **领域**：`WorkflowApprovalPolicy`、`WorkflowApprovalDecision`、`WorkflowApprovalActivationWriter`
- **运行时**：发布时校验活动办理人；节点激活创建一步骤 + 多席位 + 一人一条待办；`approve`/`reject` 按 Slot 票数串行收敛，终态取消剩余待办
- **实例详情**：`GET /api/v1/workflow/instances/{id}` 附带活动多人审批进度字段
- **Vue**：`WorkflowVue3Designer` 配置三种策略；`WorkflowTodosView` 与 `WorkflowInstancesView` 展示票数摘要
- **OpenAPI**：Todo Detail/Runtime 与 Instance Response 扩展多人审批字段；生成客户端已同步

## 本地验证（2026-09-05）

| 项 | 结果 |
| --- | --- |
| `Full.NET.Modules.Workflow` Release 构建 | 通过 |
| 聚焦 Unit（Approval/MultiApproval/Countersign/Return/Compiler/RuntimePlan/Instance Get） | 54/54 通过 |
| Vue Vitest（设计器、适配器、待办、实例） | 待 `pnpm --filter admin test` 内循环确认 |

## 延后（按用户与仓库规则）

- 页面真实栈 E2E、人工逐页验收
- 双库 Workflow API Integration 分片（迁移 113 恢复测试已添加，重型 API 分片交 CI）
- Linux Native AOT publish 门禁

## 与编号 6 合并说明

本分支在 `feat/workflow-countersign` 之上 cherry-pick 并合并 `codex/workflow-multi-approval`，保留加签（114）与多人审批（113）双迁移链；`WorkflowTodoManagementService` 同时注入 `WorkflowApprovalActivationWriter` 与 `WorkflowTodoCountersignService`。
