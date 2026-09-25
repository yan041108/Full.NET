# 执行清单 58 — 钉钉互动卡片 Adapter 与 Profile/投递配置 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **58**（首个钉钉 **互动卡片** `createAndDeliver`、access_token 续期、验签失败回执；通知平台 **Profile / Binding / Worker / 投递运维** 与 **通知偏好** 端点；**不**建设钉钉组织主数据、**不**进入审批镜像同步——见 **59**）。

## 选定能力

| 项 | 值 |
|----|-----|
| ProviderTypeKey | `im.dingtalk` |
| 渠道 | `dingtalk` |
| 载荷 | 固定互动卡片模板（`cardTemplateId` + `title`/`content` 或闭合 JSON 参数） |
| 回执 | `ReceiptModeKey=signed`（`DingTalkReceiptVerifier` + `X-DingTalk-Receipt-Signature`） |
| Token | `DingTalkAccessTokenCache`（过期前 60s 续期，进程内封顶 1024 条） |
| 开关 | `Notifications:Providers:DingTalk:Enabled` |
| 未做 | 审批实例同步 UI/API（59）；真实钉钉 OpenAPI 账号验收 |

## 交付锚点

| 层 | 位置 |
|----|------|
| 发送 Adapter | `DingTalkNotificationProviderAdapter` → `HttpDingTalkTransport.CreateAndDeliverAsync` |
| 配置 Schema | `appKey`、`agentId`、`cardTemplateId`、`robotCode`、可选 `callbackRouteKey` + `appSecret` 引用 |
| 收件端点 | `RecipientEndpointKindKey=dingtalk`；`NotificationPreferencesView`（钉钉 userId） |
| 回执 Webhook | `POST /api/v1/notifications/provider-receipts/im.dingtalk` |
| 控制面 Vue | `NotificationProviderProfilesView`；`NotificationDeliveriesView`；`NotificationPreferencesView` |
| 单元 | `DingTalkNotificationProviderAdapterTests`、`DingTalkAccessTokenCacheTests`、`DingTalkReceiptVerifierTests` |
| 注册 | `NotificationsModuleRegistrationTests`（仅显式启用时注册 Adapter/Verifier/HttpClient） |

## 清单 58 验收结论

- **已有**：闭合 Profile；Worker 共用 Intent/Binding 投递链；无效 userId/正文 **永久失败**；伪造回执 **fail-closed**。
- **本槽**：`phase-c-58-dingtalk-card-adapter.spec.mjs`（目录 `signed` + 卡片字段；UI 渠道/投递/偏好冒烟）。
- **未验**：真实 AppKey/Secret 下 token 与卡片送达（需厂商环境与 `HttpDingTalkTransport` 外网）。

## 停止边界

- **59**：`DingTalkApprovalSyncView` / `approval-sync` API 为镜像登记，不升级本槽「消息卡片」范围。
- 不同时建设钉钉通讯录/部门权威；组织数据仍走 Full.NET Identity/Organization。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~DingTalk` | **13/13**（`d40de4e6`） |
| real-stack | `phase-c-58-dingtalk-card-adapter.spec.mjs`（需本地 Host + 可选 DingTalk 开关） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
