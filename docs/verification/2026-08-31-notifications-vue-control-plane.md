# Notifications Vue 管理控制面验证（2026-08-31）

- **任务：** P1 统一消息中心 Task 7（Vue 管理控制面与精确权限）
- **基线：** `4edd1718c3713a3da2f0f5236ac4cfb4e6a4582e`（`main`）
- **快照：** `notifications-vue-control-plane-20260831`
- **范围：** Vue 模板 / Profile / Binding / Delivery 管理页、模板草稿/已发布版本状态、偏好诚实占位、OpenAPI 生成 Operation 薄适配层、精确权限 DOM、空 Provider 目录、密钥不回显、FanOut 明示、Unknown 非成功色。不含真实外部 Provider、新迁移 106、生产 Adapter 项目、Layui、偏好 API。

## 证据

| 命令 | 结果 |
|---|---|
| `pnpm --filter @fullnet/admin test` | 接管复核 **168/168 文件，591/591** |
| `pnpm --filter @fullnet/admin typecheck` | **通过** |
| `pnpm --filter @fullnet/admin exec vite build` | **通过**（通知页按路由拆包） |
| `pnpm --filter @fullnet/admin-i18n test` | **8/8** |
| OpenAPI 规范化 `manifest.entries.length` / 快照 Operation | 接管复核 **280/280** |
| `pnpm test:bundle-budgets` | **PASS**（Vue initial JS gzip +2.34%） |
| `pnpm audit:clients` | **通过**（仅已审查 vite GHSA-fx2h-pf6j-xcff） |
| `pnpm test:inner -- --snapshot notifications-vue-control-plane-20260831` | **none**（本切片无 Integration 影响，合法） |
| `tests/e2e/admin-real-stack/scripts/spec-contracts.test.mjs` | **14/14**（新 spec 未直接点隐藏的「Full.NET Host」文本） |
| `NotificationTemplatesView.test.ts` 聚焦回归 | **4/4**：同时覆盖草稿和“已发布 vN”列表状态。 |
| `tests/e2e/admin-real-stack/tests/notification-platform.spec.mjs` | **SQL Server 1/1、MySQL 1/1 通过**：真实创建 inbox 模板、发布并确认列表显示“已发布 v1”。 |

`vue-client-contract-coverage` 模块计数为 **52**（含 `notification-platform.ts`）。接管复核已把 `workflow-definitions.ts` 的 5 个 Operation 纳入统一 manifest 与生成客户端，完整 `pnpm test:openapi` **122/122**，生成产物零漂移。Profile 发布按钮只接受独立 `notifications.provider_profiles.publish` 权限，编辑权限不再隐式获得发布入口。

## 行为门禁

- Adapter 只调用生成 Operation；未知 Provider 字段、密钥字段与未知 TypeKey 失败关闭。
- 生产目录为空时 Profile 页显示「尚未安装 Provider」，不渲染创建表单或虚假类型选项。
- Secret 只展示 `configured` / `not-configured`；引用输入为空且只读视图不出现 `vault://` 或 `apiToken`。
- 启用确认声明不会自动多发；FanOut 必须勾选确认后才能提交。
- Delivery Unknown 使用 `delivery-status--unknown`，不套用成功色；死信没有独立按钮，`failed` / `dead_lettered` / `unknown` 共用带理由的 retry。
- 偏好页即使拥有 update 也不提供编辑入口，不伪造尚未交付的 API。

## 结论

- Vue 通知控制面达到与当前切片相称的 **Build-verified**，其中模板创建/发布和平台管理页已补齐双库真实栈 E2E。整体仍不得升 `Verified`：生产目录没有真实 Provider，且本切片新路径没有 Linux Native AOT 原生进程证据。
- 邮件/短信/企微/公众号/钉钉仍为 **Planned**。容量继续 `Capacity-not-verified`。未改 Layui。未创建生产 Provider 项目。
- 本任务未触发规则或 Skill 演进。

## 后续演进

2026-09-02，通知偏好页的诚实占位已由当前用户邮箱端点登记/查询/删除切片替代；新入口仍只保存 `pending`，不会伪装邮件验证或真实投递。新证据见[收件端点管理验证](2026-09-02-notifications-recipient-endpoint-management.md)。本文件上方范围和证据继续保留 2026-08-31 的历史快照语义。
