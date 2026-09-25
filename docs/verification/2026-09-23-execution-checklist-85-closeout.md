# 执行清单 85 — Notifications / DataApproval 业务链页面 closeout（2026-09-23）

**范围**：清单 §7.D 编号 **85**（提交→待办→审批→业务应用→通知的 **页面与 real-stack 证据汇编**；拒绝/撤回/失败恢复与外部厂商账号单列）。

## 业务链证据（流水号 DataApproval 主线）

| 阶段 | 页面/能力 | real-stack 锚点 |
|------|-----------|-----------------|
| 业务提交审批 | 流水号规则 | `serial-number-rules.spec.mjs`、`data-approval-serial-rule-disable.spec.mjs` |
| 工作流待办/审批 | 我的待办、定义 | `workflow-approval*.spec.mjs`、`workflow-instance-business.spec.mjs` |
| 审批请求运维 | 数据审批请求 | `data-approval-requests.spec.mjs` |
| 场景策略 | 审批场景配置 | `data-approval-scenarios.spec.mjs` |
| 站内/平台通知 | 消息中心、控制面 | `inbox-messages.spec.mjs`、`notification-platform.spec.mjs` |
| Host 公告收件 | 我收到的公告 | `host-announcements-receipts.spec.mjs`（B-42） |
| Intent/渠道边界 | API | `notification-intent-attachments`、`notification-smtp-receipt-capability`、`notification-aliyun-sms-capability`（无真实 SMTP/dysmsapi） |

**未在本槽端到端跑通**：完整「审批通过→Applier 应用→Worker 投递→外部回执」需 green Worker + 厂商账号；**retry/retry-apply/cancel** 以 Integration/Unit 为主，页面级恢复待数据夹具时补强。

## 本槽交付

| 产物 | 说明 |
|------|------|
| `phase-d-85-notifications-data-approval-nav.spec.mjs` | 通知 6 页 + 消息中心 + 数据审批 2 页导航冒烟 |
| [D 区审计 §85](2026-09-23-execution-checklist-d-zone-audit.md) | 页面→spec 映射 |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`DataApprovalRequest` + `NotificationDeliveryStateMachine` | **9/9** |
| `pnpm exec vitest run` …`DataApprovalRequestsView` + `NotificationDeliveriesView` | **11/11** |
| real-stack | `phase-d-85-*.spec.mjs` + 上表专项 spec |

**状态**：Build-verified；外部账号与 Verified 归 **87** / Gate C。
