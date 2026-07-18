# uni-app uni-ui Adoption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有 uni-app Vue 3 项目中引入官方 uni-ui，建立统一主题和三目标组件门禁，同时保留现有 HTTP、ProblemDetails 和多语言实现。

**Architecture:** 使用 `@dcloudio/uni-ui@1.5.12` npm 包与 easycom 自动组件解析，不复制源码。Full.NET 设计令牌映射到 `src/uni.scss`；组件只负责 UI，平台登录、网络、安全和语言状态继续由现有适配层负责。

**Tech Stack:** uni-app `3.0.0-5010520260709002`、Vue 3.4.21、TypeScript 5.9.3、Vite 5.4.21、Vitest 3.2.6、`@dcloudio/uni-ui@1.5.12`。

## Global Constraints

- uni-ui 是唯一默认基础 UI 库；不得同时引入原版 uView 2 或另一套完整主题体系。
- H5、`mp-weixin`、`mp-alipay` 必须使用同一业务页面并分别构建。
- 不修改 `node_modules` 或复制 uni-ui 源码；设计差异通过令牌和 Wrapper 处理。
- 现有 Vue I18n、语言原子提交、ProblemDetails 和 `uni.request` Adapter 不得回退。

---

### Task 1: 依赖、许可证与 easycom 契约

**Files:**
- Modify: `clients/uniapp/package.json`
- Modify: `clients/uniapp/src/pages.json`
- Modify: `pnpm-lock.yaml`
- Modify: `THIRD-PARTY-NOTICES`
- Modify: `clients/uniapp/tests/workspace-contract.test.ts`

**Interfaces:**
- Consumes: 当前 uni-app CLI 版本和 pnpm 工作区
- Produces: `uni-*` 到 `@dcloudio/uni-ui/lib/uni-$1/uni-$1.vue` 的唯一组件解析规则

- [ ] **Step 1: 写失败测试**

断言 package 精确依赖 `@dcloudio/uni-ui: 1.5.12`、pages easycom 规则存在、依赖中不含 `uview-ui`/`uview-plus`，第三方清单包含 Apache-2.0。

- [ ] **Step 2: 运行并确认缺少 uni-ui 时失败**

Run: `pnpm --filter @fullnet/uniapp test`

Expected: FAIL，指出 uni-ui 依赖或 easycom 规则缺失。

- [ ] **Step 3: 安装依赖并配置 easycom**

Run: `pnpm --filter @fullnet/uniapp add @dcloudio/uni-ui@1.5.12 --save-exact`

在 `pages.json` 增加 `^uni-(.*)` 精确映射；登记 Apache-2.0 和实际包版本。

- [ ] **Step 4: 运行契约测试**

Run: `pnpm --filter @fullnet/uniapp test`

Expected: PASS。

### Task 2: 建立 Full.NET uni-ui 主题映射

**Files:**
- Create: `clients/uniapp/src/uni.scss`
- Create: `clients/uniapp/src/styles/fullnet-uni-ui.scss`
- Create: `clients/uniapp/src/ui/fullnet-ui-contract.ts`
- Test: `clients/uniapp/tests/ui-theme-contract.test.ts`

**Interfaces:**
- Consumes: `packages/design-tokens/src/tokens.css` 中的语义颜色/间距
- Produces: uni-ui 主色、成功、警告、错误、文字、边框和圆角的稳定映射

- [ ] **Step 1: 写主题契约失败测试**
- [ ] **Step 2: 在 `uni.scss` 定义 `$uni-color-primary` 等标准变量，并在适配样式中使用 Full.NET 语义命名**
- [ ] **Step 3: 禁止业务页面直接覆盖 `.uni-*` 内部结构选择器**
- [ ] **Step 4: 运行单测和类型检查**

Run: `pnpm --filter @fullnet/uniapp test && pnpm --filter @fullnet/uniapp typecheck`

Expected: PASS。

### Task 3: 建立跨三目标组件冒烟页

**Files:**
- Create: `clients/uniapp/src/pages/ui/component-smoke.vue`
- Modify: `clients/uniapp/src/pages.json`
- Create: `clients/uniapp/tests/uni-ui-component-contract.test.ts`
- Modify: `tests/e2e/uniapp-h5/tests/localization.spec.mjs`

**Interfaces:**
- Consumes: uni-ui easycom、主题和 Vue I18n
- Produces: `uni-section`、`uni-list`、`uni-list-item`、`uni-forms`、`uni-easyinput`、`uni-popup` 的可构建样例

- [ ] **Step 1: 写组件清单和本地化文本失败测试**
- [ ] **Step 2: 实现只在 Development/Test 可导航的冒烟页**
- [ ] **Step 3: H5 E2E 验证中文/英文、键盘焦点、错误提示和 320 CSS px 布局**
- [ ] **Step 4: 分别构建三个目标**

Run: `pnpm --filter @fullnet/uniapp build:h5 && pnpm --filter @fullnet/uniapp build:mp-weixin && pnpm --filter @fullnet/uniapp build:mp-alipay`

Expected: 三个命令均退出 0，产物不含 uView。

### Task 4: 迁移第一个真实设置页面

**Files:**
- Modify: `clients/uniapp/src/pages/settings/locale.vue`
- Modify: `clients/uniapp/tests/locale-settings-model.test.ts`
- Modify: `tests/e2e/uniapp-h5/tests/localization.spec.mjs`

**Interfaces:**
- Consumes: 现有 Locale Settings Model 和 uni-ui 组件
- Produces: 使用 uni-ui 展示但仍保持成功后原子提交的语言设置页

- [ ] **Step 1: 增加保存失败、并发和禁用状态测试**
- [ ] **Step 2: 用 uni-ui 表单/列表/反馈组件替换视觉层，不改变 Model/API**
- [ ] **Step 3: 运行 96 项现有测试、新增测试和 H5 E2E**

Run: `pnpm --filter @fullnet/uniapp test && pnpm test:e2e:uniapp`

Expected: 全部 PASS，保存失败仍保留原语言。

### Task 5: 开发者工具、许可证与状态验收

**Files:**
- Modify: `docs/verification/uniapp-localization.md`
- Create: `docs/verification/uniapp-uni-ui.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`

**Interfaces:**
- Consumes: 三目标构建、H5 E2E 和开发者工具证据
- Produces: uni-ui 的 `Build-verified`/`Verified` 准确状态

- [ ] **Step 1: 执行依赖漏洞、许可证和包体积检查**
- [ ] **Step 2: 在微信/支付宝开发者工具分别验证组件样式、弹层、输入和语言切换**
- [ ] **Step 3: 没有真机证据时保持 `Build-verified`，不得标为 `Verified`**
- [ ] **Step 4: 更新路线图和验证记录**
