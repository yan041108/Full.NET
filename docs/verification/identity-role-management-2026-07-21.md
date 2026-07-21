# Identity Host 角色管理验证记录

- 日期：2026-07-21
- 切片计划：[`2026-07-21-identity-role-management-vertical-slice.md`](../superpowers/plans/2026-07-21-identity-role-management-vertical-slice.md)
- 状态：**Build-verified**（双库集成、双管理端 Mock E2E、OpenAPI 夹具、真实栈列表/403 冒烟；`Verified` 仍须完整人工门禁）

## 交付范围

| 能力 | 证据 |
|---|---|
| Host 角色 API | `ManageHostRoles`：列表/详情/创建/更新/权限替换/禁用 |
| 权限码 | `identity.roles.read` / `identity.roles.write` |
| 系统角色保护 | `identity.roles.system_locked`；`host-administrator` 不可变更 |
| 双库集成 | `IdentityRoleManagementAssertions`（SQL Server + MySQL，Integration 门槛 **89**） |
| Vue / Layui UI | `RolesView.vue`、`roles.js`；导航白名单 `roles` |
| Mock parity E2E | `shell-parity.spec.mjs` 角色列表/创建/禁用（**34** 项总门槛中的 1 场景 × 双端） |
| OpenAPI 夹具 | `contracts/openapi/identity-host-roles-v1.json`、`pnpm test:openapi`、Integration OpenAPI 断言 |
| 真实栈冒烟 | `host-roles.spec.mjs`：管理员列表 + 受限账号 API/UI 403（**24** 项总门槛中的 2 场景 × 双端） |
| 客户端契约 | `packages/client-contracts/src/host-roles.ts` |

## 本地验证（2026-07-21）

| 命令 | 结果 |
|---|---|
| `dotnet build -c Release` | 0 警告 |
| `pnpm test:clients` | 通过（144 项管理端相关 + uni-app 96） |
| `pnpm test:openapi` | 4/4 通过 |
| Identity Unit / Architecture | 99 / 26 通过 |
| Integration 双库 | 本地无 Testcontainers，依赖 CI |

## 明确未做

- 菜单/组织/数据范围 CRUD
- 用户-角色分配 UI
- 角色物理删除
