# Tenancy 套餐禁用引用保护纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。

- 建立日期：2026-07-24
- 状态：**Build-verified**

---

## Task 进度

- [x] 稳定错误码 `tenancy.tenant_package.in_use`
- [x] `Disable` 前统计 `fn_tenancy_tenant.TenantPackageId` 引用
- [x] 扩展现有 `TenancyHostTenantPackageManagementAssertions`（绑定拒绝 + 解除后可禁用）
- [x] 中英文错误资源
- 前置：[`2026-07-24-tenancy-provision-with-package-vertical-slice.md`](2026-07-24-tenancy-provision-with-package-vertical-slice.md)

**Goal:** 仍有租户绑定套餐时禁止禁用；解除绑定后允许禁用。

**Architecture:** 复用 `POST /api/v1/tenancy/tenant-packages/{id}/disable`；无新迁移。

**明确未交付**：过期/配额、批量解绑、禁用套餐的审计事件。

---

## 附录 A：失败契约

| 场景 | 成功 | 失败 |
|---|---|---|
| 禁用未被引用的套餐 | 200 | 404 |
| 禁用仍被租户引用的套餐 | — | 422 `tenancy.tenant_package.in_use` |
