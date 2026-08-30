# Workflow 设计器第三方依赖 PoC

> 2026-08-30 后续状态：本记录的技术风险结论仍有效，但 No-Go 裁决已被项目所有者明确要求“VForm3 直接使用、复制 Workflow-Vue3”的后续决策覆盖。当前采用边界与剩余风险见 `docs/development/third-party/vform3.md` 和 `docs/development/third-party/workflow-vue3.md`。

- **结论：** `No-Go`；命中实施计划的 CSP/动态代码停止条件
- **日期：** 2026-08-30
- **Full.NET 基线：** `d44672d8bdd2893680b0786c4765cb5fc559a7dd`
- **任务快照：** `workflow-form-runtime-v1-20260830`
- **产品代码影响：** 无；未修改 `ui/admin/package.json`、`pnpm-lock.yaml` 或 `THIRD-PARTY-NOTICES`
- **来源记录：** [VForm3](../development/third-party/vform3.md)、[Workflow-Vue3](../development/third-party/workflow-vue3.md)

## 1. 裁决

停止将 `vform3-builds@3.0.10` 直接接入管理后台。它在当前前端栈下可以编译和挂载，但发布包包含 Full.NET 明确禁止的动态代码执行与任意扩展路径；严格 CSP 下默认设计器还会请求外部模板图片。类型兼容通过不能覆盖安全协议不兼容。

Workflow-Vue3 的产品交互仍可作为需求与重写参考，但源码迁移同时受两个门禁约束：授权凭据尚未形成可审计的位置/摘要；候选目录仍包含 `new Function`、远程请求配置、随机持久 Id 和外部资产。当前不进入后续 Designer/Adapter 产品实现。

## 2. 隔离环境

PoC 在仓库外临时目录执行，没有修改产品锁文件：

| 依赖 | 精确版本 |
| --- | --- |
| Vue | `3.5.40` |
| Element Plus | `2.14.3` |
| TypeScript | `6.0.3` |
| Vite | `8.1.5` |
| `@vitejs/plugin-vue` | `6.0.8` |
| `vue-tsc` | `3.3.7` |
| `vform3-builds` | `3.0.10` |

页面同时挂载 `v-form-designer` 与 `v-form-render`，使用不含脚本/CSS 的空安全表单；CSP 使用 `script-src 'self'`，不包含 `unsafe-eval`。

## 3. 构建、包体与许可

| 检查 | 结果 |
| --- | --- |
| `pnpm install` | 通过 |
| `pnpm typecheck` | 通过；仅需为 TypeScript 6 补充普通 `*.css` 模块声明 |
| `pnpm build` | 通过；Vite 报告候选包中的 direct `eval` 警告 |
| JS | 2,727,083 B minified；698,199 B gzip；552,223 B Brotli |
| CSS | 493,170 B minified；63,128 B gzip；47,534 B Brotli |
| source map | 8,360,955 B |
| `pnpm licenses list --prod --json` | 执行成功；`vform3-builds` 为 `Unknown` |
| 候选包内容 | 仅 `dist/`、`package.json`、`README.md`；无 LICENSE 文件，package metadata 无 license 字段 |
| 漏洞审计 | 未验证；临时环境的 npmmirror registry 不提供 pnpm audit endpoint |

这些数字是隔离 PoC 的单页全量产物，不是接入现有管理后台后的增量，也不是生产性能结论。它们只证明直接集成成本较高；如果未来存在安全重构版本，仍须在真实懒加载路由重新测量增量。

## 4. 静态安全检查

候选 `dist` 的两个 JavaScript 文件对 `eval`、`new Function`、脚本/CSS 扩展和表单事件入口共检出 109 个文本匹配。人工定位确认包含：

- 任意 CSS 注入与脚本节点注入；
- `loadRemoteScript`；
- 通过 `new Function` 执行 `onFormCreated`、`onFormMounted`、`onFormDataChange` 和字段事件；
- 默认 UI 中的 HTML、图片、文件、富文本、自定义扩展、全局 CSS/函数与事件代码编辑器。

Full.NET 的边界要求危险能力不进入发布产物，仅在配置或 DOM 上隐藏不能满足该要求。

## 5. 浏览器证据

使用 Playwright CLI 在真实 Chromium 中完成以下操作：

- Designer 与 Renderer 首次挂载成功；
- 点击切换按钮卸载成功；
- 保留键盘焦点并按 Enter 后重新挂载成功；
- 默认设计器 UI 显示被 Full.NET 禁止的 HTML、文件、图片、富文本、自定义扩展及代码编辑能力；
- 首次挂载记录 17 条 console error，重新挂载后累计 33 条，主要是 8 个 `ks3-cn-beijing.ksyuncs.com` 模板图片在 `img-src 'self'` 下被阻止；
- 另观察到 Element Plus Radio `label as value` 的弃用警告。

空安全表单没有实际触发 `unsafe-eval` 运行时异常，但静态产物与可配置执行路径已经违反禁止 `new Function` 的不变量，不能据此把严格 CSP 判为通过。

## 6. Workflow-Vue3 候选复核

固定上游提交 `8d81e61edc495d07ae5fdc21e3f24aacc7f32991` 后，对本地候选 `src` 执行无索引差异：32 个变化路径，新增 7,818 行、删除 505 行。本地候选共有 49 个文件，源代码约 11,331 行；排序后的文件哈希清单摘要已记录在来源记录中。

静态扫描确认候选仍携带动态投票脚本、远程 URL/Header/Body、随机节点 Id、外部字体/图片和旧项目结构。这份证据支持“选择性交互重写”，不支持目录级迁移。

## 7. 后续重新开放门禁

重新开放需要一个物理移除动态执行和远程资产的 CSP-safe 构建，并重新完成精确来源、许可/授权、漏洞、包体、可访问性、严格 CSP 和真实栈 E2E。否则 Workflow 继续使用已交付的 Full.NET 权威 `WorkflowFormSchema`、组件目录和服务端编译边界，不进入 VForm3 Adapter 产品切片。

本结论按实施计划 Stop Conditions 终止当前第三方接入分支；不通过放宽 CSP、保留隐藏代码或提高预算继续推进。

## 8. 规则与 Skill 演进

现有第三方许可、CSP、客户端依赖和停止条件已覆盖本次风险，没有发现新的规则或项目 Skill 缺口，不更新规则与 Skill 候选。
