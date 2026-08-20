# RBAC 超级管理员权限拆分与三级授权收口设计

- 日期：2026-08-20
- 状态：Approved for implementation
- 基线：`main@e008f64e`（任务快照 `rbac-three-level-permissions-20260820`）
- 相关：[`2026-08-02-vue-action-authorization-design.md`](2026-08-02-vue-action-authorization-design.md)、[`vue-action-authorization-w4-w5-closeout-2026-08-03.md`](../../verification/vue-action-authorization-w4-w5-closeout-2026-08-03.md)

## 1. 问题

1. `identity.super_administrators.manage` 同时绑定 grant 与 revoke Endpoint，无法最小授权，且是 `LegacyCoarseActionPermissionRegistry` 最后一对粗粒度豁免。
2. Module → Page → Action 授权树与 `RolesView` 已交付；本切片以超管拆分为主，并复查三级树不变量（勾选操作必带页面、取消页面清后代）。

## 2. 决策

| 项 | 决策 |
| --- | --- |
| 新权限码 | `identity.super_administrators.grant`、`identity.super_administrators.revoke` |
| 保留 | `identity.super_administrators.read` |
| 退役 | `identity.super_administrators.manage`（目录、Endpoint、Vue、角色权限迁移后删除） |
| 三级树 | 保持现有 `AuthorizationTreeProjector`；超管权限仍不进入可分配树（与既有排除一致），仅作为 Host 精确动作 |
| Layui | 禁止修改 |

## 3. 行为

- Endpoint：`POST .../grant` → `.grant`；`POST .../{id}/revoke` → `.revoke`。
- Vue：授予/撤销按钮分别 `PermissionGate`；无权限不创建 DOM。
- 直接 API 无权限 → `403 authorization.permission_denied`。
- 迁移：SQL Server/MySQL 成对，将存量 `.manage` 角色权限展开为 grant+revoke，然后删除 `.manage`；可恢复。
- Architecture：清空 `AllowedBindings` 中对应两条 manage 豁免；`.manage` 加入 Retired 集合。

## 4. 验收

Unit / Architecture / OpenAPI / 双库 Integration / Vue Vitest；可选 real-stack 超管页按钮门控。
