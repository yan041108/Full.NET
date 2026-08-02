# Vue 页面/操作精确授权：Organization Positions

- 日期：2026-08-02
- 迁移：063
- 退役权限：`organization.positions.write`

## 目标权限

| 操作 | 权限码 |
| --- | --- |
| 创建 | `organization.positions.create` |
| 编辑 | `organization.positions.update` |
| 禁用 | `organization.positions.disable` |
| 绑定机构 | `organization.positions.assign_unit` |
| 绑定职级 | `organization.positions.assign_position_level` |

## 验证

| 层级 | 夹具 |
| --- | --- |
| 单元 | `Tenant_org_positions_actions_bind_to_exact_permissions` |
| 架构 | `Api_v1_endpoints_do_not_bind_retired_organization_positions_write` |
| 迁移 | `Migration063OrganizationPositionActionPermissionsRecoveryTests` |
| Integration | `VerifyExactOrganizationPositionActionPermissionBoundariesAsync` |
| OpenAPI | `organization-tenant-positions-contract.test.mjs` |