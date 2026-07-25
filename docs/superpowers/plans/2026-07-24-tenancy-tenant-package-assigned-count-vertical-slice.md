# Tenancy 套餐绑定租户计数纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。

- 建立日期：2026-07-24
- 状态：**Build-verified**

---

## Task 进度

- [x] `TenantPackageSummary.assignedTenantCount` 列表/详情聚合
- [x] 双库列表与详情 SQL 子查询
- [x] Integration 断言（开通后计数为 1）
- [x] client-contracts / OpenAPI / Vue / Layui / i18n
- 前置：[`2026-07-24-tenancy-tenant-package-disable-in-use-vertical-slice.md`](2026-07-24-tenancy-tenant-package-disable-in-use-vertical-slice.md)

**Goal:** Host 套餐目录展示当前绑定租户数量，辅助禁用前判断。

**明确未交付**：仅活动租户计数、历史绑定审计。
