# Tenancy 租户分配套餐真实栈 E2E 纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。

- 建立日期：2026-07-24
- 状态：**Build-verified**（脚本已交付；本地/CI 实跑待容器环境）

---

## Task 进度

- [x] `real-stack-auth.mjs` 辅助：`createTenantPackageViaApi`、`findSeedTenantViaApi`
- [x] `host-tenants.spec.mjs`「Host 管理员可为种子租户分配套餐」
- [x] 真实栈门槛 **44 → 46**（23 场景 × 双端）
- 前置：[`2026-07-24-tenancy-tenant-package-assignment-vertical-slice.md`](2026-07-24-tenancy-tenant-package-assignment-vertical-slice.md)

**Goal:** 真实 API + 真库下验证 Host 租户页套餐下拉分配闭环。

**Architecture:** 测试经 API 创建一次性套餐，在 UI 为种子 `local` 租户分配，断言列表展示 `套餐: {name}`；禁止 `page.route` mock。

**明确未交付**：解除绑定真实栈、禁用引用保护真实栈、过期/配额。
