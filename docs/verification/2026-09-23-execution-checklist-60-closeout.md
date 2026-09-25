# 执行清单 60 — 企业微信文本通知 Adapter 与 Profile/投递页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **60**（首个 **企业微信应用文本消息** Adapter、`corpId`/`agentId` + `corpSecret` 引用、access_token 续期；通知平台 **Profile / 投递运维 / 通知偏好** wecom userId；**无**部门/用户/标签同步、**无**群聊与其余媒体类型）。

## 选定能力

| 项 | 值 |
|----|-----|
| ProviderTypeKey | `im.wecom` |
| 渠道 | `wecom` |
| 载荷 | 应用消息 **text**（`subject` + 换行 + `body`，总长 ≤4096） |
| 回执 | `ReceiptModeKey=none`（无厂商验签 Webhook） |
| Token | `WeComAccessTokenCache`（与钉钉相同续期/封顶策略） |
| 开关 | `Notifications:Providers:WeCom:Enabled` |
| 未做 | 通讯录同步、标签/群聊、图文/卡片/文件等消息类型 |

## 交付锚点

| 层 | 位置 |
|----|------|
| 发送 Adapter | `WeComNotificationProviderAdapter` → `HttpWeComTransport.SendTextAsync` |
| 配置 Schema | `corpId`、`agentId` + `corpSecret` 引用 |
| 收件端点 | `RecipientEndpointKindKey=wecom`；`NotificationPreferencesView` |
| 控制面 Vue | `NotificationProviderProfilesView`；`NotificationDeliveriesView` |
| 单元 | `WeComNotificationProviderAdapterTests`、`WeComAccessTokenCacheTests` |
| 注册 | `NotificationsModuleRegistrationTests.WeCom_provider_is_registered_only_when_explicitly_enabled` |

## 清单 60 验收结论

- **已有**：闭合 Profile；无效 userId/正文 **永久失败**；匿名回执端点 **receipt_not_supported**（descriptor 为 none）。
- **本槽**：`phase-c-60-wecom-text-adapter.spec.mjs`（目录 `none` + corpId/agentId；UI 渠道/投递/偏好冒烟）。
- **未验**：真实 corpSecret 下 token 与 `message/send` 送达（需企业微信凭据与外网）。

## 停止边界

- 组织/部门/用户/标签 CRUD 与同步、应用群聊、非文本消息各 **另拆编号**。
- 58–59 钉钉能力不替代本槽；61 为微信生态下一渠道。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~WeCom` | **8/8**（`d40de4e6`） |
| real-stack | `phase-c-60-wecom-text-adapter.spec.mjs`（需本地 Host + 可选 WeCom 开关） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
