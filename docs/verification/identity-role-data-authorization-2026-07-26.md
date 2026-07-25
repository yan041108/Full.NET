# 角色与数据授权对标收口验证（2026-07-26）

## 摘要

「角色与数据授权」能力已由多条纵向切片完整交付；本记录将其在 Admin.NET 对标矩阵中收口为 **Build-verified**。

| 能力 | 交付物 | 验证记录 |
| --- | --- | --- |
| Host 角色管理 | CRUD、权限替换、禁用 | [identity-role-management-2026-07-21.md](identity-role-management-2026-07-21.md) |
| 角色数据范围 | `GET/PUT .../data-scope`、双端 UI | [identity-role-data-scope-2026-07-21.md](identity-role-data-scope-2026-07-21.md) |
| 用户-角色分配 | Host 用户角色替换 API + UI | [identity-user-roles-assignment-2026-07-21.md](identity-user-roles-assignment-2026-07-21.md) |
| 运行时数据范围 | 多角色并集 + 机构过滤 | [identity-runtime-data-scope-2026-07-21.md](identity-runtime-data-scope-2026-07-21.md) |
| 机构数据过滤 | Organization 租户机构/隶属只读 SQL | [organization-unit-management-2026-07-21.md](organization-unit-management-2026-07-21.md) |

## Integration 夹具（既有，无新增）

| 测试类 | 场景 |
| --- | --- |
| `IdentityApiSqlServerTests` / `IdentityApiMySqlTests` | `Host_role_management`、`Host_role_data_scope`、`Host_user_roles` |
| `OrganizationApi*` | `Organization_data_scope_filtering` |
| OpenAPI | `OpenApiHostRolesContractAssertions`、`OpenApiHostRoleDataScopeContractAssertions`、`OpenApiHostUserRolesContractAssertions` |

## 四处 canonical 门槛

**352/7/40/168**（本收口不新增测试）

## 关联

- [实施计划](../superpowers/plans/2026-07-26-identity-role-data-authorization-parity-closure.md)
- [Admin.NET 对标矩阵](../roadmap/adminnet-feature-parity.md)
- [能力状态矩阵](../roadmap/capability-status.md)（最小 RBAC 已为 Build-verified）
