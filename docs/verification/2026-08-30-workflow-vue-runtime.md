# Workflow Vue Runtime 首个纵向切片验证

## 范围

- 基线提交：`bca25811bbb82f76db2e6f6072a1860537d15a0f`
- 任务快照：`workflow-vue-runtime-20260830`
- 交付范围：我的待办、待办详情、静态表单渲染、同意/驳回、精确操作权限、ProblemDetails 恢复。
- 不在本切片：定义/表单设计器、定义列表、实例发起、实例轨迹。

## 契约与安全边界

- `workflowListMyTodos`、`workflowGetTodo`、`workflowApproveTodo`、`workflowRejectTodo` 已进入双库运行时 OpenAPI 快照和确定性客户端生成链。
- Vue API 适配器只调用生成 Operation，不拼接 `/api/v1` 路径，也不声明第二套后端 DTO。
- `JsonElement` 生成类型保持 `unknown`，运行时 guard 只接受 JSON 值；Workflow 再按 adapter v1、固定字段类型和字段策略收紧。
- 表单渲染只使用仓库内静态控件映射；未知 adapter、未知字段类型和未知策略失败关闭，不执行 HTML 或动态组件。
- `workflow.todos.approve` 与 `workflow.todos.reject` 独立控制按钮是否进入 DOM；服务端权限仍是最终授权边界。

## 新鲜验证结果

| 验证 | 结果 |
| --- | --- |
| SQL Server/MySQL 客户端 OpenAPI 运行时导出 | 两侧各 1/1，通过且规范一致 |
| `pnpm test:openapi` | 118/118 通过 |
| `pnpm --filter @fullnet/client-contracts test` | 139/139 通过 |
| `pnpm --filter @fullnet/client-contracts build` | 通过 |
| `pnpm --filter @fullnet/admin test` | 504/504 通过 |
| `pnpm --filter @fullnet/admin build` | typecheck 与 Vite production build 通过 |
| `pnpm test:localization` | 7/7 通过 |
| `pnpm audit:clients` | 无未审查的 critical/high 告警 |
| `pnpm openapi:client:generate -- --check` | 生成产物零漂移 |
| `pnpm openapi:client:snapshot -- --check --offline` | 快照与 manifest 一致 |
| `pnpm test:inner -- --snapshot workflow-vue-runtime-20260830` | 影响目标为 none |

## 包体证据与未关闭项

Workflow 路由保持动态导入，生产构建生成独立 `WorkflowTodosView` chunk，约 `10.09 kB` minified、`3.59 kB` gzip。现有全局首屏包体门禁仍失败：测得 `931012/273747` bytes，历史基线为 `585792/193619` bytes。首屏静态图已经在本切片前跨多个模块漂移，不能用本次 Workflow chunk 直接解释，也不在缺少同基线 A/B 的情况下抬高预算。

后续应单独建立前端首屏包体治理任务，重建同提交 A/B、定位 `http`、`adminI18n` 与 Element Plus 共享 chunk 的静态进入原因，再决定拆分或基线重认定。本切片保持 `Capacity-not-verified`，不声明容量达标。

## 规则与 Skill 演进

未发现新的规则冲突或项目 Skill 缺口，不更新规则与 Skill 候选。
