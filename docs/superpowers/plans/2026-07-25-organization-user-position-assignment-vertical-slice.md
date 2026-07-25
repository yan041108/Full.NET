# Organization 用户-职位关系纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。职位目录切片已关闭；本切片交付租户上下文中 Host 用户与职位的隶属关系。

- 建立日期：2026-07-25
- 状态：**Build-verified**
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) Organization 职位管理非目标回收
  - [`2026-07-25-organization-position-vertical-slice.md`](2026-07-25-organization-position-vertical-slice.md)

**Goal:** 租户上下文中为 Host 用户分配职位（含主职位标记）；职级、职位-机构绑定与数据范围投影留后续切片。

**Architecture:** 延续 `Full.NET.Modules.Organization`；表 `fn_organization_user_position`；`SqlDataScope.TenantRequired`；权限 `organization.user_positions.read` / `organization.user_positions.write`（Tenant 作用域）；列表为目录级查询，不按机构数据范围裁剪。

---

## 附录 A：数据模型

表名：`fn_organization_user_position`

| 列 | 说明 |
|---|---|
| Id | UUID v7 PK |
| TenantId | 租户隔离 |
| UserId | Host 用户 Id（`fn_identity_user`，应用层校验） |
| PositionId | FK `fn_organization_position` |
| IsPrimary | 租户内该用户的主职位 |
| IsActive | 禁用后保留历史 |
| CreatedAtUtc / UpdatedAtUtc / Version | 审计与乐观锁 |

唯一约束：`UX_fn_organization_user_position_Tenant_User_Position` on `(TenantId, UserId, PositionId)`。

---

## 附录 B：验收表

| 场景 | 方法 | 路径 | 权限 | 成功 | 失败 |
|---|---|---|---|---|---|
| 列表 | GET | `/api/v1/organization/user-positions` | read | 200 | 403 |
| 分配 | POST | `/api/v1/organization/user-positions` | write | 201 | 409 已存在 |
| 设主职位 | PUT | `/api/v1/organization/user-positions/{id}` | write | 200 | 404 |
| 取消 | POST | `/api/v1/organization/user-positions/{id}/disable` | write | 200 | 404 |

---

## 非目标

- 职级、职位-机构绑定、数据范围投影变更、用户档案展示集成。
