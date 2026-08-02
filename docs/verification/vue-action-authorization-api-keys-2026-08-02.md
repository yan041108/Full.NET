# Vue 页面/操作精确授权（Identity API Keys 切片）验证记录

- 日期：2026-08-02
- 基线提交：API Keys 子切片（迁移 059）
- 计划：[`2026-08-02-vue-action-authorization.md`](../superpowers/plans/2026-08-02-vue-action-authorization.md)
- 状态：**Build-verified**

## 交付范围

| 能力 | 证据 |
| --- | --- |
| 精确权限码 | `identity.api_keys.create` / `disable` / `rotate`；`identity.api_keys.write` 退役 |
| Endpoint | POST/create、POST/disable、POST/rotate 分别绑定精确权限 |
| 迁移 059 | SQL Server/MySQL 恢复 **6/6** |
| Vue | `PermissionGate` 创建表单与行内操作 + `session.can` 守卫 |
| Layui | 表单与动态按钮 `data-permission` + `applyPermissionVisibility` |
| Integration | `VerifyExactApiKeyActionPermissionBoundariesAsync`；委派管理员越权用例同步动作权限层级 |

## 本地验证（2026-08-02）

| 命令 | 结果 |
| --- | --- |
| `dotnet test ... Migration059IdentityApiKeyActionPermissionsRecoveryTests` | **6/6** |
| `dotnet test ... Host_api_keys` | **2/2** |
| `dotnet test ... AuthorizationCatalogTests` | **18/18** |
| `dotnet test ... EndpointAuthorizationTests` | **10/10** |