# Notifications 站内信收件箱验证记录（2026-07-26）

## 摘要

在 Notifications 模块上交付 Host 发信、个人收件箱、未读计数与已读状态管理，并通过 `IRealtimePublisher` 向用户组推送送达与未读数变更事件。

## 验证矩阵

| 层 | 结果 |
| --- | --- |
| 双库迁移 | `029_NotificationsInboxMessage.sql` SQL Server/MySQL |
| Integration 双库 | 扩展现有 `Host_announcement_and_inbox_management` 用例，SQL Server/MySQL **2/2** |
| OpenAPI 夹具 | `notifications-inbox-messages-v1.json` + Node 契约测试 |
| client-contracts | `inbox-messages.ts` + Vitest |
| Mock parity | 「消息中心列表与发信」× 双端 **2/2** → `shell-parity` **58 → 60** |
| 四处 canonical 门槛 | **359/7/40/172** |

## 一致性边界

- 发信、标记已读和全部已读先提交数据库事务，再发布送达与未读数事件。
- 实时推送属于尽力通知；失败只记录告警，不反转已提交业务结果。需要可靠传播的业务事实仍使用事务 Outbox。

## 关联

- [纵向切片计划](../superpowers/plans/2026-07-26-notifications-inbox-message-vertical-slice.md)
- [Host 公告验证记录](notifications-host-announcement-2026-07-26.md)
- [测试门槛核对记录](test-threshold-audit-2026-07-19.md)
