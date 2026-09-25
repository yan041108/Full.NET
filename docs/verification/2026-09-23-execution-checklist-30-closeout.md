# 执行清单 30 — 按用户撤销全部会话 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **30**（`revoke-all`、会话策略只读提示、刷新令牌失效）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `GET /api/v1/identity/session-policy`、`POST …/online-sessions/users/{userId}/revoke-all` |
| 服务 | `HostOnlineSessionManagementService.RevokeAllByUserAsync` |
| 权限 | `identity.sessions.read`、`identity.sessions.revoke` |
| Vue | `OnlineSessionsView.vue`（策略提示、`全部下线`） |
| 契约 | `identity-host-online-sessions-v1` OpenAPI |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`HostOnlineSessionManagementServiceTests` | 撤销边界与 OIDC 会话 |
| real-stack | `host-online-sessions.spec.mjs` 增补清单 30 API + Vue |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
