# Notifications Host 公告验证记录（2026-07-26）

## 摘要

交付 Host 作用域公告 CRUD（草稿创建/更新、发布）与实时广播消费点；双管理端 UI 与 OpenAPI/client-contracts 对齐。

## 验证矩阵

| 层 | 结果 |
| --- | --- |
| 双库迁移 | `028_NotificationsAnnouncement.sql` SQL Server/MySQL |
| Integration 双库 | `Host_announcement_management` SQL Server/MySQL **2/2** → **158 → 160** |
| OpenAPI 夹具 | `notifications-host-announcements-v1.json` + Node 契约测试 |
| client-contracts | `host-announcements.ts` + Vitest |
| Mock parity | 「Host 公告列表与创建发布」× 双端 **2/2** → `shell-parity` **56 → 58** |
| 四处 canonical 门槛 | **351/7/40/160** |

## 说明

- 发布路径调用 `IRealtimePublisher.PublishToGroupAsync(RealtimeGroups.HostBroadcast, ...)`；Hub 对无租户 Claim 的连接自动加入 `host:broadcast`。
- 本地 Testcontainers 不可用时 Integration/真实栈依赖 CI 新鲜输出；本记录依据 Release 构建与静态契约测试。

## 关联

- [纵向切片计划](../superpowers/plans/2026-07-26-notifications-host-announcement-vertical-slice.md)
- [Realtime SignalR 基础验证](realtime-signalr-foundation-2026-07-26.md)
- [测试门槛核对记录](test-threshold-audit-2026-07-19.md)
