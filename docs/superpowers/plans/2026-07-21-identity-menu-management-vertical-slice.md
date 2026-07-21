# Identity 菜单管理纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。本切片首次引入 **持久化导航表**，须先通过迁移与 `GetNavigation` 合并设计再实现 CRUD。

- 建立日期：2026-07-21
- 状态：**Build-verified**
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) P0：角色切片已关闭，下一刀菜单（禁止与组织并行）
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「菜单与按钮权限管理」
  - [`2026-07-17-tenant-context-permission-navigation-design.md`](../specs/2026-07-17-tenant-context-permission-navigation-design.md)
- 关联规格：租户/权限导航设计 §12 后续项

**Goal:** Host 作用域可配置导航节点 CRUD，运行时与代码目录（`IAuthorizationCatalogContributor`）合并投影；权限码仍只来自代码目录。

**Architecture:** 新表 `fn_identity_navigation`；系统内置项保持 Contributor；`NavigationProjector` 合并后按权限裁剪；`componentKey`/`path` 须落在客户端白名单；`RequiredPermission` 须为 Catalog 已发布码。

**Tech Stack:** DbUp 012 双库迁移、Dapper、ProblemDetails、Vue/Layui 同步、Playwright。

---

## 范围与非目标

### 本切片必须交付

1. `fn_identity_navigation` 双库迁移（Host、`TenantId IS NULL`）。
2. Host 菜单分页列表、详情、创建、更新、排序/父级调整、禁用。
3. `identity.menus.read` / `identity.menus.write`；管理页本身注册为 Contributor 导航项 `menus`。
4. `GET /api/v1/navigation` 合并代码目录 + DB 自定义项（`IsSystem=0`）。
5. 双端管理 UI、Mock/真实栈冒烟、OpenAPI 夹具。

### 明确非目标

- 按钮权限 CRUD、租户菜单覆盖、组织/数据范围。
- 在 DB 中发明新 permission code（只能引用 Catalog 已有码）。
- 任意 `componentKey`（须匹配 `navigation-catalog` 白名单或专用占位策略）。
- 翻译表（首版 Title/Caption 存 DB 中文；`titleKey` 后续切片）。

---

## 附录 A：数据模型（Task 1 冻结）

表名：`fn_identity_navigation`

| 列 | 类型 | 说明 |
|---|---|---|
| Id | UUID v7 PK | 应用生成 |
| TenantId | NULL | Host 切片固定 NULL |
| ScopeKey | `host` | HostOnly |
| ParentId | NULL FK self | 树形 |
| RouteName | varchar(64) | 稳定路由名，作用域内唯一 |
| Path | varchar(256) | 须匹配客户端白名单路径规则 |
| ComponentKey | varchar(64) | 客户端白名单键 |
| Title / Caption | nvarchar | 投影到 `NavigationNodeResponse` |
| Icon | varchar(64) | 图标语义键 |
| DisplayOrder | int | 同级排序 |
| RequiredPermission | varchar(160) | Catalog 已有权限码 |
| IsSystem | bit | 自定义项为 0 |
| IsActive | bit | 禁用后不再投影 |
| CreatedAtUtc / UpdatedAtUtc / Version | | 审计与乐观锁 |

唯一约束：`UX_fn_identity_navigation_Scope_RouteName` on `(ScopeKey, RouteName)`（Host 切片 `TenantId IS NULL`）。

---

## 附录 B：验收表（草案）

| 场景 | 方法 | 路径 | 权限 | 成功 | 失败 |
|---|---|---|---|---|---|
| 列表 | GET | `/api/v1/identity/menus` | `identity.menus.read` | 200 | 403 |
| 详情 | GET | `/api/v1/identity/menus/{id}` | read | 200 | 404 |
| 创建 | POST | `/api/v1/identity/menus` | write | 201 | 409 `identity.menus.route_name_exists` |
| 更新 | PUT | `/api/v1/identity/menus/{id}` | write | 200 | 版本冲突；未知权限校验失败 |
| 禁用 | POST | `/api/v1/identity/menus/{id}/disable` | write | 200 | 404 |
| 导航合并 | GET | `/api/v1/navigation` | `identity.navigation.read` | 含 DB 自定义项 | 无权限项裁剪 |

---

### Task 1: 规格冻结、迁移与失败验收夹具

1. [x] 本计划附录 A/B。
2. [x] 双库迁移 `012_IdentityNavigation.sql`。
3. [x] RED 集成测试：列表未授权 403（端点未实现时失败）。
4. [x] Integration 门槛维持 **91**（+2 SQL Server/MySQL，Task 1 已计入）。

### Task 2–5

- [x] Task 2–3：ManageHostMenus API、导航合并、集成验收
- [x] Task 4：Vue `MenusView` + Layui `menus.js` + parity E2E
- [x] Task 5：OpenAPI 夹具 + 真实栈 `host-menus.spec.mjs` + 验证记录
