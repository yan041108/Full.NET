# VForm3 ESM 源码适配实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目标：** 用当前 Vue 3.5/Vite 8 可静态分析的仓库内 ESM 安全子集替换 `vform3-builds@3.0.10` UMD 入口，修复工作流表单 JSON 已加载但画布不刷新的真实浏览器回归，同时保持现有 Host API、Workflow Schema 和移动端边界不变。

**架构：** `packages/admin-form-designer` 继续作为通用设计器边界。新增的 ESM 组件只实现已批准字段的画布、字段属性和排序交互，输入输出仍使用 VForm3 3.0.10 的 `widgetList/formConfig` 结构；Workflow Adapter 仍是保存前唯一安全编译边界。代码生成、脚本/CSS/HTML、远程模板、文件/图片/富文本、Axios 和运行时扩展不会进入依赖图。

**技术栈：** Vue 3.5、TypeScript、Element Plus、Vitest、Playwright、pnpm。

**来源与许可：** 交互与 JSON 模型基于 `vform666/variant-form3-vite` 提交 `c67479e496bab56a93a3dff168a4f529d8293c67`；保留 Variant Form 许可条款、作者声明和来源记录，不将其标记为 Full.NET MIT 源码。

---

### 任务 1：锁定浏览器回归与 ESM JSON 内核

**文件：**
- 修改：`tests/e2e/admin-parity/tests/shell-parity.spec.mjs`
- 新增：`packages/admin-form-designer/src/esm/vform3-schema.ts`
- 新增：`packages/admin-form-designer/src/esm/vform3-schema.test.ts`

- [x] 保留真实 Vite/Edge 回归：编辑既有 Workflow 表单后，画布必须显示字段机器码 `amount_e2e`。
- [x] 先为 JSON 校验、深克隆、字段新增/删除/重排建立失败单测。
- [x] 实现仅接受普通对象、`widgetList` 数组和 `formConfig` 对象的 ESM 状态内核；不得执行 JSON 内任何字符串。
- [x] 运行 `pnpm --filter @fullnet/admin-form-designer test`，确认内核单测通过而浏览器回归仍为 RED。

### 任务 2：实现 VForm3 兼容的 ESM 安全设计器

**文件：**
- 新增：`packages/admin-form-designer/src/esm/VForm3EsmDesigner.vue`
- 新增：`packages/admin-form-designer/src/esm/vform3-catalog.ts`
- 新增：`packages/admin-form-designer/src/esm/VForm3EsmDesigner.test.ts`
- 修改：`packages/admin-form-designer/src/element-plus-components.ts`

- [x] 以 VForm3 的左侧组件库、中央画布、右侧属性面板为交互基线，实现 `input`、`textarea`、`number`、`date`、`time`、`radio`、`checkbox`、`select`、`switch` 的闭合目录。
- [x] 公开兼容的同步 `setFormJson`/`getFormJson`，并保证 JSON 加载后同一响应式状态直接驱动画布。
- [x] 支持新增字段、选择字段、编辑 `name/label/required/fullNetSectionKey`、删除和上下重排；禁用态由 Host 遮罩保持只读。
- [x] 属性面板只写入 Workflow Adapter 已知键；选项类字段以纯文本值列表维护，不提供脚本、远程地址或 HTML/CSS 入口。
- [x] 运行组件单测，覆盖首次加载、第二次覆盖加载、属性编辑和排序后的 JSON 回读。

### 任务 3：切换加载器并移除 UMD 运行时

**文件：**
- 修改：`packages/admin-form-designer/src/vform3-loader.ts`
- 修改：`packages/admin-form-designer/src/VForm3DesignerHost.vue`
- 修改：`packages/admin-form-designer/src/VForm3DesignerHost.test.ts`
- 删除：`packages/admin-form-designer/src/vform3-builds.d.ts`
- 修改：`packages/admin-form-designer/package.json`
- 修改：`pnpm-lock.yaml`

- [x] 把延迟加载目标改为本地 `VForm3EsmDesigner.vue`，保留 Host 的 `ready/error/getFormJson/setFormJson` 公共契约。
- [x] 删除 Vue 3.5 下访问第三方 `designer/$refs.formRef` 的兼容旁路和 `window.axios` 恢复逻辑。
- [x] 从 workspace 删除 `vform3-builds` 直接依赖并执行 `pnpm install --lockfile-only` 更新锁文件。
- [x] 运行 `pnpm --filter @fullnet/admin-form-designer test`、`pnpm --filter @fullnet/admin-form-designer build`。
- [x] 运行浏览器回归 `pnpm --filter @fullnet/admin-parity-e2e exec playwright test tests/shell-parity.spec.mjs --grep "工作流表单编辑器"`，目标由 RED 变为 GREEN。

### 任务 4：许可、危险能力和包体治理

**文件：**
- 新增：`packages/admin-form-designer/vendor/vform3/LICENSE.txt`
- 新增：`packages/admin-form-designer/vendor/vform3/PROVENANCE.md`
- 修改：`THIRD-PARTY-NOTICES`
- 修改：`docs/development/third-party/vform3.md`
- 修改：受现有治理测试定位到的依赖/包体清单文件

- [x] 逐字保存上游许可文本，并记录仓库、精确提交、采用模型、未采用目录及本地改造边界。
- [x] 更新第三方声明：不再发布 NPM UMD 包，但设计与兼容 JSON 仍属于 Variant Form 来源范围。
- [x] 静态扫描生产依赖图，确认没有 `eval`、`new Function`、Ace、Quill、Axios、远程模板、代码生成器和上游运行时扩展。
- [x] 重新执行生产构建并以实际产物更新唯一包体预算；禁止沿用旧 UMD 数值或通过抬高预算掩盖回归。

### 任务 5：全链验证、文档与 CI

**文件：**
- 修改：`docs/verification/2026-08-30-admin-form-designer-module.md`
- 修改：仅被真实状态影响的 Workflow 开发计划/路线图

- [x] 运行 `pnpm test:integration:affected:plan -- --snapshot workflow-vform3-real-render-20260904 --phase inner` 审查影响集。
- [x] 运行选择器命中的本地静态检查、治理测试、单测、管理端构建和目标 Playwright 回归。
- [x] 运行 `git diff --check`、`git status --short`，审查只包含本任务影响集。
- [x] 更新验证记录，明确本地命令、新旧包体、已移除风险和仍保留的自定义许可证约束。
- [x] 提交并推送后检查目标提交的 GitHub Actions：`9d465a27` 的 API/Worker Native AOT runs `33880651936` / `33880652046` 成功；CI run `33880652055` 的客户端和目标 Workflow 表单真实栈通过，宽泛真实栈仍有与本适配无关的既有失败，未把整条 run 误记为 success。

### 回退边界

- ESM 设计器只替换 `packages/admin-form-designer` 内部实现，回退不得修改 Workflow HTTP API、数据库 Schema、`WorkflowFormSchema`、组件目录或移动端渲染协议。
- 如安全子集在发布前无法满足设计交互，优先回退为 Full.NET 原生表单设计器实现；不得恢复 `unsafe-eval`、脚本编辑或远程模板能力。
- 任何回退都必须保留本计划的真实浏览器回归，防止再次出现“JSON 可读但画布为空”的假通过。
