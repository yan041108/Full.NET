# Identity 角色管理纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。行为变更必须先失败测试再实现。用户管理切片已关闭；本切片为 P0 下一刀，禁止与菜单/组织并行三刀。

- 建立日期：2026-07-21
- 状态：**Build-verified**（Task 1–5 完成）
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) P0 优先队列
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「角色与数据授权」Core / M2
  - [`2026-07-21-identity-user-management-vertical-slice.md`](2026-07-21-identity-user-management-vertical-slice.md) 退出后的下一刀
- 关联 Skill：`fullnet-module-delivery`

**Goal:** 交付 Host 作用域角色列表/详情/创建/更新/权限替换/禁用（不含菜单 CRUD、组织、数据范围），复用用户切片交付模式。

**Architecture:** Identity 模块内 CQRS + Dapper；`identity.roles.read` / `identity.roles.write`；系统角色与超级管理员角色受 `identity.roles.system_locked` 保护；Vue/Layui 同场景同步（Task 4）。

**Tech Stack:** .NET 10、Dapper、DbUp、SQL Server/MySQL、ProblemDetails、Vue 3 + Layui、Playwright。

---

## 范围与非目标

### 本切片必须交付

1. Host 角色分页列表、详情（含权限码）、创建、更新名称、替换权限集合、禁用。
2. 稳定权限码 `identity.roles.read` / `identity.roles.write`。
3. 复用既有 `fn_identity_role` / `fn_identity_role_permission`；无新迁移（列已满足）。
4. 系统角色（`IsSystem = 1`）禁止更新/禁用/改权限。
5. 权限替换仅允许授权目录中 Host 作用域且非 `identity.super_administrators.*` 的码。
6. Task 4–5：双管理端页面、Mock/真实栈 E2E、OpenAPI 夹具。

### 明确非目标

- 菜单/按钮 CRUD、数据范围、组织/职位。
- 租户级角色、用户-角色分配 UI（后续切片）。
- 删除角色（物理删除）；本切片仅禁用。

---

## 附录 A：验收表（Task 1 冻结）

| 场景 | 方法 | 路径 | 权限 | 成功 | 失败 |
|---|---|---|---|---|---|
| 分页列表 | GET | `/api/v1/identity/roles` | `identity.roles.read` | 200 + `PagedResult` | 403 |
| 详情 | GET | `/api/v1/identity/roles/{id}` | read | 200 + 权限码数组 | 404 `identity.roles.not_found` |
| 创建 | POST | `/api/v1/identity/roles` | write | 201 | 409 `identity.roles.code_exists` |
| 更新名称 | PUT | `/api/v1/identity/roles/{id}` | write | 200 | 409 版本冲突；系统角色 `identity.roles.system_locked` |
| 替换权限 | PUT | `/api/v1/identity/roles/{id}/permissions` | write | 200 | 系统角色锁定；未知权限校验失败 |
| 禁用 | POST | `/api/v1/identity/roles/{id}/disable` | write | 200 | 系统角色锁定 |

---

### Task 1: 规格冻结与失败验收夹具

1. [x] 验收表（附录 A）。
2. [x] RED 集成测试：列表 403、创建重复 Code、系统角色更新拒绝。
3. [x] 不扩展菜单/组织表结构。

### Task 2: 双库持久化与领域命令

1. [x] 复用既有表；新增只读/写入 SQL Statement。
2. [x] Command/Query + 事务；冲突与系统角色不变量。
3. [ ] 双库 Integration 全绿（本地无 Testcontainers 时依赖 CI）。

### Task 3: HTTP 端点与授权目录

1. [x] 标准 HTTP + ProblemDetails；权限码注册与导航项（roles）。
2. [ ] OpenAPI/契约夹具（Task 5）。

### Task 4: Vue / Layui 双端页面

1. [x] 列表、表单、权限多选、禁用确认；导航白名单。

### Task 5: 真实栈冒烟与文档

1. [x] real-stack 冒烟；OpenAPI 夹具；验证记录与门槛同步。
