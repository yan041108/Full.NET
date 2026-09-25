# 执行清单 45 — 阿里云短信 Provider 闭环 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **45**（选定一家短信 Provider 的模板发送、收件端点验证码、验签回执与投递运维展示；复用通知 Worker、限流与回执状态机；**不**冒充真实 dysmsapi 账号验收通过）。

## 选定厂商

| 项 | 值 |
|----|-----|
| ProviderTypeKey | `sms.aliyun` |
| 渠道 | `sms` |
| 回执 | `ReceiptModeKey=signed`（`AliyunSmsReceiptVerifier` + `X-Aliyun-Sms-Receipt-Signature`） |
| 开关 | `Notifications:Providers:AliyunSms:Enabled` |
| 未选 | 腾讯云/Custom 短信 Adapter 未进入生产目录 |

## 交付锚点

| 层 | 位置 |
|----|------|
| 发送 Adapter | `AliyunSmsNotificationProviderAdapter`（dysmsapi `SendSms`、模板参数 JSON） |
| 验证码 | `AliyunSmsRecipientEndpointVerificationSender` + `RecipientEndpointVerificationService` |
| 端点 API | `POST …/my-recipient-endpoints/{id}/verification/send|verify` |
| 回执 Webhook | `POST /api/v1/notifications/provider-receipts/sms.aliyun` |
| Worker / 状态机 | `NotificationDeliveryBatchProcessor`、`NotificationDeliveryStateMachine`（`sent` 待回执） |
| Vue | `NotificationProviderProfilesView`（验签回执标签）；`NotificationPreferencesView`（`sms.aliyun` 端点验证）；`NotificationDeliveriesView`（`sms` + `sent` 提示以回执为准） |
| 集成 | `NotificationDeliveryWorkerAssertions`（验签回执推进）；`NotificationsApiSqlServerTests` / `NotificationsApiMySqlTests` 全栈切片 |
| 外部 | `AliyunSmsNotificationProviderExternalTests`（`ExternalAliyunSms`，凭据未验不记通过） |

## NT06 核验结论

- **已有**：闭合 Profile Schema（`regionId`、`signName`、`accessKeyId`、`verificationTemplateCode` + `accessKeySecret` 引用）；Intent/Binding/Worker 与邮件共用平台；手机号校验与端点脱敏；伪造回执 **fail-closed**。
- **本槽**：新增 real-stack `notification-aliyun-sms-capability.spec.mjs`（目录 `signed`、伪造 Webhook；UI 在 Aliyun 启用时验「验签回执」与模板 `sms` 渠道）。
- **未验**：真实阿里云控制台模板/签名/余额下的 SendSms 与状态报告 URL（需厂商账号与环境变量，见 `AliyunSmsNotificationProviderExternalTests`）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`AliyunSms` + `RecipientEndpointVerification` | **12/12** |
| `pnpm exec vitest run` …`NotificationDeliveriesView.test.ts` | **5/5** |
| real-stack | `notification-aliyun-sms-capability.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
