# Workflow Designer and Cross-Platform Form Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `fullnet-module-delivery`, `fullnet-performance-hardening`, and `superpowers:test-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在已验证的 Workflow 核心之上交付 Workflow-Vue3 树形设计器、VForm3 后台表单设计器、受控 Vue Web Adapter，以及 H5/微信/支付宝共用的轻量 `FullNetFormRenderer`。

**Architecture:** 第三方设计器只产生 Draft；服务端编译器是安全与兼容边界，发布不可变 Workflow IR、`WorkflowFormSchema` 和派生 `WebRenderSchema`。Vue 管理端使用 VForm3 Adapter，uni-app 使用静态组件目录；服务端、Vue 和 uni-app 共享协议与 Golden Fixture，不共享 UI 组件。

**Tech Stack:** Workflow-Vue3、VForm3、Vue 3、TypeScript、Vite、Element Plus、uni-app Vue 3、uni-ui、System.Text.Json、Vitest、Playwright。

**执行状态（2026-08-30）：** `Stopped at Task 1`。`vform3-builds@3.0.10` 虽可在当前前端栈完成 typecheck、build 和基础挂载，但发布包包含 `eval`/`new Function`、动态脚本/CSS 与远程资源路径，命中本文 CSP 停止条件；Workflow-Vue3 授权凭据位置/摘要也尚未归档。产品依赖、锁文件与 Notices 均未修改。重新开放条件与原始证据见 [Workflow 设计器第三方依赖 PoC](../../verification/2026-08-30-workflow-designer-dependency-poc.md)；Task 2/3 中与第三方无关的权威 Schema/目录能力已经由后续切片部分交付，但 Task 4/5 不得按本计划继续，必须先形成新的 CSP-safe 设计器决策。

## Global Constraints

- 依赖 [`2026-08-20-workflow-module-design.md`](../specs/2026-08-20-workflow-module-design.md) Approved，且 [`2026-08-20-workflow-first-vertical-slice.md`](2026-08-20-workflow-first-vertical-slice.md) 已达到 Build-verified。
- Workflow-Vue3 作者授权、上游提交、本地改造范围和再分发条件必须在源码迁入前归档；VForm3 精确版本、Variant Form 许可与发布物边界必须在安装前归档。
- VForm3 原始 JSON、Workflow-Vue3 Draft 和第三方组件属性不能成为公共 API 或运行时权威协议。
- 禁止 `new Function`、任意 JavaScript、HTML/iframe、CSS、远程 URL/Headers/Body、动态程序集和未知组件。
- uni-app 不安装 VForm3 或 Element Plus，不使用 `<component :is>` 或异步插件注册；共享包不得依赖 Vue/uni-ui。
- `ui/admin-layui` 保持零 diff。
- 未取得相同环境包体和渲染基线前，不宣称更小、更快或满足固定 KB/ms 指标。
- 开工第一步运行 `pnpm test:task:start -- workflow-designer-form-runtime-20260830`；后续 inner/slice 必须使用同一快照。

---

## File Map

### 来源、依赖与协议

- `docs/development/third-party/workflow-vue3.md`
- `docs/development/third-party/vform3.md`
- `docs/verification/2026-08-30-workflow-designer-dependency-poc.md`
- `THIRD-PARTY-NOTICES`
- `ui/admin/package.json`
- `pnpm-lock.yaml`
- `packages/client-contracts/src/workflow-form-schema.ts`
- `packages/client-contracts/src/workflow-form-schema.test.ts`
- `packages/client-contracts/src/fixtures/workflow-form-schema-v1.json`

### Vue 管理端

- `ui/admin/src/features/workflow/designer/WorkflowDesigner.vue`
- `ui/admin/src/features/workflow/designer/WorkflowDesigner.test.ts`
- `ui/admin/src/features/workflow/designer/node-catalog.ts`
- `ui/admin/src/features/workflow/designer/draft-adapter.ts`
- `ui/admin/src/features/workflow/designer/draft-adapter.test.ts`
- `ui/admin/src/features/workflow/forms/VFormDesignerAdapter.vue`
- `ui/admin/src/features/workflow/forms/VFormDesignerAdapter.test.ts`
- `ui/admin/src/features/workflow/forms/VFormWebRendererAdapter.vue`
- `ui/admin/src/features/workflow/forms/VFormWebRendererAdapter.test.ts`
- `ui/admin/src/views/WorkflowDesignerView.vue`
- `ui/admin/src/views/WorkflowDesignerView.test.ts`
- `ui/admin/src/router/index.ts`
- `ui/admin/src/navigation/catalog.ts`

### 服务端编译目录

- `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowDefinitionCompiler.cs`
- `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowFormCompiler.cs`
- `src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/NodeTypeCatalogEndpoint.cs`
- `src/Modules/Full.NET.Modules.Workflow/Features/ManageForms/FormComponentCatalogEndpoint.cs`
- `src/Modules/Full.NET.Modules.Workflow/Serialization/WorkflowJsonSerializerContext.cs`
- `tests/Full.NET.UnitTests/Workflow/WorkflowDesignerDraftCompilerTests.cs`
- `tests/Full.NET.UnitTests/Workflow/VFormDraftCompilerTests.cs`
- `tests/Full.NET.IntegrationTests/Workflow/WorkflowDesignerPublishAssertions.cs`

