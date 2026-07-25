# Tenancy 租户-套餐绑定纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。

- 建立日期：2026-07-24
- 状态：**Build-verified**

---

## Task 进度

- [x] 019 双库迁移 + RED 集成测试
- [x] API + Host 租户列表 JOIN
- [x] Vue/Layui 租户页套餐列与分配
- [x] OpenAPI / 门槛同步（Integration **130 → 132**）
- [x] Mock parity E2E「租户列表内分配套餐」（**48 → 50**）
- [x] 真实栈 E2E「Host 管理员可为种子租户分配套餐」（**44 → 46**）
- [x] `TenantResolutionRecord` 修复 Dapper 7 列查询回归（`/tenancy/available` 500）
- 前置：[`2026-07-23-tenancy-tenant-package-vertical-slice.md`](2026-07-23-tenancy-tenant-package-vertical-slice.md)

**Goal:** Host 管理员为租户分配或解除套餐绑定；租户列表展示当前套餐；仅允许绑定活动套餐。

**Architecture:** `fn_tenancy_tenant.TenantPackageId` 可空 FK；`POST /api/v1/tenancy/tenants/{id}/package`；复用 `tenancy.tenants.write`。

---

## 附录 A：API 契约

| 场景 | 方法 | 路径 | 权限 | 成功 | 失败 |
|---|---|---|---|---|---|
| 分配/解除 | POST | `/api/v1/tenancy/tenants/{tenantId}/package` | `tenancy.tenants.write` | 200 | 404 / 409 / 422 |

请求 `AssignHostTenantPackageRequest`：`tenantPackageId`（null 表示解除）、`version`。

`TenantSummary` 扩展可选字段：`tenantPackageId`、`tenantPackageCode`、`tenantPackageName`（Host 目录查询填充）。
