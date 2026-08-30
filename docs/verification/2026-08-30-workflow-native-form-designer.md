# Workflow 原生静态表单设计器验证

## 范围与结论

- 任务基线：`eb47f62bd7a389d9a748e817d6531495730c342f`
- 任务快照：`workflow-native-form-designer-20260830`
- 交付范围：生成客户端、闭合 Draft 状态、静态设计器、表单管理页、精确动作权限和双库真实闭环。
- 结论：通过；不接入 VForm3，不使用 `eval`、`new Function`、`v-html`、动态组件或远程设计资源。
- 容量状态：`Capacity-not-verified`；本切片验证正确性、安全闭包与双库一致性，不声明生产等价容量达标。

## 静态协议与权限边界

- 页面只通过 `workflowListForms`、`workflowGetForm`、`workflowCreateForm`、`workflowUpdateFormDraft`、`workflowPublishForm` 与 `workflowGetFormComponentCatalog` 六个生成 Operation 访问后端。
- API 边界使用 `isWorkflowFormSchema` 将 OpenAPI 宽类型收窄为 adapter/schema v1 的闭合静态协议；未知版本、字段类型和不安全约束失败关闭。
- 设计器只渲染服务端目录同时声明 `designable/publishable/executable` 的已知字段类型，约束编辑器只显示目录允许键。
- `workflow.forms.create`、`workflow.forms.update`、`workflow.forms.publish` 分别控制对应操作是否进入 DOM；服务端仍以同名 Endpoint 权限作最终授权。
- 409 ProblemDetails 保留本地 Draft，不覆盖用户尚未保存的编辑；保存和发布都携带当前 `draftRevision`。

## 双库真实栈结果

同一个 Playwright spec 在 SQL Server 与 MySQL 上执行创建表单、增加 money 字段、保存草稿、发布、读取冻结版本，以及只读账号写 API 403/写按钮不进入 DOM。

| 提供程序 | 结果 | 证据 |
| --- | --- | --- |
| SQL Server | 2/2 通过 | 管理员闭环 45.3 秒；只读权限 43.5 秒 |
| MySQL | 2/2 通过 | 完整聚焦 spec 1.4 分钟 |

浏览器同时监听未捕获异常、CSP、`unsafe-eval` 与未知组件错误，最终为零。真实栈曾发现编辑器浮层低于固定 Header、Element Plus 组件未显式导入两个缺陷；修复后重新执行上述双库证据。

## 新鲜验证结果

| 验证 | 结果 |
| --- | --- |
| `pnpm test:openapi` | 118/118 通过；Vue API 覆盖清单为 50 个生产模块 |
| `pnpm --filter @fullnet/client-contracts test` | 163/163 通过 |
| `pnpm --filter @fullnet/admin test` | 547/547 通过 |
| Workflow 表单定向 Vue 测试 | API/页面 6/6、设计器 4/4 通过 |
| `pnpm --filter @fullnet/admin build` | typecheck 与 Vite production build 通过 |
| `pnpm --filter @fullnet/admin-i18n test` | 8/8 通过 |
| `pnpm test:localization` | 7/7 通过 |
| `pnpm test:governance` | 52/52 通过 |
| `pnpm audit:clients` | 无未审查 critical/high 告警 |
| `pnpm test:slice -- --snapshot workflow-native-form-designer-20260830` | 影响目标为 none |

## 包体与发布边界

`/workflow/forms` 保持路由级动态导入。最终生产构建中路由自有产物为：

- JavaScript：约 15.73 kB minified、4.73 kB gzip；
- CSS：约 6.88 kB minified、1.51 kB gzip。

两项合计约 22.61 kB minified、6.24 kB gzip，不进入初始路由执行路径。共享 Element Plus 与 HTTP chunk 会受全局分块策略影响，本记录不把共享 chunk 波动伪装成精确 A/B 增量；后续仍由既有客户端包体门禁治理。

## 规则与 Skill 演进

本次发现均已由现有 CSP、生成客户端覆盖、权限 DOM 和真实栈规则捕获，没有新的规则冲突或项目 Skill 缺口，不更新规则与 Skill 候选。
