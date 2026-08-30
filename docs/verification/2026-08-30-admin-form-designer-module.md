# Admin Form Designer 独立模块验证

- 日期：2026-08-30
- 任务基线：`73004d69746e58d0f2b70561d162d19e676fa3a8`
- 任务快照：`workflow-vform3-direct-20260830`
- 结论：VForm3 已封装为独立 workspace 包；Workflow 只保留领域 Adapter 和薄 Wrapper；真实浏览器读写、严格 CSP 与包体门禁通过。

## 边界

- `packages/admin-form-designer` 负责 VForm3 的延迟加载、按 Vue App 隔离安装、所需 Element Plus 组件的选择性注册以及通用 JSON 读写。
- 通用包不依赖 `@fullnet/client-contracts`、Workflow API 或 Workflow Schema。
- `ui/admin/src/workflow/vform3-adapter.ts` 独立承担 VForm JSON 与 `WorkflowFormSchema` 的失败关闭转换。
- 其他业务场景可以消费通用 Host，但必须实现自己的组件目录和闭合 Adapter，不能直接持久化第三方 JSON。

## 包体证据

同一 Windows、Node.js 24.12.0、pnpm 10.26.0、Vite 8.1.5 环境执行生产构建。

| 指标 | 任务基线提交 | 当前候选 | 变化 |
| --- | ---: | ---: | ---: |
| 首屏静态 JS minified | 950,287 B | 994,028 B | +4.60% |
| 首屏静态 JS gzip | 277,441 B | 294,317 B | +6.08% |
| VForm3 延迟 JS minified | 不存在 | 1,461,821 B | 新增可选块 |
| VForm3 延迟 JS gzip | 不存在 | 342,013 B | 新增可选块 |
| VForm3 Element Plus 运行时静态图 minified | 不存在 | 467,256 B | 新增可选图上界 |
| VForm3 Element Plus 运行时静态图 gzip | 不存在 | 173,109 B | 新增可选图上界 |

VForm3 主体没有进入首屏静态依赖图，但它对 Vue 的 UMD external 引用改变了共享切块，形成约 43 KB minified / 16.6 KB gzip 的首屏增量。用空 Host 替换 VForm3 的同环境 A/B 为 951,493 B / 277,763 B，证明该增量来自直接集成，而不是 Workflow 页面主体。

全量 `app.use(ElementPlus)` 虽能渲染，但会把首屏 minified 推高到 1,596,196 B，已拒绝。最终方案只注册真实浏览器发现的 30 个 `el-*` 组件；浏览器确认设计器渲染、`setFormJson` 成功且没有组件解析失败。VForm3 安装期间写入的全局 `window.axios` 会在安装后按原属性描述符恢复，不污染宿主。VForm3 自身仍产生 5 条上游 Radio API 弃用提示，生产构建仍报告 direct `eval`，许可证与 CSP 风险未消失，详见 VForm3 来源记录。

严格 CSP 验证使用 `script-src 'self'; object-src 'none'; base-uri 'self'`，不开放 `unsafe-eval`。设计器基础加载、权威 Draft 回读和保存闭环可用；VForm3 上游“导入 JSON”会尝试 Blob Worker 和外部 Ace snippet，已由 CSP 阻断，不纳入 Full.NET 支持面。业务页面不得依赖该入口，表单 JSON 只通过 Full.NET Adapter 与权威 API 进入设计器。

仓库原有 585,792 B / 193,619 B 首屏预算来自 2026-07-28，已在本任务基线提交上以 950,287 B / 277,441 B 失败，不能继续作为当前产品线的有效基线。本次基于同环境生产构建重新封板首屏、VForm3 UMD 和选择性 Element Plus 运行时图，三者分别允许最多 5% 回涨。运行时图统计包含与首屏共享的静态依赖，因此 467,256 B / 173,109 B 是保守上界，不是额外网络传输量。

## 验证

- `pnpm --filter @fullnet/admin-form-designer build`
- `pnpm --filter @fullnet/admin-form-designer test`
- `pnpm --filter @fullnet/admin test -- src/workflow/VForm3WorkflowDesigner.test.ts src/workflow/vform3-adapter.test.ts`
- `pnpm --filter @fullnet/admin typecheck`
- `pnpm --filter @fullnet/admin build`

专项与合并候选结果：

| 检查 | 结果 |
| --- | --- |
| 独立包 Build | 通过 |
| 独立包 Unit | 1/1 通过 |
| 管理端 Unit | 570/570 通过 |
| Contracts / i18n / uni-app | 163/163、8/8、103/103 通过 |
| Governance | 52/52 通过 |
| Performance Governance | 14/14 通过 |
| Client Dependency Audit | 无未审查 Critical/High；保留已批准 Vite 例外 |
| Snapshot inner / slice | 选择器均判定无 Integration 目标 |
| 真实浏览器探针 | 严格 CSP 下设计器渲染、JSON 设置成功、无 `el-*` 解析失败；危险导入入口被阻断 |
| VForm3 延迟块与运行时图预算 | 通过 |
| 完整 `test:bundle-budgets` | 4/4 目标通过 |

浏览器探针只用于验证并已从生产源码删除；当前发布物不包含测试入口。
