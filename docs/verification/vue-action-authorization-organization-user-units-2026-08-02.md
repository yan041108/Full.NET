# Vue 页面/操作精确授权：Organization User Units

- 日期：2026-08-02
- 迁移：066
- 退役权限：`organization.user_units.write`

## 目标权限

| 操作 | 权限码 |
| --- | --- |
| 分配 / 可分配用户 | `organization.user_units.create` |
| 设为主部门 | `organization.user_units.update` |
| 取消隶属 | `organization.user_units.disable` |

## 验证

| 层级 | 夹具 |
| --- | --- |
| 单元 | `Tenant_org_user_units_actions_bind_to_exact_permissions` |
| 架构 | `Api_v1_endpoints_do_not_bind_retired_organization_user_units_write` |
| 迁移 | `Migration066OrganizationUserUnitActionPermissionsRecoveryTests` |
| Integration | `VerifyExactOrganizationUserUnitActionPermissionBoundariesAsync` |
| OpenAPI | `organization-tenant-user-units-contract.test.mjs` |