# Notifications Host 公告验证记录（2026-07-26）

## 摘要

交付 Host 作用域公告 CRUD（草稿创建/更新、发布）与实时广播消费点；双管理端 UI 与 OpenAPI/client-contracts 对齐。

## 验证矩阵

| 层 | 结果 |
| --- | --- |
| 双库迁移 | `028_NotificationsAnnouncement.sql` SQL Server/MySQL |
| Integration 双库 | `Host_announcement_management` SQL Server/MySQL **2/2** |
| OpenAPI 夹具 | `notifications-host-announcements-v1.json` + Node 契约测试 |
| client-contracts | `host-announcements.ts` + Vitest |
| Mock parity | 「Host 公告列表与创建发布」× 双端 **2/2** → `shell-parity` **56 → 58** |
| 四处 canonical 门槛 | **359/7/40/172** |

## 说明

- 公告状态与 `fullnet.notifications.announcement.published` v1 在同一数据库事务内提交；Outbox 写入失败会回滚公告发布。
- 提交后仍直接调用实时发布器作为低延迟快路径；快路径失败只记录告警，不反转已提交发布结果，Worker 随后通过 Redis Backplane 至少一次修复广播。
- 修复 Handler 重复执行只触发相同公告目录刷新，不产生新的业务写入，因此声明 `NaturallyIdempotent`。
- Hub 对无租户 Claim 的连接自动加入 `host:broadcast`。

## Outbox 修复增补（2026-07-30）

- Unit：Notifications 修复 Handler/注册 **5/5**，写路径事务边界聚焦集合 **5/5**。
- Integration：SQL Server/MySQL Notifications API **2/2**，真实读取并反序列化公告 Outbox 载荷。
- Worker：使用仅发布型 SignalR 注册；启用 Realtime 时必须配置专用 Backplane 或 `ConnectionStrings:redis`，禁止无 Redis 时把本地空 Hub 发布误记为修复成功。

## 关联

- [纵向切片计划](../superpowers/plans/2026-07-26-notifications-host-announcement-vertical-slice.md)
- [Realtime SignalR 基础验证](realtime-signalr-foundation-2026-07-26.md)
- [测试门槛核对记录](test-threshold-audit-2026-07-19.md)
