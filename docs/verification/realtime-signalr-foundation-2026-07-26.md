# Realtime SignalR 基础验证记录（2026-07-26）

- 范围：`IRealtimePublisher`、通知 Hub、JWT 鉴权、用户/租户分组、MessagePack、Testing 探针、Vue/Layui 管理端实时客户端
- 计划：[实施计划](../superpowers/plans/2026-07-26-realtime-signalr-foundation-vertical-slice.md)
- 状态：**Build-verified**

## 自动化证据

| 层 | 结果 |
|---|---|
| Unit | `RealtimeGroupsTests` **2/2** → **349 → 351** |
| Architecture | `BusinessModules_DoNotDependOnSignalRHubContext` 等 **40/40** → **38 → 40** |
| Integration 双库 | `Realtime_hub_and_probe` SQL Server/MySQL **2/2** → **156 → 158** |
| 四处 canonical 门槛 | **359/7/40/172** |
| 浏览器共享契约 | `@fullnet/client-contracts` **72/72** |
| Vue / Layui | **197/197** / **95/95** |
| Mock parity | **99/99** 通过，按项目矩阵跳过 **5** |

## 行为摘要

- Hub：`/hubs/notifications`；`[Authorize]`；连接后加入 `user:{id}` 与可选 `tenant:{id}` 组
- 发布：`IRealtimePublisher.PublishToUserAsync` / `PublishToGroupAsync`
- 浏览器客户端可通过 `?access_token=` 传递 JWT（与 Identity 会话校验链兼容）
- `Realtime:Enabled=false` 时注入 `NullRealtimePublisher`
- Redis Backplane：配置 `Realtime:RedisBackplaneConnectionString` 或复用 `ConnectionStrings:redis`
- 管理端：认证后连接 `/hubs/notifications`；Access Token 仅由内存会话闭包按需提供；切换 Host/租户上下文时先断开旧连接再重连，匿名、退出和卸载时断开
- 通知消费：只接受已登记稳定机器码；Vue/Layui 同步真实未读徽标，并在当前站内信或公告页收到对应事件时刷新 HTTP 数据
- 降级：初始连接失败或断开失败不破坏登录、退出、租户切换与通知页面 HTTP 主流程

## 非目标

- 多实例 Backplane 真实栈、Outbox 修复推送、浏览器真实后端断网/恢复 E2E、非浏览器客户端

## 管理端客户端增补（2026-07-27）

- 实施计划：[Notifications Realtime Admin Client](../superpowers/plans/2026-07-27-notifications-realtime-admin-client.md)
- 依赖：`@microsoft/signalr` **10.0.0**，MIT，已登记 `THIRD-PARTY-NOTICES`
- RED：共享连接器、Vue 状态、Layui 状态与动态未读徽标均先因能力缺失失败；Vue 全量随后发现快照订阅会创建身份控制器并覆盖 Pinia 状态，`App.test.ts` **3/3** 稳定复现
- GREEN：共享契约 **72/72**、Vue **197/197**、Layui **95/95**；Mock parity **99/99**（按矩阵跳过 **5**）；Vue/Layui/共享包生产构建通过
- Mock 边界：纯 Mock web server 通过 `VITE_REALTIME_ENABLED=false` 显式关闭 Hub 和初始未读查询；真实开发与真实栈默认启用，不以 404 探测能力
- 状态仍为 `Build-verified`：本切片不把 Mock/单元/构建证据提升为真实多实例或浏览器断网恢复验证
