# Notifications 外部渠道送达回执与退信验证记录（任务 12）

> 日期：2026-09-05  
> 状态：`Build-verified`；双库 Integration 与 Provider 生产验签以目标提交 GitHub Actions 为最终门禁。  
> 任务基线：`c0df20fcb1f931dbe6b0dc858d03aaf096ea4929`

## 1. 交付边界

本切片闭合外部渠道送达回执查询与退信展示（回执接收处理器与状态机为既有能力，本任务扩展详情 API 与 Vue）：

- **关联**：`ProviderTypeKey + ProviderMessageId` 精确匹配投递；不同 Provider 不得串扰
- **状态**：Accepted、Delivered、Bounced、Rejected 等映射为稳定 `mappedStatusKey`；乱序/重复回执不回退终态
- **安全**：验签、幂等、未知 Provider 失败关闭；原始 Body 不入库、不回显
- **边界**：回执只推进 `fn_notifications_delivery` 状态，不修改 Workflow 业务状态
- **API**：`GET /api/v1/notifications/deliveries/{id}` 返回 `receipts[]` 时间线
- **Vue**：投递运维详情展示回执时间线、退信原因与人工重试入口

## 2. 本地新鲜证据

| 验证 | 结果 |
| --- | --- |
| `NotificationDeliveryStateMachineTests` | 5 项通过，0 失败 |
| `NotificationReceiptProcessorTests` | 2 项通过，0 失败 |
| `NotificationDeliveriesView` Vitest | 3 项通过，0 失败 |
| `notifications-deliveries-receipts` OpenAPI 夹具测试 | 通过 |
| Notifications 模块 `dotnet build` | 通过 |
| OpenAPI 客户端生成 | 通过 |

## 3. 保留边界

- 页面真实栈 E2E、双库 Integration（`NotificationDeliveryWorkerAssertions` 含回执流程）、Linux Native AOT 与人工验收未在本切片执行。
- `email.smtp` 生产 Webhook 验签与 QQ 外部账号仍为 **External-auth-not-verified**。
- 短信回执、容量与退信全链路生产矩阵未纳入本任务。

规则演进结论：未命中规则升级候选。Skill 演进结论：沿用 `fullnet-module-delivery`，无新 Skill 缺口。
