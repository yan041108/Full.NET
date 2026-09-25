# 执行清单 D 区（83–87）页面验收审计（2026-09-23）

**目的**：Phase D 在选定功能批次 **closeout 合并后** 做逐页 real-stack 与人工补项登记；**不等同** parity `Verified`（仍受 [B2 运行手册](2026-09-22-b2-verified-gates-runbook.md) 与 **87** CI fresh 约束）。

## 83 — Identity / 角色 / 菜单 / 租户 / 组织 / 设置

| 页面（route 语义） | 深度 E2E 锚点 | 83 槽判定 |
|--------------------|---------------|-----------|
| 用户管理 `users` | `host-users.spec.mjs`、`host-users-import-bulk.spec.mjs` | closeout（403/导航裁剪） |
| 角色管理 `roles` | `host-roles.spec.mjs`（含复制，B-19） | closeout |
| 菜单管理 `menus` | `host-menus.spec.mjs` | closeout |
| 租户管理 `tenants` | `host-tenants.spec.mjs` | closeout |
| 租户套餐 `tenant-packages` | `host-tenant-packages.spec.mjs` | closeout |
| 租户品牌 `tenant-branding` | `host-tenant-branding.spec.mjs`（B-22） | closeout |
| 机构/职位/职级/用户机构/用户职位 | `host-org-*.spec.mjs`（租户上下文） | closeout |
| 数据字典 `dict-types` | `host-dict-types.spec.mjs` | closeout |
| 租户数据字典 `tenant-dict-types` | `host-tenant-dict-types.spec.mjs`（租户上下文） | closeout |
| 系统配置 `config-entries` | `host-config-entries.spec.mjs` | closeout |
| 限时诊断 `diagnostic-policy` | `host-diagnostic-policy.spec.mjs` | closeout |
| 枚举常量 `enum-catalogs` | `host-enum-catalogs.spec.mjs`（B-25） | closeout |
| 导航冒烟（83 汇总） | `phase-d-83-identity-org-settings-nav.spec.mjs` | closeout |

**未在本槽声称通过**：axe/WCAG 全量、键盘-only 操作审阅、双库 Integration 矩阵 fresh（归 **87**）；Layui 冻结线仅安全修复。

## 84 — Workflow / 表单 / 待办 / 实例 / 恢复

| 页面 | 深度 E2E 锚点 | 84 槽判定 |
|------|---------------|-----------|
| 我的待办 `workflow-todos` | `workflow-todos-history.spec.mjs`、`workflow-approval*.spec.mjs` | closeout |
| 我的抄送 `workflow-cc` | 导航冒烟（84）；知会深度待业务数据时补强 | closeout（可达性） |
| 工作流定义 `workflow-definitions` | `workflow-approval-fixtures`、`workflow-forms.spec.mjs` | closeout |
| 工作流表单 `workflow-forms` | `workflow-forms.spec.mjs`（CSP/VForm3/子表） | closeout |
| 工作流实例 `workflow-instances` | `workflow-instances.spec.mjs`、`workflow-instance-business.spec.mjs` | closeout |
| 恢复任务 `workflow-recovery-tasks` | `WorkflowRecoveryTasksView.test.ts`；`phase-d-84-workflow-pages-nav.spec.mjs` | closeout（列表/权限 UI；Worker E2E 仍 gap） |
| 导航汇总 | `phase-d-84-workflow-pages-nav.spec.mjs` | closeout |

## 85 — Notifications / DataApproval 业务链

| 页面/链路 | 深度 E2E 锚点 | 85 槽判定 |
|-----------|---------------|-----------|
| 通知控制面五页 + 偏好 | `notification-platform.spec.mjs` | closeout |
| 公告管理 / 我收到的公告 | `host-announcements.spec.mjs`、`host-announcements-receipts.spec.mjs` | closeout |
| 消息中心 | `inbox-messages.spec.mjs` | closeout |
| 数据审批请求/场景 | `data-approval-requests.spec.mjs`、`data-approval-scenarios.spec.mjs` | closeout |
| 提交→待办→业务跳转 | `serial-number-rules`、`workflow-instance-business`、`workflow-approval*` | closeout（链片段） |
| Intent/回执边界 | B-43–45 capability specs | closeout（API；无厂商） |
| 导航汇总 | `phase-d-85-notifications-data-approval-nav.spec.mjs` | closeout |
| retry/cancel/retry-apply UI | Unit/Integration 为主 | **未验** full real-stack |

## 86 — Files / Document / Jobs / CodeGeneration / 运维

| 模块 | 深度 E2E | 86 槽判定 |
|------|----------|-----------|
| Files | `host-files.spec.mjs` | closeout |
| Document 全子页 + 回滚 | `host-documents`、`document-*`、`host-document-version-rollback` | closeout |
| Jobs/计划/执行/取消/批量 | `host-jobs*`、`host-job-executions-cancel`、`host-job-schedules-batch` | closeout |
| CodeGeneration | `host-code-generation-*` | closeout |
| 观测三页 | `host-observability-*` | closeout |
| 导航汇总 | `phase-d-86-ops-modules-nav.spec.mjs` | closeout |

## 87 — 程序收口

| 项 | 87 槽判定 |
|----|-----------|
| Phase A/B/D closeout 汇编 | **完成**（本工作区文档；合 main 待推送） |
| `pnpm test:governance` / `pnpm test:openapi` | **55/55**、**175/175**（`d40de4e6` 工作区） |
| integration-matrix / real-stack-e2e fresh | **未在本机关闭**；分项见 [87 closeout](2026-09-23-execution-checklist-87-closeout.md) |
| WF+DA parity Verified | **未宣称**；[Gate C 登记](2026-09-23-execution-checklist-gate-c-wf-da-status.md) |
| Phase C 48+ | **未启动** |
