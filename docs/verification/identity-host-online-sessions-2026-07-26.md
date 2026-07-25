# Identity Host 在线用户与强制下线验证记录（2026-07-26）

- 范围：活跃刷新会话列表；强制下线（撤销 family）；Vue/Layui 管理页
- 计划：[实施计划](../superpowers/plans/2026-07-26-identity-host-online-sessions-vertical-slice.md)
- 状态：**Build-verified**（`adminnet-feature-parity`「在线用户与强制下线」仍为部分交付）

## 自动化证据

| 层 | 结果 |
|---|---|
| Integration 双库 | `Host_online_sessions` SQL Server/MySQL **2/2** → **152 → 154** |
| OpenAPI 夹具 | `identity-host-online-sessions-v1` 静态 **2/2** |
| client-contracts | `host-online-sessions` **1/1** |
| Vue API 单测 | `online-sessions.test.ts` **2/2** |
| Layui 单测 | `online-sessions.test.js` **1/1** |
| Mock parity | 「在线用户列表与强制下线」× 双端 **2/2** → `shell-parity` **52 → 54** |
| 四处 canonical 门槛 | **349/7/38/154** |

## 收尾修复（2026-07-26）

- Mock 响应 `id`/`userId` 须为 UUID v7 字符串，否则 Vue 端 `isHostOnlineSessionPage` 校验失败并呈现 `client.host_online_session_failed`。
- Layui `app.js` 补回缺失的 `createAccessLogsController` 导入；否则整站 JS 初始化失败、导航无法渲染。

## 本地备注

- 本机无 Testcontainers 时 Integration `Host_online_sessions` 无法在本地复跑；以 CI `minimum-expected-tests 154` 为准。

## 增补（2026-07-26，真实栈 E2E 脚本）

| 层 | 结果 |
|---|---|
| 脚本 | `tests/e2e/admin-real-stack/tests/host-online-sessions.spec.mjs`（2 场景 × 双端） |
| 真实栈门槛 | **80 → 84** |
| 新鲜实跑 | 本机无 Testcontainers 时未重跑；以 CI `real-stack-e2e` / `real-stack-e2e-mysql` 为准 |

## 行为摘要

- 在线判定：`ConsumedAtUtc`/`RevokedAtUtc` 为空且 `ExpiresAtUtc > now`，用户为 Host 作用域
- 强制下线：撤销目标会话所属 refresh family；访问令牌在下次校验时返回 `identity.session_not_active`

## 非目标

- 实时在线状态推送、按租户筛选、审计专码
