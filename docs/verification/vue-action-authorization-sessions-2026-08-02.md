# Vue 页面/操作精确授权（Identity Sessions 切片）验证记录

- 日期：2026-08-02
- 基线提交：Sessions 子切片（迁移 058）
- 计划：[`2026-08-02-vue-action-authorization.md`](../superpowers/plans/2026-08-02-vue-action-authorization.md)
- 状态：**Build-verified**

## 交付范围

| 能力 | 证据 |
| --- | --- |
| 精确权限码 | `identity.sessions.revoke`；`identity.sessions.write` 退役 |
| Endpoint | `POST .../revoke` 绑定 revoke |
| 迁移 058 | SQL Server/MySQL 恢复 **6/6** |
| Vue | `PermissionGate` 强制下线按钮 + `session.can` 守卫 |
| Integration | `VerifyExactSessionRevokePermissionBoundariesAsync` |

## 本地验证（2026-08-02）

| 命令 | 结果 |
| --- | --- |
| `dotnet test ... Migration058IdentitySessionActionPermissionsRecoveryTests` | **6/6** |
| `dotnet test ... Host_online_sessions` | **2/2** |
| `dotnet test ... AuthorizationCatalogTests` | **17/17** |
| `dotnet test ... EndpointAuthorizationTests` | **9/9** |
