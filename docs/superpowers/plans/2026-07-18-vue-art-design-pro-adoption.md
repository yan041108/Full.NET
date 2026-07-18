# Vue Art Design Pro Adoption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Art Design Pro 的管理壳层、主题和通用交互安全地迁入 `ui/admin`，同时完整保留 Full.NET 已验证的认证、租户、权限、ProblemDetails、多语言和 Vue/Layui 对等契约。

**Architecture:** 固定 Art Design Pro 上游提交并执行选择性源码迁入，所有上游 UI 都位于 `ui/admin/src/framework/art-design/`，通过 Full.NET Adapter 消费现有 Session、Router、Navigation 和 Client Contracts。ECharts 作为单独审计的标准图表引擎纳入；富文本按独立 Tiptap 计划实施。禁止导入模板 Mock、axios 请求层、Token 持久化、任意动态组件路径和其内置 wangEditor。

**Tech Stack:** Vue 3.5.40、TypeScript 6.0.3、Vite 8.1.5、Element Plus 2.14.3、Pinia 4.0.2、Vue Router 5.2.0、Vitest 4.1.10、Playwright、ECharts 6.1.0、Art Design Pro commit `f3aaf58eec1a0e988f162352c33862327a484f95`。

## Global Constraints

- Art Design Pro 仅迁入 MIT 审计通过的壳层/主题/通用组件；保留版权和许可证。
- 不新增 axios、持久化 Token、wangEditor、xlsx、视频、二维码、拖拽等未命中模块的依赖；ECharts 只按模块化图表基线引入。
- `ui/admin/src/api/http.ts`、`auth/session.ts`、`navigation/catalog.ts` 和 `packages/client-contracts` 保持协议权威。
- Vue 变化必须继续通过 Layui 镜像契约和双端 E2E；不得降低 CSP、可访问性或本地化门禁。

---

### Task 1: 锁定上游来源和禁止导入边界

**Files:**
- Create: `docs/upstreams/art-design-pro.md`
- Create: `ui/admin/tests/art-design-boundary.test.mjs`
- Modify: `THIRD-PARTY-NOTICES`
- Modify: `ui/admin/package.json`

**Interfaces:**
- Consumes: Art Design Pro commit `f3aaf58eec1a0e988f162352c33862327a484f95`
- Produces: 可机器检查的来源清单和禁止依赖列表

- [ ] **Step 1: 写失败的来源/依赖边界测试**

测试读取 `docs/upstreams/art-design-pro.md` 和 `ui/admin/package.json`，要求固定 commit、MIT、导入清单存在，并断言默认依赖不包含 `axios`、`pinia-plugin-persistedstate`、`@wangeditor/editor`、`xlsx`、`xgplayer`、`crypto-js`；`echarts` 必须精确锁定为 `6.1.0`。

- [ ] **Step 2: 运行测试并确认因来源清单缺失而失败**

Run: `node --test ui/admin/tests/art-design-boundary.test.mjs`

Expected: FAIL，指出 `docs/upstreams/art-design-pro.md` 不存在。

- [ ] **Step 3: 建立来源清单**

清单必须记录仓库 URL、固定 commit、MIT LICENSE、每个原始路径/目标路径、修改摘要、排除资产和复核日期；`THIRD-PARTY-NOTICES` 添加实际进入发布物的 Art Design Pro MIT 声明。

- [ ] **Step 4: 运行边界测试**

Run: `node --test ui/admin/tests/art-design-boundary.test.mjs`

Expected: PASS，且禁止依赖全部不存在。

### Task 2: 在迁移前冻结 Full.NET 安全契约

**Files:**
- Modify: `ui/admin/src/auth/session.test.ts`
- Modify: `ui/admin/src/api/http.test.ts`
- Modify: `ui/admin/src/navigation/catalog.test.ts`
- Modify: `tests/e2e/admin-parity/tests/shell-parity.spec.mjs`

**Interfaces:**
- Consumes: 当前 Session/HTTP/Navigation 实现
- Produces: Art Design Pro 迁移不得突破的回归门禁

- [ ] **Step 1: 增加禁止 Web Storage Token、未知动态组件、Admin.NET 200 包络和模板 Mock 的断言**
- [ ] **Step 2: 增加登录、Refresh single-flight、租户切换、退出和语言偏好的 Vue/Layui 同场景断言**
- [ ] **Step 3: 运行现有 Vue 与双端测试**

Run: `pnpm --filter @fullnet/admin test && pnpm --filter @fullnet/admin-parity-e2e test`

Expected: PASS；测试数量增加后同步最小门槛或验证记录。

### Task 3: 迁入壳层并建立 Adapter

