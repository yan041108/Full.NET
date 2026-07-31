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

- 发信与 `fullnet.notifications.inbox.received` v1、真实已读状态变更与 `fullnet.notifications.inbox.read_state_changed` v1 在同一事务内提交；Outbox 写入失败会回滚业务写入。
- 已经处于已读状态的单条消息和零行更新的全部已读不新增 Outbox 事件。
- 提交后直接推送仍是低延迟快路径，失败只记录告警；Worker Handler 失败向 Outbox 传播并进入既有重试/死信路径。
- 修复消费时重新查询当前未读数，不复用事件产生时的旧计数，避免延迟或并发事件乱序后把客户端徽标回退。
- 重复修复只触发相同消息目录刷新并发布当前未读数，不产生新的数据库业务状态，因此声明 `NaturallyIdempotent`。

## Outbox 修复增补（2026-07-30）

- Unit：Notifications 修复 Handler/注册 **5/5**，写路径事务边界聚焦集合 **5/5**。
- Integration：SQL Server/MySQL Notifications API **2/2**，真实读取并反序列化送达与已读状态 Outbox 载荷。
- Worker 实时修复 Integration：SQL Server/MySQL **2/2**；写入 API 显式关闭 Realtime，独立 Worker 领取真实 Outbox，经 Redis Backplane 向另一 API 节点的已鉴权 SignalR 客户端发布送达消息与当前未读数，发布完成后才确认 Outbox。
- 浏览器真实栈 E2E 已在 SQL Server 与 MySQL 分别通过 Vue/Layui 两个项目：恢复端建立真实 SignalR 连接后被显式断开，独立 Worker 在其离线期间消费站内信 Outbox 并经 Redis Backplane 推送到在线观察端；恢复端重新上线后无需刷新页面或发送第二条消息，即通过重连补拉恢复未读数。
- 真实栈按 API/Worker 角色分离启动，并以 Worker 日志中的 `fullnet.notifications.inbox.received` processed 记录约束“数据库已写入”与“Outbox 已成功发布”的边界。
- 生产多副本编排与告警仍待补证，不据此把能力提升为 `Verified`。

## 关联

- [纵向切片计划](../superpowers/plans/2026-07-26-notifications-inbox-message-vertical-slice.md)
- [Host 公告验证记录](notifications-host-announcement-2026-07-26.md)
- [测试门槛核对记录](test-threshold-audit-2026-07-19.md)
