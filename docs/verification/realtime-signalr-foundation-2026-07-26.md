# Realtime SignalR 基础验证记录（2026-07-26）

- 范围：`IRealtimePublisher`、通知 Hub、JWT 鉴权、用户/租户分组、MessagePack、Testing 探针
- 计划：[实施计划](../superpowers/plans/2026-07-26-realtime-signalr-foundation-vertical-slice.md)
- 状态：**Build-verified**

## 自动化证据

| 层 | 结果 |
|---|---|
| Unit | `RealtimeGroupsTests` **2/2** → **349 → 351** |
| Architecture | `BusinessModules_DoNotDependOnSignalRHubContext` 等 **40/40** → **38 → 40** |
| Integration 双库 | `Realtime_hub_and_probe` SQL Server/MySQL **2/2** → **156 → 158** |
| 四处 canonical 门槛 | **351/7/40/158** |

## 行为摘要

- Hub：`/hubs/notifications`；`[Authorize]`；连接后加入 `user:{id}` 与可选 `tenant:{id}` 组
- 发布：`IRealtimePublisher.PublishToUserAsync` / `PublishToGroupAsync`
- 浏览器客户端可通过 `?access_token=` 传递 JWT（与 Identity 会话校验链兼容）
- `Realtime:Enabled=false` 时注入 `NullRealtimePublisher`
- Redis Backplane：配置 `Realtime:RedisBackplaneConnectionString` 或复用 `ConnectionStrings:redis`

## 非目标

- 管理端 SignalR 客户端、Notifications 业务模块、多实例 Backplane 真实栈、Outbox 驱动推送
