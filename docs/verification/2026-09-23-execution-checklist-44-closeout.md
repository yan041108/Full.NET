# 执行清单 44 — SMTP 回执边界与渠道能力展示 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **44**（为真实邮件服务增加验签回执 **或** 明示 SMTP 无回执；渠道能力与回执状态 UI；不把 Provider `Accepted`/`sent` 当作 `Delivered`）。

## 交付锚点

| 层 | 位置 |
|----|------|
| SMTP 描述符 | `SmtpNotificationProviderAdapter` → `ReceiptModeKey=none` |
| 回执处理器 | `NotificationReceiptProcessor`（`none` → `notifications.receipt_not_supported`） |
| 匿名 Webhook | `POST /api/v1/notifications/provider-receipts/{providerTypeKey}` |
| 类型目录 API | `GET /api/v1/notifications/provider-types`（含 `receiptModeKey`） |
| 验签实现 | `INotificationReceiptVerifier`（阿里云短信、钉钉等；**无** 生产邮件 SMTP 验签器） |
| 状态机 | `NotificationDeliveryStateMachine`（`sent` 不自动升为 `delivered`） |
| Vue | `NotificationProviderProfilesView`（`notification-profiles-receipt-mode`）；`NotificationDeliveriesView` 回执时间线 |
| 文档 | [2026-09-01-notifications-smtp-provider](2026-09-01-notifications-smtp-provider.md)（DSN/送达回执不在 SMTP 切片） |
| 单元 | `SmtpNotificationProviderAdapterTests.Descriptor_exposes_closed_email_smtp_schema`、`NotificationReceiptProcessorTests.Provider_without_receipt_capability_fails_closed`、`NotificationDeliveryStateMachineTests` |
| 集成 | `NotificationDeliveryWorkerAssertions`（`email.smtp` Webhook → `ReceiptNotSupported`） |

## NT05 核验结论

- **选定策略**：当前阶段 **不** 接入可验签的邮件厂商 Webhook；`email.smtp` 在目录中声明 **`none`（无回执）**，伪造/误投 Webhook 在处理器层 **fail-closed**。
- **已有**：渠道配置页展示回执模式标签；投递详情保留 receipts 时间线（仅可信回执推进状态）；Worker 发送成功停留在 `sent`，与「不把 Accepted 当 Delivered」一致。
- **本槽**：新增 real-stack `notification-smtp-receipt-capability.spec.mjs`（API `receiptModeKey` + 匿名 Webhook；UI 在 SMTP 启用时验「无回执」）。
- **未验**：Mailgun/SendGrid 等带签名的邮件送达回执（需清单前置「选定真实邮件服务」与厂商账号）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`Descriptor_exposes` + `NotificationReceiptProcessorTests` + `NotificationDeliveryStateMachineTests` | **9/9** |
| `pnpm exec vitest run` …`NotificationProviderProfilesView.test.ts` | **5/5** |
| real-stack | `notification-smtp-receipt-capability.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