### uni-app

- `clients/uniapp/src/features/workflow/forms/fullnet-form-schema.ts`
- `clients/uniapp/src/features/workflow/forms/fullnet-form-state.ts`
- `clients/uniapp/src/features/workflow/forms/FullNetFormRenderer.vue`
- `clients/uniapp/src/features/workflow/forms/fields/FullNetTextField.vue`
- `clients/uniapp/src/features/workflow/forms/fields/FullNetNumberField.vue`
- `clients/uniapp/src/features/workflow/forms/fields/FullNetChoiceField.vue`
- `clients/uniapp/src/features/workflow/forms/fields/FullNetDateTimeField.vue`
- `clients/uniapp/src/features/workflow/forms/fields/FullNetSwitchField.vue`
- `clients/uniapp/src/features/workflow/forms/FullNetFormRenderer.test.ts`
- `clients/uniapp/src/pages/workflow/start.vue`
- `clients/uniapp/src/pages/workflow/todo.vue`
- `clients/uniapp/src/pages.json`
- `tests/e2e/uniapp-h5/tests/workflow-form.spec.mjs`
- `tests/performance/frontend-bundle-budgets.json`
- `tests/performance/workflow-form-bundle-budget.test.mjs`

## Stable Interfaces

```ts
export type WorkflowFormFieldType =
  | 'text'
  | 'textarea'
  | 'integer'
  | 'decimal'
  | 'money'
  | 'date'
  | 'time'
  | 'datetime'
  | 'radio'
  | 'checkbox'
  | 'select'
  | 'switch'

export interface WorkflowFormFieldV1 {
  fieldKey: string
  type: WorkflowFormFieldType
  labelKey: string
  required: boolean
  constraints: Readonly<Record<string, string | number | boolean>>
  options?: ReadonlyArray<{ optionKey: string; labelKey: string }>
}

export interface WorkflowFormSchemaV1 {
  schemaVersion: 1
  adapterVersion: number
  formVersionId: string
  hash: string
  sections: ReadonlyArray<{
    sectionKey: string
    fields: ReadonlyArray<WorkflowFormFieldV1>
  }>
}
```

金额值在线路中使用规范十进制字符串；日期时间使用 Spec 固定的 ISO 8601/UTC 或显式 offset 语义。运行时状态函数只接收 Schema、服务端字段策略和值，不读取 VForm3 属性。

---

### Task 1: 完成第三方来源、许可和兼容 PoC

- [ ] 在两个来源记录中写明仓库 URL、精确 Commit/Package Version、许可证、授权依据、拟迁入/安装范围、禁止范围和更新流程；授权原件若含私密信息只登记受控存放位置与校验摘要，不提交原件内容。
- [ ] 用独立临时分支/工作区验证 VForm3 与当前 Vue、Element Plus、TypeScript、Vite：安装、typecheck、production build、designer mount/unmount、render、CSP 和基本键盘操作。
- [ ] 对旧 Workflow-Vue3 改造成果建立文件清单与上游 diff；确认不携带 Mock、远程资产、LogicFlow 编辑器、数字协议或旧 API Adapter。
- [ ] 运行 `pnpm licenses list --prod --json`、`pnpm audit:clients` 和 Vue production build；记录直接/传递依赖、minified/gzip/Brotli 变化与未验证项。
- [ ] PoC 不通过版本兼容、许可、CSP 或包体停止条件时停止，不修改产品依赖；通过后更新 `THIRD-PARTY-NOTICES` 与锁文件。

### Task 2: 冻结跨端 Schema 与 Golden Fixture

- [ ] 先写 `workflow-form-schema.test.ts` RED：接受 v1 基础字段；拒绝未知 SchemaVersion、字段类型、重复 FieldKey、未知约束、脚本/CSS/HTML/URL 和原型污染键。
- [ ] 建立唯一 Golden Fixture，包含所有首批字段、Hidden/ReadOnly/Editable/Required 节点策略、金额/时间/空值和选项边界。
- [ ] 服务端 `VFormDraftCompilerTests` 使用同一 Fixture 证明 VForm Draft 单向编译结果稳定；客户端不得从 `WebRenderSchema` 反推协议。
- [ ] `packages/client-contracts` 只导出类型、运行时守卫与纯状态机，不导出 Vue 组件。
- [ ] 运行 client-contracts、Workflow Unit 和同输入 Hash 漂移测试。

### Task 3: 强化服务端节点/组件目录与发布编译

- [ ] 先写 RED：目录仅返回当前部署 `Designable/Publishable/Executable` 能力；客户端伪造能力、未知适配版本和被禁组件发布均失败。
- [ ] `NodeTypeCatalogEndpoint` 和 `FormComponentCatalogEndpoint` 返回闭合源生成 DTO，不使用反射扫描。
- [ ] Workflow 编译器接受受限树形 Draft，规范化为单一 IR；VForm 编译器移除设计态元数据并产生 `WorkflowFormSchema + WebRenderSchema + Hash`。
- [ ] 发布 Definition/Form 时原子固化版本绑定；已有实例继续读取原版本。
- [ ] 运行 Unit、SQL Server/MySQL Publish Integration、OpenAPI 和 Host.Api AOT analysis。

