# 执行清单 59 — 钉钉审批实例镜像同步与同步记录页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **59**（固定首种 **processCode** 场景的单个审批实例镜像：登记、Worker 出站/轮询、验签回调、补偿重试与 **同步记录页**；**Full.NET Workflow 保持流程权威**，钉钉结果 **不**自动审批/拒绝本地待办）。

## 选定场景

| 项 | 值 |
|----|-----|
| 数据表 | `fn_notifications_dingtalk_approval_sync` |
| 状态机 | `pending_outbound` / `outbound_failed` / `running` / `completed` / `terminated` |
| 幂等 | 同一 `WorkflowInstanceId` 重复登记返回既有镜像 |
| 出站 | `DingTalkApprovalSyncHostedProcessor` + `IDingTalkTransport.CreateProcessInstanceAsync` / 轮询快照 |
| 回调 | `POST …/approval-sync/callback`（`AllowAnonymous` + `X-DingTalk-Approval-Sync-Signature`） |
| 开关 | `Notifications:Providers:DingTalk:Enabled` **且** `Notifications:Providers:DingTalk:Workflow:Enabled` |
| 配置 | `ProcessCode`、`AgentId`、`AppKey`、`AppSecretReference`、`CallbackSecretReference` |
| 未做 | 多模板场景目录、钉钉驱动 Workflow 决策、真实钉钉 OA 全链路验收 |

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `/api/v1/notifications/dingtalk/approval-sync`（list/get/create/retry）+ `/callback` |
| 权限 | `notifications.dingtalk_approval_sync.read` / `.create` / `.retry` |
| Vue | `DingTalkApprovalSyncView`（`data-testid`：列表/登记/重试） |
| 契约 | `notifications-dingtalk-approval-sync-v1.json` |
| 单元 | `DingTalkApprovalSyncCallbackVerifierTests`（验签 + 状态映射） |
| 架构 | `EndpointAuthorizationTests.DingTalk_routes_match_provider_and_workflow_switches` |

## 清单 59 验收结论

- **已有**：镜像只读诊断字段（`ExternalStatusKey` 等）；失败可 `retry`；伪造回调 **fail-closed**（`notifications.receipt_invalid`）。
- **本槽**：`phase-c-59-dingtalk-approval-sync.spec.mjs`（授权列表分页、匿名伪造回调、UI 权威提示与列表冒烟）。
- **未验**：真实 processCode 下创建实例与钉钉回调（需厂商凭据与外网）。

## 停止边界

- 避免双系统同时成为流程权威：无「按钉钉结果写 Workflow」路径。
- 58 互动卡片与 59 镜像同步独立；组织/通讯录不在本槽建设。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~DingTalkApprovalSync` | **2/2**（`d40de4e6`） |
| `node --test` `notifications-dingtalk-approval-sync-contract.test.mjs` | **1/1** |
| real-stack | `phase-c-59-dingtalk-approval-sync.spec.mjs`（需 Host + 双开关） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
