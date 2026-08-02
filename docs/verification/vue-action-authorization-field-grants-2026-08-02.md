# Vue 页面/操作精确授权（Identity Field Grants 切片）验证记录

- 日期：2026-08-02
- 基线提交：Field Grants 子切片（迁移 056）
- 计划：[`2026-08-02-vue-action-authorization.md`](../superpowers/plans/2026-08-02-vue-action-authorization.md)
- 状态：**Build-verified**

## 交付范围

| 能力 | 证据 |
| --- | --- |
| 精确权限码 | `identity.role_field_grants.replace`；`identity.role_field_grants.write` 退役 |
| Endpoint | `PUT .../field-grants` 绑定 replace |
| 迁移 056 | SQL Server/MySQL 恢复 **6/6** |
| Vue | `PermissionGate` 保存按钮 + `session.can` 守卫 |
| Integration | `VerifyExactFieldGrantPermissionBoundariesAsync` |

## 本地验证（2026-08-02）

| 命令 | 结果 |
| --- | --- |
| `dotnet test ... Migration056IdentityRoleFieldGrantActionPermissionsRecoveryTests` | **6/6** |
| `dotnet test ... Host_role_field_grants` | **2/2** |
| `dotnet test ... AuthorizationCatalogTests` | **15/15** |
| `dotnet test ... EndpointAuthorizationTests` | **7/7** |
