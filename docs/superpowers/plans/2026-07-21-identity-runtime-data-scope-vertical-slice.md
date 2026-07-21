# Identity 运行时数据范围并集纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。用户-角色分配切片已关闭；本切片交付多角色数据范围并集解析与首个查询过滤接入点。

- 建立日期：2026-07-21
- 状态：**Verified（本地静态门禁）**
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) P0：运行时数据范围解析
  - [`2026-07-17-fullnet-architecture-design.md`](../specs/2026-07-17-fullnet-architecture-design.md) §10.2

**Goal:** 从用户活动 Host 角色解析有效数据范围；多角色取 SQL `OR` 并集；超级管理员或任一 `all` 范围不受限；在租户机构列表/详情只读查询落地首个过滤接入点。

**Architecture:** `IUserDataScopeResolver` + `IDataScopeSqlFilterBuilder`（Contracts）；`RoleDataScopeProjection.BuildUnionOrganizationUnitFilter`；Organization `TenantUnitQueryService` 追加参数化 WHERE。

---

## 附录 A：并集规则

| 条件 | 有效范围 |
|---|---|
| 超级管理员 Claim 或活动超级管理员角色 | 不受限 |
| 任一活动角色 `identity.data_scope.all` | 不受限 |
| 多角色其余种类 | 各角色 SQL 片段 `OR` 合并 |
| 无活动角色 | `1 = 0`（不可见） |

## 附录 B：首个接入点

| 查询 | 过滤 |
|---|---|
| `GET /api/v1/organization/units` | 列表 + 计数 |
| `GET /api/v1/organization/units/{id}` | 详情（范围外 404） |

写操作与内部 `FindByIdAsync` 不受数据范围过滤（管理边界仍靠权限）。

## 非目标

- 业务模块全面接入机构过滤
- 用户-机构隶属列表过滤
- 新 HTTP 只读诊断端点