### Task 4: 迁移 Workflow-Vue3 树形设计器交互

- [ ] 先写 Vue RED：稳定 NodeKey、添加/删除/分支、不可发布节点、服务端错误定位、Draft Revision 冲突、无发布权限按钮缺失和离开未保存提示。
- [ ] 只迁移树形插入、分支编辑、节点卡片和批准 Drawer；NodeKey 使用 UUID/稳定生成器，不使用 `Math.random()`。
- [ ] `draft-adapter.ts` 只负责 UI Draft 与服务端 Draft DTO 的显式映射；禁止保留旧 `FlowJson` 兼容分支。
- [ ] 设计器从服务端目录决定可设计/可发布状态；前端校验只改善体验，Publish 仍由服务端失败关闭。
- [ ] 运行 Vue Unit、typecheck、production build、路由权限和无权限 DOM 测试。

### Task 5: 接入 VForm3 Designer 与 Web Runtime Adapter

- [ ] 先写 Vue RED：组件白名单、危险配置剔除、Draft Revision、发布预览、节点字段策略、隐藏字段不在 DOM、只读字段不进 Patch 和客户端替换 FormJson 被拒。
- [ ] `VFormDesignerAdapter` 只暴露批准组件与语义属性；保存前转换为服务端 Draft DTO，不直接持久化 VForm3 任意 JSON。
- [ ] `VFormWebRendererAdapter` 只渲染服务端返回的 `WebRenderSchema`，提交只包含 FieldPatch/ExpectedRevision/IdempotencyKey。
- [ ] Designer 与 Workflow Draft 分开保存；发布向导在服务端原子建立 DefinitionVersion → FormVersion。
- [ ] 运行 Vue Unit、CSP、可访问性、production build、bundle budgets 和 admin-real-stack `tests/e2e/admin-real-stack/tests/workflow-designer.spec.mjs`。

### Task 6: 实现 uni-app 静态轻量渲染器

- [ ] 先写 Vitest RED：Golden Fixture 全字段映射、Required/Hidden/ReadOnly、金额字符串、日期时间、未知字段失败、旧 SchemaVersion 失败和 Patch 最小化。
- [ ] `FullNetFormRenderer.vue` 使用显式 `v-if/v-else` 选择静态字段组件；不得使用 `<component :is>`、动态 import、eval 或 VForm Web Adapter。
- [ ] 在 `pages.json` 建立 workflow subPackage，`start.vue` 与 `todo.vue` 只从 API 获取已发布 Schema 和字段策略。
- [ ] Schema 用 `FormVersionId + Hash/ETag` 缓存；版本变化重新获取，候选项按需分页，不缓存授权决定。
- [ ] 运行 uni-app test/typecheck、`build:h5`、`build:mp-weixin`、`build:mp-alipay` 和 H5 Playwright。

### Task 7: 建立跨端语义与性能门禁

- [ ] 后台、服务端、H5、微信构建产物和支付宝构建产物都读取同一 Golden Fixture；断言字段类型、必填、隐藏、只读、选项和值序列化一致。
- [ ] 建立 30 字段与 100 字段固定 Fixture；在相同 Node/pnpm/Release 环境记录 H5 minified/gzip/Brotli、初始/懒 Chunk 与微信/支付宝主包/分包字节。
- [ ] 在指定低端设备/模拟环境分别记录冷/热启动、首次可交互、首次校验和候选项加载 P50/P95；环境、预热、网络和原始结果写入 dated Verification。
- [ ] 首次基线通过后才把相对回归预算写入 `frontend-bundle-budgets.json`；Renderer 必须留在 subPackage，禁止仅提高告警阈值。
- [ ] 先运行 `pnpm test:integration:affected:plan -- --snapshot workflow-designer-form-runtime-20260830 --phase slice` 审查影响集，再运行 `pnpm test:slice -- --snapshot workflow-designer-form-runtime-20260830`、三目标构建、客户端审计和 `git diff --check`；完整客户端矩阵留给 main CI。

---

## Stop Conditions

- 第三方授权/许可无法归档、VForm3 与当前栈不兼容、CSP 需要 unsafe-eval、移动端必须引入 VForm3/Element Plus 或服务端不能拒绝危险 Draft 时停止。
- 任何实现产生第二套权威表单协议、长期兼容旧 FlowJson、让客户端决定字段权限或通过提高预算掩盖包体增长时停止。
- 微信/支付宝任一目标不能构建，不能把 H5 通过表述为 uni-app 三端完成。

## Completion Evidence

- 第三方来源与 Notices、Schema Golden、服务端编译、Vue 设计/运行、uni-app 三构建、H5 E2E、包体与渲染基线均有新鲜证据。
- Spec 的危险能力保持拒绝；`ui/admin-layui/**` 无新功能差异。
- 没有生产等价容量数据时 Verification 明确保留 `Capacity-not-verified`。
