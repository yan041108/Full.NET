# Workflow Native Form Designer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付不依赖 VForm3、符合严格 CSP 的 Workflow 表单定义管理与受限可视化编辑闭环。

**Architecture:** 服务端现有 `WorkflowFormSchema`、组件目录、编译器和版本是唯一权威边界。Vue 管理端通过生成的 OpenAPI Client 读取目录和表单，使用 Full.NET 自有纯 TypeScript Draft 状态机与静态 Vue 组件编辑，再把完整强类型 Draft 连同 revision 提交；客户端不保存第三方 JSON，也不提供脚本、HTML、CSS、远程资源或动态组件执行入口。

**Tech Stack:** Vue 3.5、TypeScript 6、Element Plus、Vitest、OpenAPI 生成客户端、ASP.NET Core 10、System.Text.Json、现有 Workflow Dapper 双库实现。

## Global Constraints

- 不新增前端或后端第三方依赖，不修改 `THIRD-PARTY-NOTICES`。
- 只使用服务端 `workflowGetFormComponentCatalog` 返回且同时 Designable/Publishable/Executable 的字段类型。
- Draft 只包含 `schemaVersion`、`adapterVersion`、Section/Field 稳定键、`required` 与目录允许的约束。
- 禁止 `eval`、`new Function`、脚本、HTML/iframe、CSS、远程 URL/Header/Body、未知属性和 `<component :is>`。
- 页面权限为 `workflow.forms.read`；创建、编辑和发布分别使用独立操作权限，缺少权限时对应 DOM 不创建。
- revision 冲突由服务端 `409 ProblemDetails` 权威处理；客户端不得静默覆盖。
- 本计划只完成表单设计器；Workflow 树形定义设计器在表单闭环通过后另行推进。

---

### Task 1: 暴露 Workflow 表单管理生成客户端

**Files:**
- Modify: `tests/openapi/client-openapi-normalization-contract.test.mjs`
- Modify: `contracts/openapi/fullnet-client-v1.openapi.json`（生成）
- Modify: `contracts/openapi/client-generation-manifest-v1.json`
- Modify: `packages/client-contracts/src/generated/*`（生成）

**Interfaces:**
- Produces: `workflowListForms`、`workflowGetForm`、`workflowCreateForm`、`workflowUpdateFormDraft`、`workflowPublishForm`、`workflowGetFormComponentCatalog` 及对应生成 DTO。

- [x] **Step 1: 写 OpenAPI RED**

在现有 Workflow 规范断言中加入上述六个 operationId，并断言 `/api/v1/workflow/forms/component-catalog` 与表单 CRUD 路径存在。

- [x] **Step 2: 运行 RED**

Run: `pnpm test:openapi`

Expected: 因当前 Client snapshot 尚无表单管理 Operation 而失败。

- [x] **Step 3: 生成规范与客户端**

Run: `pnpm openapi:client:snapshot`

Run: `pnpm openapi:client:generate`

把六个 operationId 登记到 manifest，`apiModule` 固定为 `ui/admin/src/api/workflow-forms.ts`、`generatedGroup` 固定为 `workflow-forms`、`status` 固定为 `generated`。

- [x] **Step 4: 运行 GREEN**

Run: `pnpm test:openapi`

Run: `pnpm --filter @fullnet/client-contracts test`

Run: `pnpm --filter @fullnet/client-contracts build`

- [x] **Step 5: 提交**

```powershell
git add tests/openapi contracts/openapi packages/client-contracts/src/generated
git commit -m "feat: expose workflow form management clients"
```

### Task 2: 建立安全 Draft 状态机

**Files:**
- Create: `packages/client-contracts/src/workflow-form-draft.ts`
- Create: `packages/client-contracts/tests/workflow-form-draft.test.ts`
- Modify: `packages/client-contracts/src/index.ts`

**Interfaces:**
- Consumes: `WorkflowFormSchema`、`WorkflowFormComponentCatalogResponse`。
- Produces: `createWorkflowFormDraft()`、`addWorkflowFormSection()`、`addWorkflowFormField()`、`updateWorkflowFormField()`、`removeWorkflowFormField()`；所有函数返回新对象，不修改输入。

