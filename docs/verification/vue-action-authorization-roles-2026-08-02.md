# Vue 页面/操作精确授权（Identity Roles 切片）验证记录

- 日期：2026-08-02
- 基线提交：`d10a801`（Task 8–9 延续）+ `7092bf3` / `2d603dc` / `d10a801`
- 计划：[`2026-08-02-vue-action-authorization.md`](../superpowers/plans/2026-08-02-vue-action-authorization.md)
- 设计：[`2026-08-02-vue-action-authorization-design.md`](../superpowers/specs/2026-08-02-vue-action-authorization-design.md)
- 状态：**Build-verified**（双库 Integration、迁移 055 恢复、Vue 逐按钮门控、真实栈 E2E）

## 交付范围

| 能力 | 证据 |
| --- | --- |
| Roles 精确权限码 | `identity.roles.read/create/update/assign_permissions/disable/assign_data_scope`；`identity.roles.write` 移出可分配目录 |
| Endpoint 绑定 | `ManageHostRoles/Endpoint.cs` 五个写操作各绑定独立权限 |
| 迁移 055 | SQL Server/MySQL 双库恢复 **6/6** |
| Vue 权限门 | `RolesView.vue` `PermissionGate` + Vitest **8/8** |
| 双库 Integration 动作边界 | `IdentityRoleManagementAssertions.VerifyExactRoleActionPermissionBoundariesAsync` |
| OpenAPI 夹具 | `identity-host-roles-v1.json`、`identity-host-role-data-scope-v1.json` |
| Vue 真实栈按钮裁剪 | `host-roles.spec.mjs`（只读目录、仅禁用按钮、API 403） |

## 本地验证（2026-08-02）

| 命令 | 结果 |
| --- | --- |
| `dotnet test ... --filter "Name~Migration055IdentityRoleActionPermissionsRecoveryTests"` | **6/6** 通过 |
| `dotnet test ... --filter "Name~Host_role_management_follows_contract"` | **2/2** 通过 |
| `dotnet test ... --filter "FullyQualifiedName~AuthorizationCatalogTests"` | **14/14** 通过 |
| `dotnet test ... --filter "FullyQualifiedName~EndpointAuthorizationTests"` | **6/6** 通过 |
| `pnpm --filter @fullnet/admin test -- RolesView.test.ts` | **8/8** 通过 |
| `node --test tests/openapi/identity-host-roles-contract.test.mjs` | **4/4** 通过（含 data-scope） |

## 明确未做（后续 W1 子切片）

- Identity Menus / Sessions / API Keys 粗粒度 `.write` 清零
- Layui 存量端不参与本切片，保持 2026-08-02 冻结树。

## 结论

Identity Roles 作为 W1 首个完整子切片已满足：角色可授予页面/单操作、Vue 无权限不渲染、直调 API 返回 `authorization.permission_denied`、迁移 055 双库可恢复。库存见 [`admin-action-permission-inventory.md`](../roadmap/admin-action-permission-inventory.md)。
