# Vue 页面/操作精确授权：Organization Position Levels

- 日期：2026-08-02
- 迁移：064
- 退役权限：`organization.position_levels.write`

## 目标权限

| 操作 | 权限码 |
| --- | --- |
| 创建 | `organization.position_levels.create` |
| 编辑 | `organization.position_levels.update` |
| 禁用 | `organization.position_levels.disable` |

## 验证

| 层级 | 夹具 |
| --- | --- |
| 单元 | `Tenant_org_position_levels_actions_bind_to_exact_permissions` |
| 架构 | `Api_v1_endpoints_do_not_bind_retired_organization_position_levels_write` |
| 迁移 | `Migration064OrganizationPositionLevelActionPermissionsRecoveryTests` |
| Integration | `VerifyExactOrganizationPositionLevelActionPermissionBoundariesAsync` |
| OpenAPI | `organization-tenant-position-levels-contract.test.mjs` |