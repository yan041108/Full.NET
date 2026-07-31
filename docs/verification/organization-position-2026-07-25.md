# Organization 职位管理验证记录（2026-07-25）

- 增补日期：2026-07-29
- 范围：`fn_organization_position`、`fn_organization_position_level`；租户职位与职级 CRUD；机构与职级绑定；Vue/Layui 管理页
- 计划：[`2026-07-25-organization-position-vertical-slice.md`](../superpowers/plans/2026-07-25-organization-position-vertical-slice.md)
- 状态：**Build-verified**（双库双端真实栈绑定已通过；完整 `main` CI 与发布前人工验收仍未执行）

## 证据

| 层 | 结果 |
| --- | --- |
| 迁移 | `025_OrganizationPosition.sql`、`034_OrganizationPositionUnit.sql`、`035_OrganizationPositionLevel.sql`、`036_OrganizationPositionLevelBinding.sql` SQL Server + MySQL |
| Integration | `Tenant_position_management` SQL Server/MySQL **2/2** |
| OpenAPI 静态 | `organization-tenant-positions-contract.test.mjs` **2/2** |
| Mock parity | 「职位创建、机构与职级绑定及禁用」× 双端 **2/2** |
| 真实栈 | `host-org-positions.spec.mjs` 经 Vue/Layui 发起真实机构/职级绑定并由 API 回读；SQL Server **2/2**、MySQL **2/2**。`host-org-position-levels.spec.mjs` 经双端完成职级创建、更新、禁用并由 API 回读；SQL Server **2/2**、MySQL **2/2** |

## 边界

- 租户上下文必需；编码租户内唯一；禁用保留历史。
- 不包含用户-职位分配真实栈。
