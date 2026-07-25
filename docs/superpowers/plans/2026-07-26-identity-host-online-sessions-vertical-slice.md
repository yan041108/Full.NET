# Identity Host 在线用户与强制下线纵向切片

- 日期：2026-07-26
- 状态：**Build-verified**
- 验证：[验证记录](../../verification/identity-host-online-sessions-2026-07-26.md)

## 目标

Host 管理员分页查看活跃刷新会话，并强制下线指定会话（撤销整个 family）；双端管理 UI 与 Mock parity。

## 交付清单

1. [x] `GET /api/v1/identity/online-sessions`（`identity.sessions.read`）
2. [x] `POST /api/v1/identity/online-sessions/{sessionId}/revoke`（`identity.sessions.write`）
3. [x] Integration：`Host_online_sessions` SQL Server/MySQL **2/2** → **152 → 154**
4. [x] OpenAPI：`identity-host-online-sessions-v1.json`
5. [x] Vue/Layui 只读列表 + 强制下线
6. [x] `shell-parity`「在线用户列表与强制下线」× 双端 → **52 → 54**

## 非目标

- SignalR 实时推送、IP/设备指纹展示、租户作用域在线用户

## Task 5: 真实栈 E2E

1. [x] `host-online-sessions.spec.mjs`：登录后会话列表；受限账号 403 与导航裁剪。
2. [x] 真实栈门槛 **80 → 84**。
