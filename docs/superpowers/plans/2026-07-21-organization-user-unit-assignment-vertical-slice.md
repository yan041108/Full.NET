# Organization 用户-组织关系纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。机构单元切片已关闭；本切片交付租户上下文中 Host 用户与机构单元的隶属关系。

- 建立日期：2026-07-21
- 状态：**Verified**
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) P0：数据范围 / 用户-组织
  - [`2026-07-21-organization-unit-management-vertical-slice.md`](2026-07-21-organization-unit-management-vertical-slice.md) 非目标回收

**Goal:** 租户上下文中为 Host 用户分配机构单元（含主部门标记）；数据范围枚举与 SQL 投影留后续切片。

**Architecture:** 延续 `Full.NET.Modules.Organization`；表 `fn_organization_user_unit`；`SqlDataScope.TenantRequired`；权限 `organization.user_units.read` / `organization.user_units.write`（Tenant 作用域）。

---

## 附录 A：数据模型

表名：`fn_organization_user_unit`

| 列 | 说明 |
|---|---|
| Id | UUID v7 PK |
| TenantId | 租户隔离 |
| UserId | Host 用户 Id（`fn_identity_user`，应用层校验） |
| UnitId | FK `fn_organization_unit` |
| IsPrimary | 租户内该用户的主部门 |
| IsActive | 禁用后保留历史 |
| CreatedAtUtc / UpdatedAtUtc / Version | 审计与乐观锁 |

唯一约束：`UX_fn_organization_user_unit_Tenant_User_Unit` on `(TenantId, UserId, UnitId)`。

---

## 附录 B：验收表

| 场景 | 方法 | 路径 | 权限 | 成功 | 失败 |
|---|---|---|---|---|---|
| 列表 | GET | `/api/v1/organization/user-units` | read | 200 | 403 |
| 分配 | POST | `/api/v1/organization/user-units` | write | 201 | 409 已存在 |
| 设主部门 | PUT | `/api/v1/organization/user-units/{id}` | write | 200 | 404 |
| 取消 | POST | `/api/v1/organization/user-units/{id}/disable` | write | 200 | 404 |

---

### Task 1–5

（按机构单元切片模式推进。）
