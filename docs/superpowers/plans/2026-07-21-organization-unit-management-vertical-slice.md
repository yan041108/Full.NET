# Organization 机构管理纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。本切片首次引入 **Organization 模块**与租户作用域机构树。

- 建立日期：2026-07-21
- 状态：**Build-verified**
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) P0：菜单切片已关闭，下一刀组织/数据范围
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「机构管理」
  - [`2026-07-17-fullnet-architecture-design.md`](../specs/2026-07-17-fullnet-architecture-design.md) §6.2、§10.2

**Goal:** 租户上下文中机构（组织单元）树 CRUD；Host 管理员须先切换租户上下文；数据范围规则留后续切片。

**Architecture:** 新模块 `Full.NET.Modules.Organization`；表 `fn_organization_unit`；`SqlDataScope.TenantRequired`；权限 `organization.units.read` / `organization.units.write`（Tenant 作用域）。

**Tech Stack:** DbUp 013 双库迁移、Dapper、ProblemDetails、Vue/Layui 同步、Playwright。

---

## 范围与非目标

### 本切片必须交付

1. `fn_organization_unit` 双库迁移（`TenantId NOT NULL`）。
2. 租户机构分页列表、详情、创建、更新、父级调整、禁用。
3. `organization.units.read` / `organization.units.write`；Contributor 导航项 `org-units`。
4. 双端管理 UI、Mock/真实栈冒烟、OpenAPI 夹具。

### 明确非目标

- 职位/职级、用户-组织关系、数据范围枚举与 SQL 投影。
- Host 作用域机构（无 `TenantId` 的平台组织树）。
- 按钮权限、租户套餐联动。

---

## 附录 A：数据模型（Task 1 冻结）

表名：`fn_organization_unit`

| 列 | 类型 | 说明 |
|---|---|---|
| Id | UUID v7 PK | 应用生成 |
| TenantId | UUID NOT NULL | 租户隔离 |
| ParentId | NULL FK self | 树形 |
| Code | varchar(64) | 租户内唯一 |
| Name | nvarchar(128) | 显示名称 |
| DisplayOrder | int | 同级排序 |
| IsActive | bit | 禁用后保留历史 |
| CreatedAtUtc / UpdatedAtUtc / Version | | 审计与乐观锁 |

唯一约束：`UX_fn_organization_unit_Tenant_Code` on `(TenantId, Code)`。

---

## 附录 B：验收表（草案）

| 场景 | 方法 | 路径 | 权限 | 成功 | 失败 |
|---|---|---|---|---|---|
| 列表 | GET | `/api/v1/organization/units` | `organization.units.read` | 200 | 403 |
| 详情 | GET | `/api/v1/organization/units/{id}` | read | 200 | 404 |
| 创建 | POST | `/api/v1/organization/units` | write | 201 | 409 `organization.units.code_exists` |
| 更新 | PUT | `/api/v1/organization/units/{id}` | write | 200 | 版本冲突 |
| 禁用 | POST | `/api/v1/organization/units/{id}/disable` | write | 200 | 404 |
| 租户上下文 | GET | 列表（Host 令牌） | — | 403/422 租户上下文缺失 | — |

---

### Task 1: 规格冻结、迁移与失败验收夹具

1. [x] 本计划附录 A/B。
2. [x] 双库迁移 `013_OrganizationUnit.sql`。
3. [x] RED 集成测试：租户上下文中列表未授权 403。
4. [x] Integration 门槛上调至 **93**（+2 SQL Server/MySQL）。

### Task 2–5

- [x] API、权限、导航 Contributor
- [x] Vue/Layui 双端 UI
- [x] OpenAPI 夹具、Mock/真实栈 E2E、验证记录
