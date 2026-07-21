# Identity 用户-角色分配纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。角色数据范围切片已关闭；本切片交付 Host 用户可分配角色读写与双端 UI。

- 建立日期：2026-07-21
- 状态：**Verified（本地静态门禁）**
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) P0：用户-角色分配 UI
  - [`2026-07-21-identity-role-data-scope-vertical-slice.md`](2026-07-21-identity-role-data-scope-vertical-slice.md)

**Goal:** Host 用户可查看与替换可分配角色集合；系统角色与超级管理员角色不可经此 API 分配，但超级管理员既有绑定在 PUT 时保留。

**Architecture:** 复用 `fn_identity_user_role`；`GET/PUT /api/v1/identity/users/{userId}/roles`；乐观并发基于用户 `Version`；成功后轮换安全戳并撤销会话。

---

## 附录 A：可分配角色规则

| 条件 | 可经本 API 分配 |
|---|---|
| `IsActive = 1` | 是 |
| `IsSystem = 0` | 是 |
| `IsSuperAdministrator = 0` | 是 |
| 系统/超级管理员角色 | 否 → `identity.user_roles.role_not_assignable` |

## 附录 B：验收表

| 场景 | 方法 | 路径 | 成功 | 失败 |
|---|---|---|---|---|
| 读取 | GET | `/api/v1/identity/users/{userId}/roles` | 200 | 404 |
| 替换 | PUT | `/api/v1/identity/users/{userId}/roles` | 200 | 未知角色；不可分配角色；版本冲突 |

## 非目标

- 运行时多角色数据范围并集
- 超级管理员角色经本 UI 授予/撤销
- 新数据库迁移（复用既有用户-角色表）
