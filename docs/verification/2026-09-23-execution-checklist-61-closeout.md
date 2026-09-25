# 执行清单 61 — 微信小程序 OpenId 绑定与订阅消息首切片 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **61**（`im.wechat_miniprogram` **订阅消息** Adapter；**js_code** 交换 OpenId 绑定、订阅授权登记、Host **绑定记录页**；Profile 确认 **AppId**；**无**公众号客服/素材/二维码）。

## 选定能力

| 项 | 值 |
|----|-----|
| ProviderTypeKey | `im.wechat_miniprogram` |
| 渠道 | `wechat_miniprogram` |
| 载荷 | 订阅消息（`defaultTemplateId` 或 Intent `subject` 作 templateId + JSON `data`） |
| 身份 | `ExchangeWeChatMiniProgramBindingRequest`（`ProviderProfileVersionId` + `jsCode`） |
| 订阅 | `POST …/bindings/{appId}/subscriptions` 登记 accepted/rejected |
| 回执 | `ReceiptModeKey=none` |
| 开关 | `Notifications:Providers:WeChatMiniProgram:Enabled` |
| 未做 | 公众号 OAuth/客服/素材/二维码；真实 wx.login / subscribeMessage 全链路 |

## 交付锚点

| 层 | 位置 |
|----|------|
| 发送 Adapter | `WeChatMiniProgramNotificationProviderAdapter` → `SendSubscribeMessageAsync` |
| 绑定 API | `/api/v1/notifications/wechat-miniprogram/bindings`（list/mine/exchange/subscriptions） |
| 权限 | `notifications.wechat_miniprogram_bindings.read` / `.bind` / `.record_subscription` |
| Vue | `WeChatMiniProgramBindingsView`（OpenId **掩码**、订阅列表） |
| 控制面 | `NotificationProviderProfilesView`（`appId` + `defaultTemplateId`） |
| 契约 | `notifications-wechat-miniprogram-bindings-v1.json` |
| 单元 | `WeChatMiniProgramNotificationProviderAdapterTests`、`WeChatMiniProgramAccessTokenCacheTests` 等 |

## 清单 61 验收结论

- **已有**：绑定响应仅 `OpenIdMask`；exchange 依赖已发布 Profile 版本；无效 js_code **4xx**（非 5xx）。
- **本槽**：`phase-c-61-wechat-miniprogram-bindings.spec.mjs`（Provider 目录、绑定分页、伪造 exchange、UI 绑定页 + 渠道冒烟）。
- **未验**：真实 AppId/AppSecret 下 code2session 与订阅消息下发（需微信开放平台与小程序端）。

## 停止边界

- 58–60 钉钉/企微通道不替代本槽；公众号能力 **另编号**。
- 用户收件端点由绑定流程创建，**非**通知偏好页手填 OpenId（与企微/钉钉偏好不同）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~WeChatMiniProgram` | **7/7**（`d40de4e6`） |
| `node --test` `notifications-wechat-miniprogram-bindings-contract.test.mjs` | **1/1** |
| real-stack | `phase-c-61-wechat-miniprogram-bindings.spec.mjs`（需 Host + 可选开关） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
