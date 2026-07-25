# Tenancy 套餐 Mock Parity E2E 纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。

- 建立日期：2026-07-24
- 状态：**Build-verified**

---

## Task 进度

- [x] `租户开通可选套餐在两端保持一致`（Vue/Layui）
- [x] `套餐仍被引用时禁用失败在两端保持一致`（展示 `assignedTenantCount` + `tenancy.tenant_package.in_use`）
- [x] 既有套餐 parity 夹具补全 `assignedTenantCount`
- [x] Mock parity E2E 门槛 **44 → 48**
- [x] 聚焦验证 **4/4** 通过（2026-07-24）
- 前置：开通可选套餐、禁用引用保护、绑定租户计数切片

**Goal:** 双管理端 Mock E2E 覆盖套餐开通绑定与禁用失败路径。

**明确未交付**：真实栈 E2E（租户列表分配套餐见 assignment 切片 Mock parity）。
