# 执行清单 43 — 通知 Intent 邮件附件 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **43**（SMTP 邮件附件经 Files 受控引用、`NotificationIntentAttachmentCoordinator` claim/释放、`MailKit` 多部分投递；厂商 SMTP 凭据验收不冒充本机通过）。

## 交付锚点

| 层 | 位置 |
|----|------|
| 协调器 | `NotificationIntentAttachmentCoordinator`（校验/claim/投影/终端释放） |
| 规则 | `NotificationIntentAttachmentRules`（扩展名、大小、数量、仅 `email` 渠道） |
| 投递 | `NotificationAttachmentLoader`、`MailKitSmtpTransport`（multipart） |
| Files Port | `IHostFileReferenceClaimService`、`IHostFileDescriptorReader` |
| API | `POST /api/v1/notifications/intents`（`attachmentFileIds`） |
| Vue | `NotificationDeliveriesView`（Intent 附件计数） |
| 迁移 | `144_NotificationsIntentAttachment`（双库） |
| 单元 | `NotificationIntentAttachmentRulesTests`、`MailKitSmtpTransportAttachmentTests` |

## NT04 核验结论

- **已有**：上传人自有 Files 引用、扩展名/大小有界、Inbox 渠道拒绝附件、Email Intent 持久化附件投影；SMTP 层支持 MIME 附件（单元验 multipart）。
- **本槽**：新增 real-stack `notification-intent-attachments.spec.mjs`（Inbox 拒绝 / Email 接受 / `.exe` 拒绝）。
- **未验**：真实外部 SMTP 发信端到端（清单要求不冒充本地通过）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`NotificationIntentAttachmentRules` + `MailKitSmtpTransportAttachment` | **4/4** |
| `pnpm exec vitest run` …`notification-intents.test.ts` | **1/1** |
| real-stack | `notification-intent-attachments.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
