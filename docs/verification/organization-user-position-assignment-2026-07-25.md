# Organization 用户-职位隶属验证记录（2026-07-25）

- 范围：`fn_organization_user_position`；租户用户-职位隶属 CRUD；Vue/Layui 管理页
- 计划：[实施计划](../superpowers/plans/2026-07-25-organization-user-position-assignment-vertical-slice.md)
- 状态：**Build-verified**（职级、职位-机构绑定未交付；不能标记 `Verified`）

## 自动化证据

| 层 | 结果 |
|---|---|
| Integration 双库 | Organization user-positions SQL Server/MySQL **2/2** → **148 → 150** |
| OpenAPI 夹具 | `organization-tenant-user-positions-v1` 静态 **2/2** |
| client-contracts | `tenant-user-positions` **1/1** |
| Vue API 单测 | `org-user-positions.test.ts` **2/2** |
| Layui 单测 | `org-user-positions.test.js` **1/1** |
| Mock parity | 「用户职位隶属列表、分配与取消」× 双端 **2/2** → `shell-parity` **50 → 52** |
| Real-stack E2E | `host-org-user-positions.spec.mjs` **2** 场景 × 双端 → **74 → 76** |
| 四处 canonical 门槛 | **349/7/38/150** |

## 非目标

- 不包含职级、职位-机构绑定、数据范围投影变更。
