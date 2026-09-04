# Admin Form Designer 独立模块验证

- 初始验证日期：2026-08-30
- ESM 适配复验日期：2026-09-04
- 当前任务基线：`f940ba7956f5a940f8dea6b3e29c211fbb6bad1c`
- 当前任务快照：`workflow-vform3-real-render-20260904`
- 结论：旧 `vform3-builds@3.0.10` UMD 已由仓库内 Vue 3.5/Vite 8 ESM 安全子集替换；真实 Edge 中既有 Draft 字段可见，组件包单测、类型检查、管理端生产构建与包体门禁通过。

## 当前边界

- `packages/admin-form-designer` 负责 Host、延迟加载、VForm3 兼容 JSON 状态、九类安全字段目录和纯设计交互。
- 通用包不依赖 `@fullnet/client-contracts`、Workflow API、Workflow Schema、Element Plus 或旧 VForm3 NPM 运行时。
- `ui/admin/src/workflow/vform3-adapter.ts` 仍是 VForm JSON 与 `WorkflowFormSchema` 之间唯一失败关闭的编译边界。
- 设计器不包含代码/SFC 生成、脚本/CSS/HTML 编辑、远程模板、Axios、Ace、Quill、文件/图片/富文本或运行时扩展。
- H5、微信小程序和支付宝小程序继续只消费服务端编译后的 `WorkflowFormSchema`，不安装该设计器。

## 回归原因与修复证据

旧 UMD 在 Vue 3.5 下出现内部 `formRef` 不可用：`getFormJson` 能读到载入数据，但 `designer.loadFormJson` 替换的字段数组没有驱动画布刷新。曾尝试强制刷新、补充字段属性、Vue 去重、调整安装时机和保留数组身份，均未解决真实浏览器问题，已全部撤销。

新增 Playwright 回归通过真实 Vite 页面打开 Workflow 表单编辑器，加载字段 `amount_e2e` 后检查画布文本。该用例在旧实现稳定失败，在 ESM 状态与画布共用同一响应式引用后通过：`1 passed (10.3s)`。

## 包体证据

同一 Windows、Node.js 24、pnpm 10.26.0、Vite 8.1.5 环境执行生产构建并由治理脚本读取 minified/gzip 实际字节：

| 指标 | 旧 UMD 基线 | 当前 ESM | 变化 |
| --- | ---: | ---: | ---: |
| 首屏静态 JS minified | 994,028 B | 995,004 B | +0.10% |
| 首屏静态 JS gzip | 294,317 B | 287,496 B | -2.32% |
| VForm3 设计器延迟 JS minified | 1,461,821 B | 8,450 B | -99.42% |
| VForm3 设计器延迟 JS gzip | 342,013 B | 3,203 B | -99.06% |
| VForm3 Element Plus 运行时图 | 467,256 / 173,109 B | 已移除 | -100% |

旧 UMD、旧 Axios 和设计器专用 Element Plus 图已从 workspace 锁文件及生产依赖图移除。新的 `VForm3EsmDesigner-*` 延迟块按实际 8,450/3,203 B 封板并允许最多 5% 回涨，不能通过修改预算掩盖回归。

## 许可与安全证据

- 上游仓库和提交固定为 `vform666/variant-form3-vite@c67479e496bab56a93a3dff168a4f529d8293c67`。
- `packages/admin-form-designer/vendor/vform3/LICENSE.txt` 保存上游完整自定义许可；组件头保留作者声明，`THIRD-PARTY-NOTICES` 已同步采用模型。
- 生产源码和锁文件不再包含 `vform3-builds`、Ace、Quill、Axios、`eval` 或 `new Function` 依赖路径。
- Workflow Adapter 对未知控件、脚本、远程 URL、HTML/iframe、CSS 与可执行配置继续失败关闭，严格 CSP 不增加 `unsafe-eval`。

## 2026-09-04 新鲜验证

| 命令 | 结果 |
| --- | --- |
| `pnpm --filter @fullnet/admin-form-designer test` | 3 个测试文件、8 个用例通过 |
| `pnpm --filter @fullnet/admin-form-designer build` | 通过 |
| `pnpm --filter @fullnet/admin build` | 类型检查与 Vite 生产构建通过 |
| `pnpm --filter @fullnet/admin-parity-e2e exec playwright test tests/shell-parity.spec.mjs --grep "工作流表单编辑器"` | Edge 1/1 通过 |
| `pnpm test:bundle-budgets` | 3/3 目标通过 |
| `pnpm test:governance` / `pnpm test:performance-governance` | 52/52、14/14 通过 |
| Workflow 管理端目标单测 | 3 个文件、13/13 通过 |
| Contracts / i18n / uni-app | 163/163、8/8、135/135 通过 |
| `pnpm audit:clients` | 无未审查 Critical/High；保留既有 Vite 审查例外 |

管理端全量套件在本机执行为 608/610 通过，两个无关用例命中各自 5 秒超时；对应两个文件隔离复跑为 27/27 通过，未修改无关实现或放宽超时。目标提交 `9d465a27` 的 API/Worker Native AOT runs [`33880651936`](https://github.com/yan041108/Full.NET/actions/runs/33880651936) / [`33880652046`](https://github.com/yan041108/Full.NET/actions/runs/33880652046) 成功；CI run [`33880652055`](https://github.com/yan041108/Full.NET/actions/runs/33880652055) 的客户端门禁和 Workflow 表单目标真实栈通过，整条 run 因无关宽泛真实栈失败为 failure，因此没有将其误记为全绿。
