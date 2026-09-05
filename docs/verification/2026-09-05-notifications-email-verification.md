# Notifications 邮箱验证闭环验证记录（任务 11）

> 日期：2026-09-05  
> 状态：`Build-verified`；双库 Integration 与 QQ SMTP 外部认证以目标提交 GitHub Actions / 运维门禁为最终依据。  
> 任务基线：`45040de20b77d7295f43d6039970e580ca93e232`

## 1. 交付边界

本切片闭合 Notifications 收件端点邮件验证全链路（迁移 108 及既有后端保持不变，本任务补齐 Vue 重发倒计时）：

- **验证码**：6 位数字、SHA-256 哈希落库、15 分钟过期、1 分钟发送冷却、最多 5 次错误、旧挑战失效
- **API**：`POST .../verification/send`、`POST .../verification/verify`；限流策略；HTTP 不回显验证码、原邮箱或 SMTP 密钥
- **状态**：校验成功 CAS 升级 `pending → verified`；尝试耗尽升级 `failed`；绑定用户、租户作用域、Profile Version 与端点
- **Vue**：`NotificationPreferencesView` 发送验证码、输入校验、**重发倒计时**（`resendAvailableAtUtc`）、验证后刷新列表状态

## 2. 本地新鲜证据

| 验证 | 结果 |
| --- | --- |
| `RecipientEndpointVerificationCodeHasherTests` | 2 项通过，0 失败 |
| `NotificationPreferencesView` Vitest | 5 项通过，0 失败 |
| Notifications 模块 `dotnet build` | 通过 |

## 3. 保留边界

- 页面真实栈 E2E、双库 Integration（`NotificationRecipientEndpointAssertions` pending→verified）、Linux Native AOT 与人工验收未在本切片执行。
- QQ SMTP 生产账号外部认证仍为 **External-auth-not-verified**。
- 短信验证码、管理员代管、送达回执未纳入本任务。

规则演进结论：未命中规则升级候选。Skill 演进结论：沿用 `fullnet-module-delivery`，无新 Skill 缺口。
