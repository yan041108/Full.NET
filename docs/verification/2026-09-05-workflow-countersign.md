# Workflow 审批加签验证记录

## 结论

- 状态：`Build-verified`，不提升为 `Verified`。
- 已交付前加签与后加签：`POST /api/v1/workflow/todos/{todoId}/countersign`（`workflowCountersignTodo`）。
- 已交付加签链查询与取消：`GET .../countersign-chain`（`workflowGetTodoCountersignChain`）、`POST .../countersign/cancel`（`workflowCancelTodoCountersign`）。
- 权限码：`workflow.todos.countersign`。
- 迁移 **114**：`fn_workflow_countersign_chain`、`fn_workflow_countersign_item`。
- 前加签将原办理人待办置为 `awaiting_before_countersign` 并按顺序激活加签待办；全部完成后恢复原办理人。
- 后加签在原办理人同意后依次激活加签待办，末位加签人完成后才推进节点。
- Vue 待办页在 `workflow.todos.countersign` 权限下展示方向选择、办理人选择与链顺序；无权限不渲染入口。

## 本地证据

- Workflow 模块 Release 构建通过。
- `WorkflowTodoCountersignServiceTests` 4/4 通过（权限、非法办理人、前加签首待办、幂等重放）。
- `WorkflowTodoReturnServiceTests` 构造器已适配加签服务注入。

## 延后

- OpenAPI 快照导出与 `pnpm openapi:client:generate` 完整零漂移（待 Host.Api 聚焦导出后执行）。
- SQL Server/MySQL Workflow API 双库 Integration 执行。
- 页面级 Playwright/真实栈 E2E、视觉微调、人工逐页验收。
- Linux Native AOT publish/原生进程 E2E。

规则/Skill 演进：未触发。
