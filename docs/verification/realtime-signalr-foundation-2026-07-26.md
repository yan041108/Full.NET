# Realtime SignalR 基础验证记录（2026-07-26）

- 范围：`IRealtimePublisher`、通知 Hub、JWT 鉴权、用户/租户分组、MessagePack、专用 Redis ready 与双节点故障恢复
- 计划：[实施计划](../superpowers/plans/2026-07-26-realtime-signalr-foundation-vertical-slice.md)
- 故障恢复：[验证记录](realtime-redis-backplane-recovery-2026-07-26.md)
- 状态：**Build-verified**

## 自动化证据

| 层 | 结果 |
|---|---|
| Unit | `RealtimeGroupsTests` **2/2** → **349 → 351** |
| Architecture | `BusinessModules_DoNotDependOnSignalRHubContext` 等 **40/40** → **38 → 40** |
| Integration 双库 | `Realtime_hub_and_probe` SQL Server/MySQL **2/2** → **156 → 158** |
| Redis 故障恢复 | SQL Server/MySQL 双 API 节点 **2/2**；`HealthEndpointTests` **8/8** |
| 当前 canonical 门槛 | **392/7/49/189** |

## 行为摘要

- Hub：`/hubs/notifications`；`[Authorize]`；连接后加入 `user:{id}` 与可选 `tenant:{id}` 组
- 发布：`IRealtimePublisher.PublishToUserAsync` / `PublishToGroupAsync`
- 浏览器客户端可通过 `?access_token=` 传递 JWT（与 Identity 会话校验链兼容）
- `Realtime:Enabled=false` 时注入 `NullRealtimePublisher`
- Redis Backplane：配置 `Realtime:RedisBackplaneConnectionString` 或复用 `ConnectionStrings:redis`
- 配置 Backplane 后注册 `realtime-backplane` ready 探针；中断不影响 live/startup
- 运行连接保留后台重连并使用 `fullnet:{environment}:signalr:` Channel Prefix
- 固定 Redis 端点 stop/start 后，无需重启两个 API 宿主或 SignalR 客户端即可恢复跨节点投递

## 非目标

- 管理端 SignalR 客户端、生产多副本编排/告警、Redis Cluster/Sentinel、Outbox 驱动推送