**Files:**
- Create: `ui/admin/src/framework/art-design/layout/ArtAdminShell.vue`
- Create: `ui/admin/src/framework/art-design/layout/ArtSidebar.vue`
- Create: `ui/admin/src/framework/art-design/layout/ArtTopBar.vue`
- Create: `ui/admin/src/framework/art-design/layout/ArtTabs.vue`
- Create: `ui/admin/src/framework/art-design/theme/art-theme.css`
- Create: `ui/admin/src/framework/art-design/adapters/fullNetShellAdapter.ts`
- Modify: `ui/admin/src/App.vue`
- Modify: `ui/admin/src/styles/app.css`
- Test: `ui/admin/src/framework/art-design/adapters/fullNetShellAdapter.test.ts`

**Interfaces:**
- Consumes: `useSession()`、本地 Navigation Catalog、`@fullnet/design-tokens`
- Produces: `FullNetShellAdapter`，只暴露展示所需的用户、租户、菜单、权限和退出动作

- [ ] **Step 1: 先写 Adapter 失败测试**

断言未知导航组件被拒绝、菜单不执行字符串路径、退出调用现有 Session、主题不读取 Token/Cookie。

- [ ] **Step 2: 实现最小 Adapter 和四个壳层组件**

所有认证/API 行为委托给现有模块；上游代码使用中文来源注释说明原始路径、commit 和 Full.NET 修改意图。

- [ ] **Step 3: 用 `ArtAdminShell` 替换 App 的视觉壳层**

保留现有 RouterView、登录页、状态页、租户上下文和错误边界，不一次迁入业务页面。

- [ ] **Step 4: 运行测试、类型检查和生产构建**

Run: `pnpm --filter @fullnet/admin test && pnpm --filter @fullnet/admin typecheck && pnpm --filter @fullnet/admin build`

Expected: 全部 PASS，生产包不存在 Mock API 和禁止依赖。

### Task 4: 建立 ECharts 标准图表层

**Files:**
- Modify: `ui/admin/package.json`
- Modify: `pnpm-lock.yaml`
- Create: `ui/admin/src/framework/art-design/charts/echarts.ts`
- Create: `ui/admin/src/framework/art-design/charts/FullNetChart.vue`
- Create: `ui/admin/src/framework/art-design/charts/fullNetChartTheme.ts`
- Test: `ui/admin/src/framework/art-design/charts/FullNetChart.test.ts`
- Modify: `THIRD-PARTY-NOTICES`

**Interfaces:**
- Consumes: `@fullnet/design-tokens`、当前 Locale 和页面数据
- Produces: `FullNetChart`，输入纯数据/声明式 Option，不执行服务端函数字符串

- [ ] **Step 1: 写失败测试，禁止完整包副作用导入并要求替代表格/摘要**
- [ ] **Step 2: 精确安装 `echarts@6.1.0`，只从 `echarts/core` 注册 Bar/Line/Pie、Title/Tooltip/Grid/Legend/Dataset 和 CanvasRenderer**
- [ ] **Step 3: 实现异步组件、ResizeObserver、主题、语言、减弱动画和空/错状态**
- [ ] **Step 4: 为 10k 点折线和首页组合图记录 gzip chunk 与交互基线**
- [ ] **Step 5: 运行单测、类型检查、构建、许可证和产物依赖检查**

Run: `pnpm --filter @fullnet/admin test && pnpm --filter @fullnet/admin build && pnpm audit:clients`

Expected: PASS；ECharts 只出现在使用图表的异步 chunk，Apache-2.0/NOTICE 已登记。

### Task 5: 多语言、可访问性和双端对等

**Files:**
- Modify: `packages/admin-i18n/src/messages.ts`
- Modify: `ui/admin/src/i18n/elementLocale.ts`
- Modify: `tests/e2e/admin-parity/tests/accessibility-i18n.spec.mjs`
- Modify: `tests/e2e/admin-parity/tests/shell-parity.spec.mjs`

**Interfaces:**
- Consumes: `zh-CN/en-US` 治理清单和 Art 壳层
- Produces: 两种语言、键盘、320 CSS px 和减弱动画均可验证的主管理端

- [ ] **Step 1: 为新增壳层文案写资源缺键测试**
- [ ] **Step 2: 补齐中英文资源，禁止在组件中硬编码用户可见文本**
- [ ] **Step 3: 增加侧栏、标签页、主题和移动抽屉的键盘/焦点/axe E2E**
- [ ] **Step 4: 运行共享资源、Vue、Layui 和双端 E2E**

Run: `pnpm test:localization && pnpm test:clients && pnpm test:e2e`

Expected: 全部 PASS，无 axe 排除项。

### Task 6: 完成迁移验收和状态更新

**Files:**
- Modify: `README.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `docs/verification/admin-art-design-pro.md`

**Interfaces:**
- Consumes: Task 1-5 的构建、E2E、许可证和体积证据
- Produces: 可审查的迁移完成记录

- [ ] **Step 1: 比较迁移前后 gzip 产物并列出新增依赖/资产**
- [ ] **Step 2: 执行 `pnpm audit:clients` 和生产资产来源检查**
- [ ] **Step 3: 记录未执行的 NVDA/强制颜色人工项，禁止提前标记 `Verified`**
- [ ] **Step 4: 更新状态矩阵，提交聚焦迁移提交**
