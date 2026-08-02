# Vue 页面/操作精确授权（Identity Menus 切片）验证记录

- 日期：2026-08-02
- 基线提交：Menus 子切片（迁移 057）
- 计划：[`2026-08-02-vue-action-authorization.md`](../superpowers/plans/2026-08-02-vue-action-authorization.md)
- 状态：**Build-verified**

## 交付范围

| 能力 | 证据 |
| --- | --- |
| 精确权限码 | `identity.menus.create` / `update` / `disable`；`identity.menus.write` 退役 |
| Endpoint | POST/PUT/disable 分别绑定 create/update/disable |
| 迁移 057 | SQL Server/MySQL 恢复 **6/6** |
| Vue | `PermissionGate` 创建表单与行内操作 + `session.can` 守卫 |
| Layui | 创建表单与动态按钮 `data-permission` + `applyPermissionVisibility` |
| Integration | `VerifyExactMenuActionPermissionBoundariesAsync` |

## 本地验证（2026-08-02）

| 命令 | 结果 |
| --- | --- |
| `dotnet test ... Migration057IdentityMenuActionPermissionsRecoveryTests` | **6/6** |
| `dotnet test ... Host_menu_management` | **2/2** |
| `dotnet test ... AuthorizationCatalogTests` | **16/16** |
| `dotnet test ... EndpointAuthorizationTests` | **8/8** |