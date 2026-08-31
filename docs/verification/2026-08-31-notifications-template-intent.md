# Notifications Template / Intent 验证（2026-08-31）

- **任务：** P1 统一消息中心 Task 4（Template、Intent 与内建 Inbox 纵向闭环）
- **基线：** `4edd1718c3713a3da2f0f5236ac4cfb4e6a4582e`（`main`）
- **快照：** `notifications-template-intent-20260831`
- **范围：** Host/Tenant 模板 Draft/Publish、不可变版本、参数 Schema 校验、Intent 幂等与多 Recipient 扇出内建 Inbox。仅 `inbox` 渠道；`BindingVersionId` 为空、`DispatchModeKey='single'`、`RouteSnapshotJson='[]'`。不含多 Profile/Binding 控制面、Delivery Worker、真实 Provider、新 Vue 管理页。未新建迁移 106（沿用 104 表）。

## 证据

| 命令 | 结果 |
|---|---|
| Notifications Unit（含模板编译器 5 项） | **38/38** |
| Architecture `FullyQualifiedName~Notifications`（含 104 边界、AOT 参数/物化器） | **5/5**（门槛写成 8 会因过滤器命中不足而误报） |
| `pnpm test:naming` | **30/30** |
| OpenAPI 夹具 `模板与意图...一致` | **1/1** |
| `pnpm test:inner -- --snapshot notifications-template-intent-20260831` | 本阶段执行 **none**（inner 只跑 immediate + `migration-*`；本切片无新迁移，合法） |
| `pnpm test:slice -- --snapshot notifications-template-intent-20260831` | 首轮 **2/2 失败**（租户 `INSERT...SELECT` 未在 WHERE 使用 `TenantId = @TenantId`，`SqlScopeGuard` 拒绝）；补谓词后 **2/2 通过**（SQL Server + MySQL Notifications API） |

slice 覆盖：无权限 403、非 inbox 渠道关闭、Draft 更新冲突、未发布 Intent **422** `template_not_published`、Publish Hash/版本冻结、同幂等键回放 200、不同载荷 409、未知/缺失/超限参数 400 且错误体不回显参数值、Host 双 Recipient 扇出 Inbox、租户创建模板 201 且 Host GET 该 id **404**。

未重跑完整 `pnpm test:openapi`：此前完整套件中 2 个失败来自工作区 P0 Workflow 脏文件，本任务不修。未跑完整 `pnpm test:sql-safety`（无新迁移；历史 009/011/051/093 豁免行号偏差仍在）。

## 结论

- Template / Intent（仅内建 Inbox）达到与当前切片相称的 **Build-verified**。Host 公告/Inbox 与 Tenant Inbox 既有范围不降级，也不升 `Verified`。
- 没有重要跨模块事实时不为 Intent 本身写 Outbox；Inbox 与手工发信同类：同一事务写 `InboxMessageReceived`，提交后 best-effort SignalR。
- 生产 Provider 目录保持为空。容量继续 `Capacity-not-verified`。
- 本机 Windows 不把 Linux Native AOT publish 标为通过。未改 Layui。未新增独立 Vue 页（`notifications.intents.create` 挂在既有模板导航下）。
- 本任务未触发规则或 Skill 演进。Tenant INSERT 守卫失败属于既有 `SqlScopeGuard` 已覆盖的单次实现疏忽。