- [x] **Step 1: 写 Draft RED**

测试必须证明：默认 Draft 含一个 `main` Section 和一个 `summary:text` 字段；只能添加目录中三态均为 true 的字段；重复/危险稳定键失败；choice 默认生成非空 `options`，money/decimal 默认生成合法 `scale`；更新约束会剔除目录未声明键；删除最后字段被拒绝；输入对象保持不变。

- [x] **Step 2: 运行 RED**

Run: `pnpm --filter @fullnet/client-contracts test -- workflow-form-draft.test.ts`

Expected: 因模块和导出不存在而失败。

- [x] **Step 3: 写最小实现**

```ts
export function createWorkflowFormDraft(): WorkflowFormSchema;
export function addWorkflowFormSection(schema: WorkflowFormSchema, sectionKey: string): WorkflowFormSchema;
export function addWorkflowFormField(
  schema: WorkflowFormSchema,
  sectionKey: string,
  fieldKey: string,
  fieldTypeKey: WorkflowFieldType,
  catalog: WorkflowFormComponentCatalogResponse
): WorkflowFormSchema;
export function updateWorkflowFormField(
  schema: WorkflowFormSchema,
  fieldKey: string,
  patch: Readonly<{ fieldKey?: string; required?: boolean; constraints?: Readonly<Record<string, unknown>> }>,
  catalog: WorkflowFormComponentCatalogResponse
): WorkflowFormSchema;
export function removeWorkflowFormField(schema: WorkflowFormSchema, fieldKey: string): WorkflowFormSchema;
```

无效操作统一抛出稳定客户端错误：`client.invalid_workflow_form_draft`。

- [x] **Step 4: 运行 GREEN**

Run: `pnpm --filter @fullnet/client-contracts test -- workflow-form-draft.test.ts`

Run: `pnpm --filter @fullnet/client-contracts build`

- [x] **Step 5: 提交**

```powershell
git add packages/client-contracts
git commit -m "feat: add safe workflow form draft state"
```

### Task 3: 实现静态表单设计器组件

**Files:**
- Create: `ui/admin/src/workflow/WorkflowFormDesigner.vue`
- Create: `ui/admin/src/workflow/WorkflowFormDesigner.test.ts`

**Interfaces:**
- Consumes props: `schema: WorkflowFormSchema`、`catalog: WorkflowFormComponentCatalogResponse`、`disabled: boolean`。
- Emits: `'update:schema': [schema: WorkflowFormSchema]`。

- [ ] **Step 1: 写组件 RED**

测试必须证明：只渲染目录允许类型；可以新增 Section/Field、修改稳定键/required/受控约束和删除字段；choice 选项使用普通文本列表；无脚本、HTML、CSS、URL 或动态组件入口；disabled 时不创建修改动作；键盘可操作并保持可见 label。

- [ ] **Step 2: 运行 RED**

Run: `pnpm --filter @fullnet/admin test -- WorkflowFormDesigner.test.ts`

Expected: 因组件不存在而失败。

- [ ] **Step 3: 写最小静态组件**

组件使用显式模板分支和 Task 2 状态函数；字段类型选择来自服务端目录，约束编辑器仅按 `constraintKeys` 显式显示。组件不执行 Schema 内容，不使用 `v-html` 或动态组件。

- [ ] **Step 4: 运行 GREEN**

Run: `pnpm --filter @fullnet/admin test -- WorkflowFormDesigner.test.ts`

Run: `pnpm --filter @fullnet/admin typecheck`

- [ ] **Step 5: 提交**

```powershell
git add ui/admin/src/workflow
git commit -m "feat: add native workflow form designer"
```

### Task 4: 交付 Workflow 表单管理页面

