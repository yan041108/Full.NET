# Organization 职位管理纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。在既有 `Full.NET.Modules.Organization` 内新增垂直切片。

- 建立日期：2026-07-25
- 状态：**Build-verified**
- 批准依据：[`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「职位管理」；机构/用户-机构隶属已闭环。
- 验证记录：[`organization-position-2026-07-25.md`](../../verification/organization-position-2026-07-25.md)

**Goal:** 租户上下文中职位目录 CRUD（列表、详情、创建、更新、禁用）；Vue/Layui 同步。

**Architecture:** 表 `fn_organization_position`；权限 `organization.positions.read` / `write`；API `/api/v1/organization/positions`；导航 `org-positions` → `/organization/positions`。

---

## 非目标

- 职级、用户-职位、职位-机构绑定、数据范围投影变更。
- Realtime / 通知 / Files。
- `Verified`。

---

## 任务

1. [x] 025 迁移、权限、RED（Integration **146 → 148**）
2. [x] ManageTenantPositions + OpenAPI + Integration 双库绿
3. [x] 双端 UI + E2E（shell-parity **48 → 50**；real-stack **70 → 74**）
4. [x] 文档与门槛 **349/7/38/148**
