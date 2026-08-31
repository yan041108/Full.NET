# Notifications Tenant Inbox 验证（2026-08-31）

- **任务：** P1 统一消息中心 Task 3（Tenant Inbox 与权威未读数）
- **基线：** `4edd1718c3713a3da2f0f5236ac4cfb4e6a4582e`（`main`）
- **快照：** `notifications-tenant-inbox-20260831`
- **范围：** 成对迁移 `105_NotificationsInboxScopeExtension.sql`（Inbox `ScopeKey`/`TenantScopeKey`/`IntentId`、Intent 幂等唯一、RecipientEndpoint 验证状态）；Host 旧 Inbox API 路径不变；租户发信 `POST /api/v1/notifications/tenant-inbox-messages`；未读数按受信 Scope 读库；Vue 收到 SignalR 未读变更后重拉 HTTP。不含 Template/Intent HTTP API、多 Profile 控制面、Delivery Worker、真实 Provider。

## 证据

| 命令 | 结果 |
|---|---|
| Notifications Unit（Scope/掩码/Intent 幂等/Host 拒租户会话/既有 Inbox） | **33/33** |
| Architecture（105 契约 + Notifications AOT 参数/物化器 + MemoryPack 事件） | **8/8** |
| `pnpm test:naming` | **30/30** |
| 错误资源完整性 | **5/5** |
| 站内信 OpenAPI 夹具 | **1/1** |
| Vue `ui/admin` realtime 未读刷新 | **6/6** |
| `pnpm test:inner -- --snapshot notifications-tenant-inbox-20260831` | 通过；inner 执行 integration-matrix（669）+ 105 MySQL 恢复 **1/1** |
| `pnpm test:slice -- --snapshot notifications-tenant-inbox-20260831` | 发现 4 项，**4/4**（Notifications SQL Server/MySQL API，含 Host 回归与 Tenant 隔离；105 双库恢复） |
| 105 SQL 安全扫描 | **0** 条 105 违规；未跑完整 `pnpm test:sql-safety`（历史 009/011/051/093 豁免行号偏差仍在，本任务未修改这些文件） |

## 结论

- Tenant Inbox 与权威未读数达到与当前切片相称的 **Build-verified**。Host 公告/Inbox 既有范围不降级，也不升 `Verified`。
- 不建第二套 Inbox。请求体 `tenantId` 被忽略；跨租户已读返回 `notifications.inbox_message_not_found`（404）；错作用域发信返回 `notifications.inbox_scope_forbidden`（403）。
- 生产 Provider 目录保持为空。容量继续 `Capacity-not-verified`。
- 本机 Windows 不把 Linux Native AOT publish 标为通过。未改 Layui。
- 本任务未触发规则或 Skill 演进。
