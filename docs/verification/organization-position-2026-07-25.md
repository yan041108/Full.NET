# Organization 职位管理验证记录（2026-07-25）

- 范围：`fn_organization_position`；租户职位 CRUD；Vue/Layui 管理页
- 计划：[`2026-07-25-organization-position-vertical-slice.md`](../superpowers/plans/2026-07-25-organization-position-vertical-slice.md)
- 状态：**Build-verified**（职级、职位-机构绑定未交付；不能标记 `Verified`）

## 证据

| 层 | 结果 |
| --- | --- |
| 迁移 | `025_OrganizationPosition.sql` SQL Server + MySQL |
| Integration | `Tenant_position_management` SQL Server/MySQL **2/2** |
| OpenAPI 静态 | `organization-tenant-positions-contract.test.mjs` **2/2** |
| Mock parity | 「职位列表、创建与禁用」× 双端 **2/2** → `shell-parity` **48 → 50** |
| 真实栈 | 新增 `host-org-positions.spec.mjs`；门槛 **70 → 74**；完整容器矩阵由 CI 覆盖 |
| 四处 canonical | **349/7/38/148** |

## 边界

- 租户上下文必需；编码租户内唯一；禁用保留历史。
- 不包含职级、用户-职位分配、职位-机构绑定。
