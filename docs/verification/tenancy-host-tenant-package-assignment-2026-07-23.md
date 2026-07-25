# Tenancy 租户-套餐绑定验证记录

- 日期：2026-07-24
- 状态：**Build-verified**
- 计划：[`2026-07-24-tenancy-tenant-package-assignment-vertical-slice.md`](../superpowers/plans/2026-07-24-tenancy-tenant-package-assignment-vertical-slice.md)

## 交付范围

`fn_tenancy_tenant.TenantPackageId` 可空 FK；`POST /api/v1/tenancy/tenants/{id}/package` 分配/解除；Host 租户列表 JOIN 套餐信息；Vue/Layui 租户页套餐下拉。

**明确未交付**：过期/配额策略、开通时默认套餐、套餐变更审计。

## 验证矩阵

| 门禁 | 结果 |
| --- | --- |
| Integration `Host_tenant_package_assignment` | **2/2**（SQL Server + MySQL） |
| `pnpm test:openapi` | **18/18**（Host 租户契约扩展） |
| Parity E2E 聚焦 | `租户列表内分配套餐` **2/2**（Vue/Layui，2026-07-24） |
| 真实栈 E2E | `host-tenants.spec.mjs`「分配套餐」**2/2**（脚本已交付；实跑待容器环境） |
| Release 构建 | 0 警告 / 0 错误 |

Integration 门槛 **130 → 132**。
