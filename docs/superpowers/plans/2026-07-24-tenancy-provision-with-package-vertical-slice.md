# Tenancy 开通租户可选套餐纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。

- 建立日期：2026-07-24
- 状态：**Build-verified**

---

## Task 进度

- [x] `ProvisionTenantRequest` 扩展可选 `TenantPackageId`
- [x] `ProvisionTenant` Handler 校验活动套餐并写入 `TenantPackageId`
- [x] RED→GREEN 双库集成测试 `Provision_tenant_with_optional_package_returns_standard_contract`
- [x] Vue/Layui 开通表单可选套餐下拉
- [x] client-contracts / OpenAPI 契约同步
- [x] Integration 门槛 **132 → 134**
- 前置：[`2026-07-24-tenancy-tenant-package-assignment-vertical-slice.md`](2026-07-24-tenancy-tenant-package-assignment-vertical-slice.md)

**Goal:** Host 开通租户时可选择绑定活动套餐；禁用套餐拒绝开通；响应 `TenantSummary` 回填套餐字段。

**Architecture:** 复用 `POST /api/v1/tenancy/tenants` 与 `tenancy.tenants.write`；`TenantSql.Insert` 写入可空 `TenantPackageId`；无新迁移。

**明确未交付**：默认套餐策略、过期/配额、开通后套餐变更审计。

---

## 附录 A：API 契约

| 场景 | 方法 | 路径 | 权限 | 成功 | 失败 |
|---|---|---|---|---|---|
| 开通（可选套餐） | POST | `/api/v1/tenancy/tenants` | `tenancy.tenants.write` | 201 | 409 / 422 |

请求 `ProvisionTenantRequest` 扩展可选 `tenantPackageId`；`tenantPackageId` 省略或 null 表示不开通时绑定。

失败码：`tenancy.tenant_package.inactive`（422）、`tenancy.tenant_package.not_found`（404）。
