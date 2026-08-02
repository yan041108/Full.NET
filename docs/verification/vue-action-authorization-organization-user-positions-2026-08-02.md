# Vue 页面/操作精确授权：Organization User Positions

- 日期：2026-08-02
- 迁移：065
- 退役权限：`organization.user_positions.write`

## 目标权限

| 操作 | 权限码 |
| --- | --- |
| 分配 / 可分配用户 | `organization.user_positions.create` |
| 设为主职位 | `organization.user_positions.update` |
| 取消隶属 | `organization.user_positions.disable` |

## 验证

| 层级 | 夹具 |
| --- | --- |
| 单元 | `Tenant_org_user_positions_actions_bind_to_exact_permissions` |
| 架构 | `Api_v1_endpoints_do_not_bind_retired_organization_user_positions_write` |
| 迁移 | `Migration065OrganizationUserPositionActionPermissionsRecoveryTests` |
| Integration | `VerifyExactOrganizationUserPositionActionPermissionBoundariesAsync` |
| OpenAPI | `organization-tenant-user-positions-contract.test.mjs` |