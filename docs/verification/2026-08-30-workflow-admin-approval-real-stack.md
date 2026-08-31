# Workflow Vue 管理端审批真实栈验证

## 范围与结论

- 基线提交：`be61c547d2d1ad5a2b0b4634edaadab29905f80d`
- 任务快照：`workflow-admin-real-stack-20260830`
- 验证范围：Host 作用域下通过 API 建立已发布表单/定义版本，在 Vue 管理端发起实例、同意、驳回，验证字段策略、动作权限、并发冲突和权威刷新；同一场景分别运行于 SQL Server 与 MySQL。
- 结论：本管理端 Host 审批子切片 `Build-verified`；SQL Server/MySQL 真实栈浏览器用例各 3/3 通过。
- 非结论：本记录当时未关闭 Tenant 作用域与危险 Patch 422；该两项已由 [`2026-08-31-workflow-first-slice-closeout.md`](2026-08-31-workflow-first-slice-closeout.md) 关闭。Worker 恢复、`notify.cc`、`gateway.exclusive`、生产容量和人工产品验收仍未关闭，因此 Workflow 不提升为 `Verified`。

## 修复的真实栈缺口

1. Vue 待办适配器原先调用普通详情 `/api/v1/workflow/todos/{todoId}`，随后却按包含 `formSchemaHash` 的运行时契约校验，真实响应必然失败关闭；现改为生成客户端的 `workflowGetTodoRuntime` Operation，对应 `/runtime` 端点。
2. 审批动作收到旧 Revision 409 后，页面原先保留已失效详情和动作按钮；现关闭过期详情并重新读取“我的待办”，同时保留原始 409 ProblemDetails，刷新失败也不会重新开放旧动作。

测试先行证据：

- `pnpm --filter @fullnet/admin test -- workflow-todos.test.ts` 首次失败，明确显示期望 `/runtime`、实际请求普通详情端点；修复后相关 17/17 通过。
- `WorkflowTodosView` 新增 409 权威刷新断言；修复前期望列表读取 2 次、实际 1 次，修复后通过。

## 双库真实栈场景

同一份 `tests/e2e/admin-real-stack/tests/workflow-approval.spec.mjs` 在两个数据库提供程序执行：

1. 管理员通过 API 创建并发布静态表单和线性审批定义，再在 Vue 定义页填写业务标识及初始表单并发起实例；
2. 在 Vue“我的待办”分别完成同意与驳回，API 回读实例终态为 `completed` 与 `rejected`；
3. 验证 `reason` 为只读、`secret` 不进入 DOM，并按运行时 `required` 策略填写 `decision`；
4. 仅有 `workflow.todos.read` 的用户看不到同意/驳回按钮，直接绕过客户端调用返回 403 与 `authorization.permission_denied`；
5. 另一请求先完成同一待办后，页面使用旧 Revision 办理得到 409，随后刷新权威待办并移除过期行与动作按钮。

## 新鲜验证结果

| 验证 | 结果 |
| --- | --- |
| SQL Server admin real-stack | 3/3 通过，约 57 秒；103 个迁移脚本成功，API/Worker 构建均 0 警告、0 错误 |
| MySQL admin real-stack | 3/3 通过，约 1.6 分钟；103 个迁移脚本成功，API/Worker 构建均 0 警告、0 错误 |
| `pnpm --filter @fullnet/admin test` | 162 个文件、572/572 通过 |
| `pnpm --filter @fullnet/admin typecheck` | 通过 |
| `pnpm --filter @fullnet/admin build` | 通过；保留既有 VForm3 `eval` 与大 chunk 告警，不将告警表述为消失 |
| `pnpm test:bundle-budgets` | 4/4 预算通过；Vue initial static JS 为 995,775 bytes / gzip 296,134 bytes |
| `pnpm audit:clients` | 通过；接受既有 `vite` `GHSA-fx2h-pf6j-xcff` 精确例外，无未审查 Critical/High |
| `pnpm licenses list --prod --json` | 成功；`vform3-builds@3.0.10` 仍由包元数据报告为 `Unknown` |
| affected plan / inner | 快照识别 5 个任务变更，Integration 目标为 `none`；真实双库浏览器场景已按上表显式执行 |

## 状态、许可证与容量边界

- 本切片没有新增依赖；Workflow-Vue3 作者授权边界没有变化。
- VForm3 的 npm 包许可证元数据仍不完整，本次只记录既有事实，不据此扩大其许可结论；后续发布仍服从既有依赖 PoC 和仓库第三方声明门禁。
- 本验证使用开发真实栈容器，不是生产等价容量认证，保持 `Capacity-not-verified`。
- 首次实栈尝试还发现本机 Docker Desktop 未运行；启动既有 Docker Desktop 后才执行有效门禁。该环境失败不计入通过结果。

## 规则与 Skill 演进

本次发现由真实栈覆盖补足了单元 Mock 未发现的 Operation 选错，但现有 TDD、双库真实栈和生成客户端边界规则已经能够预防复发；未形成新的规则冲突或项目 Skill 缺口，不更新规则与 Skill 候选。
