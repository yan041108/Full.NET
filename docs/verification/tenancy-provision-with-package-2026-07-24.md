# Tenancy 开通租户可选套餐验证记录

- 日期：2026-07-24
- 状态：**Build-verified**
- 计划：[`2026-07-24-tenancy-provision-with-package-vertical-slice.md`](../superpowers/plans/2026-07-24-tenancy-provision-with-package-vertical-slice.md)

## 交付范围

`ProvisionTenantRequest` 可选 `TenantPackageId`；开通时校验活动套餐并写入 `fn_tenancy_tenant.TenantPackageId`；`TenantSummary` 响应回填套餐字段；Vue/Layui 开通表单可选套餐。

**明确未交付**：默认套餐策略、过期/配额、开通审计。

## 验证矩阵

| 门禁 | 结果 |
| --- | --- |
| Integration `Provision_tenant_with_optional_package_returns_standard_contract` | **2/2**（SQL Server + MySQL） |
| `pnpm test:clients` | client-contracts `CreateHostTenantRequest.tenantPackageId` |
| `pnpm test:openapi` | Host 租户 `ProvisionTenantRequest` 扩展 |
| Release 构建 | 0 警告 / 0 错误 |
| Integration 聚焦（2026-07-24） | **2/2** 通过，墙钟 ~1m 30s |

Integration 门槛 **132 → 134**。
