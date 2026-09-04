# Workflow 首切片收口验证（2026-08-31）

## 范围与结论

- 基线提交：`4edd1718c3713a3da2f0f5236ac4cfb4e6a4582e`
- 任务快照：`workflow-first-slice-closeout-20260831`
- 验证范围：在既有 Host Vue 审批、线性 `human.approval`、表单运行时与 Native AOT 证据之上，补齐 Host/Tenant × SQL Server/MySQL 的权限、并发与表单安全矩阵；关闭 Task 7 中 Tenant/422 与受影响 slice 遗留项。
- 结论：Workflow 首切片 Task 7 的 Host/Tenant 真实栈矩阵与 slice 受影响集已关闭，能力状态保持 `Build-verified`。
- 后续进展：`notify.cc` 首个可执行纵向切片已由 [`2026-09-04-workflow-notify-cc-verification.md`](2026-09-04-workflow-notify-cc-verification.md) 关闭。
- 非结论：不得升格为 `Verified`。`gateway.exclusive` 首个闭合切片已由[独立验证记录](2026-09-05-workflow-exclusive-gateway-verification.md)接续；Worker 恢复/重放/reconcile、Tenant 本地收件人候选、生产容量与人工产品验收仍开放。本机验证使用开发真实栈容器，保持 `Capacity-not-verified`。

本记录关闭 [`2026-08-30-workflow-admin-approval-real-stack.md`](2026-08-30-workflow-admin-approval-real-stack.md) 中明确留下的 Tenant 作用域与危险 Patch 422 缺口；不替代该记录中的 Host Vue 审批证据，也不替代 Native AOT 记录。

## 矩阵覆盖

| 场景 | Host | Tenant |
| --- | --- | --- |
| Vue 同意、驳回 | 真实栈页面 | 真实栈页面（同一会话完成，避免二次进入租户） |
| Vue 旧 Revision 409 后刷新权威待办并关闭过期动作 | 真实栈页面 | 真实栈页面 |
| 危险 Patch（只读 `reason`、隐藏 `secret`、未知字段、类型错误、缺必填）返回 422 且不推进待办 | Vue + API；Integration 矩阵 | Tenant API 真实栈 + Integration 矩阵 |
| 仅 `workflow.todos.read` 时动作按钮不进入 DOM | Vue 真实栈（`WorkflowTodosView`） | 由 Host 同一视图覆盖；Tenant 不重复不稳定的受限用户进租户页面 |
| 仅读取权限时直接调用 API 返回 403 | Vue 会话内 `page.request` | 隔离 `playwright.request` 上下文 + Integration |
| 禁止引用 Host 定义、Host 读不到 Tenant 实例 | Integration | Integration |

Tenant Vue「无权限按钮不进 DOM」未做成独立页面用例的原因：受限用户走 Vue「进入租户」会掉回登录页（`identity.session_not_active` 与 SignalR 401）。根因包括同一用户先 API 登录再页面登录被单会话策略踢掉、PUT 后在途 401 Refresh 与刚轮换的刷新 Cookie 重叠。产品侧已修复 Refresh：若内存 Token 已在租户切换后换成新值，Refresh 失败不得 `clearLocal()`。Tenant 403 仍以 API 矩阵验收；不得再改 `enterDevelopmentTenant` 去迁就错误会话。

## 产品修复

`packages/client-contracts/src/identity-session.ts` 的 `refreshAccessToken`：刷新开始时记下 `tokenBeforeRefresh`；Refresh 失败或非法时，若内存 Token 已被租户切换换成新值，不得清空本地会话。配套 RED/GREEN：`ui/admin/src/auth/session.test.ts`「租户切换成功后，过期请求触发的刷新失败不得清空新令牌」。

## 新鲜验证结果

| 验证 | 结果 |
| --- | --- |
| `pnpm --filter @fullnet/admin test -- src/auth/session.test.ts` | 17/17 通过 |
| `pnpm --filter @fullnet/admin-real-stack-e2e test -- scripts/spec-contracts.test.mjs` 对应契约 | 14/14 通过；Host/Tenant 规格必须含 403/409/422、`workflow-todo-approve/reject`、`enterDevelopmentTenant`、`loginTenantAdminAccessToken` |
| SQL Server Host+Tenant admin-real-stack（`workflow-approval.spec.mjs` + `workflow-approval-tenant.spec.mjs`） | **7/7 通过**，约 1.3 分钟；103 个迁移脚本成功，API/Worker 构建 0 警告、0 错误 |
| MySQL 同一套件（`FULLNET_E2E_DATABASE_PROVIDER=MySql`） | **7/7 通过**，约 2.0 分钟；103 个迁移脚本成功，API/Worker 构建 0 警告、0 错误 |
| `pnpm test:slice -- --snapshot workflow-first-slice-closeout-20260831` | 工具链 53/53、治理 52/52；Integration 矩阵 api-sqlserver=66、api-mysql=66、合计 665；Workflow 聚焦 **6/6 通过**（约 1 分 24 秒），双 Provider 均确认 |
| 升档 `Verified` | **否** |
| 容量认证 | **否**，保持 `Capacity-not-verified` |

Integration 新增 `Tenant_scope_approval_matrix_holds_with_*`：独立 `hostClient` / `tenantClient` / `limitedClient`（同一 HttpClient 的 Cookie 会覆盖 Bearer），覆盖跨作用域隔离、同意/拒绝、422、409 与仅 `todos.read` 的 403。`eng/testing/test-matrix.json` 将 api-sqlserver/api-mysql 最低发现数从 65 调整为 66，full 从 663 调整为 665。

## 状态、许可证与容量边界

- 本切片没有新增依赖或第三方许可证变化。
- 本验证使用开发真实栈容器，不是生产等价容量认证。
- Worker 崩溃恢复、事件发布与 Notifications 消费不在本记录范围。

## 规则与 Skill 演进

本任务未触发规则或 Skill 演进。Refresh 竞态与 Tenant 进租户 flaky 由既有会话、权限和真实栈规则覆盖；未形成新的规则冲突或项目 Skill 缺口。
