# Vue 页面/操作精确授权（Identity Users 切片）验证记录

- 日期：2026-08-02
- 基线提交：`89d0a13`（Task 8）+ Task 9 验收提交
- 计划：[`2026-08-02-vue-action-authorization.md`](../superpowers/plans/2026-08-02-vue-action-authorization.md)
- 设计：[`2026-08-02-vue-action-authorization-design.md`](../superpowers/specs/2026-08-02-vue-action-authorization-design.md)
- 状态：**Build-verified**（双库 Integration、Vue 真实栈按钮/API 403、迁移 054 恢复；Layui 未改动；全模块权限清零队列见 Task 10）

## 交付范围

| 能力 | 证据 |
| --- | --- |
| 授权目录操作模型 | `AuthorizationActionDefinition`、目录校验 Unit |
| Users 精确权限码 | `identity.users.read/create/update/assign_roles/reset_password/disable/enable/export`；`identity.users.write` 移出可分配目录 |
| 授权树 API | `GET /api/v1/identity/authorization-tree`、OpenAPI 夹具 |
| 角色页面/操作层级 | `identity.roles.action_requires_page`、Integration 层级断言 |
| 迁移 054 | SQL Server/MySQL 双库恢复 **6/6** |
| Vue 权限门 | `PermissionGate`、`usePermission().can()` |
| 角色授权树 UI | `RolesView.vue` + Vitest **8/8** |
| Users 逐按钮门控 | `UsersView.vue` + Vitest **9/9** |
| 双库 Integration 动作边界 | `IdentityUserManagementAssertions.VerifyExactActionPermissionBoundariesAsync` |
| 角色分配动作边界 | `IdentityUserRolesManagementAssertions.VerifyAssignRolesPermissionBoundaryAsync` |
| 权限撤销后拒绝 | `IdentityRoleManagementAssertions.VerifyRolePermissionRevocationDeniesActionAsync` |
| Vue 真实栈按钮缺失 | `host-users.spec.mjs`（只读目录、仅禁用按钮） |
| 真实栈 API 403 | `host-users.spec.mjs`、`permission-denied.spec.mjs` |
| 角色授权树冒烟 | `host-roles.spec.mjs`（Vue） |

## 本地验证（2026-08-02）

| 命令 | 结果 |
| --- | --- |
| `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release` | 0 错误 |
| `dotnet test ... --filter "Name~Host_user_management"` | **2/2** 通过（SQL Server + MySQL） |
| `dotnet test ... --filter "Name~Host_role_management|Name~Host_user_roles"` | **4/4** 通过 |
| `pnpm --filter @fullnet/admin test -- UsersView.test.ts RolesView.test.ts` | **17/17** 通过 |
| `pnpm --filter @fullnet/client-contracts test` | **117/117** 通过 |
| `pnpm test:integration:affected:plan -- --snapshot admin-action-permissions-identity-users-20260802 --phase inner` | 影响集含 Identity 断言与迁移 054 |

## 明确未做（Task 10）

- 全后台粗粒度 `.write` 权限清零清单与架构门禁
- Layui 管理端逐按钮授权（项目策略：零新增）
- Identity Roles 自身操作级权限（`identity.roles.assign_permissions` 等）独立切片

## 结论

Identity Users 作为首个完整样板已满足：角色可授予页面+单操作、Vue 无权限不渲染、直接 API 返回 `authorization.permission_denied`、迁移 054 双库可恢复。`adminnet-feature-parity`「菜单、页面与按钮权限管理」可标记 **Build-verified**（Users 切片）；其余模块沿用 Task 10 清单推进。