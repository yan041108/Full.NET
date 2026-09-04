# Notifications 收件端点邮件验证闭环验证记录

- **日期：** 2026-09-04
- **状态：** Build-verified（本地单元/Vue）；双库 Integration 待 CI；QQ SMTP 仍为 **External-auth-not-verified**
- **范围：** 成对迁移 `108_NotificationsRecipientEndpointVerification.sql`、验证码挑战表、`pending → verified` 自动升级、发送/校验 API、限流、Vue 偏好页验证 UX

## 交付能力

- `POST /api/v1/notifications/my-recipient-endpoints/{id}/verification/send`：向 `pending` 邮箱端点发送 6 位验证码；挑战只保存 SHA-256 哈希；1 分钟应用层冷却 + 15 分钟窗口限流。
- `POST /api/v1/notifications/my-recipient-endpoints/{id}/verification/verify`：校验成功后 CAS 升级 `verified`；错误码/尝试耗尽升级 `failed`。
- Delivery Worker 仍只消费 `verified` 端点；HTTP 不能直接写入验证状态。
- Vue `NotificationPreferencesView`：待验证端点展示发送/输入/验证；明示 QQ SMTP **External-auth-not-verified**，不夸大“已发信”。

## 本地验证

- `RecipientEndpointVerificationCodeHasherTests` 2/2
- `NotificationPreferencesView` Vitest 4/4
- Notifications 模块 `dotnet build` 成功
- 双库 Integration（`NotificationRecipientEndpointAssertions` 含 pending→verified 流程）未在本地 Docker 环境执行，交 CI

## 未声明

- QQ SMTP 生产账号外部认证（仍为 External-auth-not-verified）
- 短信验证码、管理员代管、容量/送达回执