**Files:**
- Create: `ui/admin/src/api/workflow-forms.ts`
- Create: `ui/admin/src/api/workflow-forms.test.ts`
- Create: `ui/admin/src/views/WorkflowFormsView.vue`
- Create: `ui/admin/src/views/WorkflowFormsView.test.ts`
- Modify: `ui/admin/src/router/index.ts`
- Modify: `ui/admin/src/navigation/catalog.ts`
- Modify: `ui/admin/src/navigation/catalog.test.ts`
- Modify: `packages/admin-i18n/src/messages.ts`

**Interfaces:**
- Consumes: 六个生成 Operation、Task 2 Draft API、Task 3 Designer。
- Produces: `/workflow/forms` 页面；列表、创建、编辑 Draft、保存和发布闭环。

- [ ] **Step 1: 写页面 RED**

测试必须证明：`workflow.forms.read` 页面可加载；没有 create/update/publish 权限时对应按钮不进入 DOM；创建使用安全默认 Draft；编辑先加载目录和权威 Draft；保存发送 `expectedRevision + draft`；发布发送当前 revision；409 展示稳定 ProblemDetails 且不覆盖本地 Draft；成功后刷新权威对象。

- [ ] **Step 2: 运行 RED**

Run: `pnpm --filter @fullnet/admin test -- WorkflowFormsView.test.ts`

Expected: 因 API、页面和路由不存在而失败。

- [ ] **Step 3: 写最小页面实现**

所有 HTTP 只经 `ui/admin/src/api/workflow-forms.ts` 调用生成客户端。页面把服务端目录作为可编辑能力源；保存/发布失败保留 Drawer 和 Draft；成功后使用返回对象替换 revision。

- [ ] **Step 4: 运行 GREEN**

Run: `pnpm --filter @fullnet/admin test -- WorkflowFormsView.test.ts WorkflowFormDesigner.test.ts`

Run: `pnpm --filter @fullnet/admin typecheck`

Run: `pnpm --filter @fullnet/admin build`

- [ ] **Step 5: 提交**

```powershell
git add ui/admin packages/admin-i18n
git commit -m "feat: add workflow form management page"
```

### Task 5: 关闭真实协议、CSP 与影响集门禁

**Files:**
- Modify: `tests/e2e/admin-real-stack/tests/workflow-forms.spec.mjs`
- Create: `docs/verification/2026-08-30-workflow-native-form-designer.md`
- Modify: `eng/testing/test-matrix.json`（仅测试数量真实变化时）

**Interfaces:**
- Consumes: 完整表单管理页面与现有 SQL Server/MySQL Workflow API。
- Produces: 创建→编辑→保存→发布→读取冻结版本的真实闭环证据。

- [ ] **Step 1: 写真实栈 RED**

E2E 使用受控测试身份验证权限 DOM、创建安全表单、增加字段、保存、发布并读取版本；浏览器 console/CSP 不允许 `unsafe-eval`、远程资源、脚本或未知组件错误。

- [ ] **Step 2: 执行聚焦验证**

Run: `pnpm test:integration:affected:plan -- --snapshot workflow-native-form-designer-20260830 --phase slice`

Run: `pnpm test:slice -- --snapshot workflow-native-form-designer-20260830`

Run: `pnpm test:openapi`

Run: `pnpm test:governance`

Run: `pnpm audit:clients`

Run: `git diff --check`

- [ ] **Step 3: 记录验证**

验证文档记录 SQL Server/MySQL、OpenAPI、Vue Unit/typecheck/build、CSP、权限 DOM、包体增量和任何未验证项。没有生产等价容量证据时保留 `Capacity-not-verified`。

- [ ] **Step 4: 提交**

```powershell
git add tests/e2e/admin-real-stack docs/verification eng/testing/test-matrix.json
git commit -m "test: verify native workflow form designer"
```

## Stop Conditions

- 若实现需要动态代码、任意 HTML/CSS/URL、客户端权威授权、第二套表单协议或新增第三方运行时，立即停止。
- 若生成客户端不能表达现有强类型 API，先修复 OpenAPI 契约，不得手写重复 DTO 或直接 `fetch`。
- 若 SQL Server/MySQL 任一提供程序不能完成发布闭环，不得把单库结果标记为完成。
- 不得通过提高前端包体预算掩盖回归。
