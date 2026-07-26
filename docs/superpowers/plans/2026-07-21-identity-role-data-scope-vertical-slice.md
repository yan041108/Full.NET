# Identity 角色数据范围纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。用户-机构隶属切片已关闭；本切片交付 Host 角色数据范围配置与 SQL 投影基础。

- 建立日期：2026-07-21
- 状态：**Verified（本地静态门禁）**
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) P0：Identity 数据范围
  - [`2026-07-17-fullnet-architecture-design.md`](../specs/2026-07-17-fullnet-architecture-design.md) §10.2

**Goal:** Host 角色可配置数据范围种类；`custom` 时由 Host 管理请求显式指定目标租户并绑定该租户机构单元；提供参数化 SQL 投影基础供后续查询过滤。

**Architecture:** `fn_identity_role.DataScopeKind` + `fn_identity_role_data_scope_unit`；`identity.data_scope.*` 稳定机器码；`GET/PUT /api/v1/identity/roles/{id}/data-scope`；`RoleDataScopeProjection` 单元测试。

---

## 附录 A：数据范围种类

| 机器码 | 含义 |
|---|---|
| `identity.data_scope.all` | 全部（默认） |
| `identity.data_scope.org` | 当前主部门 |
| `identity.data_scope.org_subtree` | 主部门及下级 |
| `identity.data_scope.self` | 本人 |
| `identity.data_scope.custom` | 自定义机构集合（Host 管理请求显式指定目标租户） |

## 附录 B：验收表

| 场景 | 方法 | 路径 | 成功 | 失败 |
|---|---|---|---|---|
| 读取 | GET | `/api/v1/identity/roles/{id}/data-scope` | 200 | 404 |
| 更新 | PUT | `/api/v1/identity/roles/{id}/data-scope` | 200 | 系统角色锁定；custom 无单元；未知 kind |

## 非目标

- 用户-角色分配 UI、运行时多角色并集解析、业务模块全面接入过滤。
