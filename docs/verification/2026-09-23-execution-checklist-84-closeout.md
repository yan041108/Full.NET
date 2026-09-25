# 执行清单 84 — Workflow 页面验收 closeout（2026-09-23）

**范围**：清单 §7.D 编号 **84**（工作流表单/定义/待办/已办/实例/恢复页面 real-stack 与证据汇编；分支审批、业务跳转、历史快照的深度路径以既有专项 spec 为准）。

## 验收策略

- **深度路径**：`workflow-approval.spec.mjs` / `workflow-approval-tenant.spec.mjs`（发起、同意/驳回、危险 PATCH 422）；`workflow-forms.spec.mjs`（VForm3 草稿/发布/子表冒烟、定义页）；`workflow-todos-history.spec.mjs`（待办/已办页签）；`workflow-instances.spec.mjs`（全部/我发起）；`workflow-instance-business.spec.mjs`（DataApproval 业务类型跳转 + 审批详情）。
- **本槽补强**：`phase-d-84-workflow-pages-nav.spec.mjs` — 工作流分组下 6 个核心页面标题可达（含 **恢复任务**）。
- **单元/UI**：`WorkflowAssignee*`、`WorkflowRecovery*`、`WorkflowBusinessTitle*` 等；`WorkflowInstancesView.test.ts`（暂停/强制恢复权限）、`WorkflowRecoveryTasksView.test.ts`。
- **集成**：`WorkflowRuntimeApiAssertions` 等（双库常于 CI `integration-shard`；本机 Docker 可跳过）。
- **未在本槽声称**：全分支组合人工审阅、Recovery Worker 端到端投影 fresh（见 wave3 gap-audit）、Verified（归 **87** / Gate C）。

## 页面 → 证据索引

见 [D 区审计 §84](2026-09-23-execution-checklist-d-zone-audit.md)。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`WorkflowAssignee` + `WorkflowRecovery` + `WorkflowBusinessTitle` | **30/30** |
| `pnpm exec vitest run` …`WorkflowInstancesView` + `WorkflowRecoveryTasksView` | **11/11** |
| real-stack | `phase-d-84-workflow-pages-nav.spec.mjs` + `workflow-*.spec.mjs` 子集 |

**状态**：Build-verified；升 Verified 受 **87**、Gate C 与 B2 手册约束。
