# Full.NET 双管理端国际化与可访问性 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Vue 与 Layui 两套管理端增加 `zh-CN/en-US` 无框架共享国际化契约，并建立可重复执行的 WCAG 2.2 AA、键盘、焦点与窄屏验收基线。

**Architecture:** `@fullnet/admin-i18n` 只共享消息键、纯文本翻译、语言解析和文档语言更新；Vue 与 Layui 各自维护响应式/DOM 适配，不共享 UI 组件源码。自动验收在现有双端 Playwright 项目中使用同一场景运行，人工 NVDA/强制颜色检查保持显式发布门禁。

**Tech Stack:** TypeScript 6、Vue 3、Pinia、Element Plus、原生 ES Modules、Layui 2、Vitest 4、Playwright 1.61、`@axe-core/playwright` 4.12.1。

## Global Constraints

- 首期只支持 `zh-CN` 与 `en-US`，默认 `zh-CN`，存储键固定为 `fullnet.admin.locale`。
- 两端只共享国际化契约，禁止共享 Vue/Layui UI 组件源码。
- 所有翻译只能作为纯文本或属性写入，禁止通过 `innerHTML` 注入翻译内容。
- 动态导航继续使用本地 `componentKey` 白名单；国际化不得把任意服务端字符串解释成可执行配置。
- Layui 端保持 clean-room 独立实现，禁止复制 layuiAdmin 源码、CSS 和产品资产。
- `@axe-core/playwright` 只作为开发依赖，不得进入最终发布物。
- 自动检查通过不等于真实辅助技术已验证；未实际执行 NVDA/强制颜色检查时保持明确待验状态。
- 所有新增手写源代码注释使用中文，只解释边界、不变量或风险。

---

### Task 1: 建立无框架国际化契约

**Files:**
- Create: `packages/admin-i18n/package.json`
- Create: `packages/admin-i18n/tsconfig.json`
- Create: `packages/admin-i18n/src/index.ts`
- Create: `packages/admin-i18n/src/locale.ts`
- Create: `packages/admin-i18n/src/messages.ts`
- Create: `packages/admin-i18n/tests/i18n.test.ts`
- Modify: `pnpm-lock.yaml`
- Test: `packages/admin-i18n/tests/i18n.test.ts`

**Interfaces:**
- Consumes: 标准 `Storage`、`Navigator.languages` 和 `Document` 接口；不依赖任何 UI 框架。
- Produces: `SupportedLocale`、`MessageKey`、`supportedLocales`、`localeStorageKey`、`resolveLocale`、`translate`、`applyDocumentLocale`。

- [ ] **Step 1: 创建包配置并写失败测试**

`package.json` 固定为私有工作区包：

```json
{
  "name": "@fullnet/admin-i18n",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "exports": {
    ".": {
      "types": "./src/index.ts",
      "import": "./src/index.ts"
    }
  },
  "scripts": {
    "build": "tsc -p tsconfig.json",
    "test": "vitest run"
  },
  "devDependencies": {
    "typescript": "6.0.3",
    "vitest": "4.1.10"
  }
}
```

`tsconfig.json` 只执行严格类型检查，浏览器构建由两个管理端的 Vite 完成：

```json
{
  "compilerOptions": {
    "target": "ES2024",
    "lib": ["ES2024", "DOM"],
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "strict": true,
    "noEmit": true,
    "skipLibCheck": true
  },
  "include": ["src/**/*.ts", "tests/**/*.ts"]
}
```

`tests/i18n.test.ts` 先声明契约：

```ts
import { describe, expect, it } from 'vitest';
import {
  applyDocumentLocale,
  localeStorageKey,
  messageKeys,
  messages,
  resolveLocale,
  translate
} from '../src';

describe('管理端国际化契约', () => {
  it('两种语言公开完全相同的消息键', () => {
    expect(Object.keys(messages['zh-CN']).sort()).toEqual([...messageKeys].sort());
    expect(Object.keys(messages['en-US']).sort()).toEqual([...messageKeys].sort());
  });

  it.each([
    ['en-US', ['zh-CN'], 'en-US'],
    [undefined, ['en-GB'], 'en-US'],
    [undefined, ['zh-Hans-CN'], 'zh-CN'],
    ['invalid', ['fr-FR'], 'zh-CN']
  ])('按保存值和浏览器语言解析 %s / %s', (saved, preferred, expected) => {
    expect(resolveLocale(saved, preferred)).toBe(expected);
  });

  it('使用命名参数生成纯文本', () => {
    expect(translate('en-US', 'tenant.activeCount', { count: 3 }))
      .toBe('3 active scopes');
  });

  it('更新文档语言和标题', () => {
    const target = {
      documentElement: { lang: '' },
      title: ''
    } as unknown as Document;
    applyDocumentLocale(target, 'en-US', 'Overview · Full.NET');
    expect(target.documentElement.lang).toBe('en-US');
    expect(target.title).toBe('Overview · Full.NET');
    expect(localeStorageKey).toBe('fullnet.admin.locale');
  });
});
```

- [ ] **Step 2: 运行测试并确认因契约尚未实现而失败**

Run: `pnpm --filter @fullnet/admin-i18n test`

Expected: FAIL，错误指向 `../src` 或缺少导出；不能是 Node/pnpm 环境错误。

- [ ] **Step 3: 实现最小语言解析与翻译内核**

`locale.ts` 使用精确白名单和稳定回退：

```ts
export const supportedLocales = ['zh-CN', 'en-US'] as const;
export type SupportedLocale = typeof supportedLocales[number];
export const localeStorageKey = 'fullnet.admin.locale';

export function resolveLocale(
  savedLocale: unknown,
  preferredLocales: readonly string[] = []
): SupportedLocale {
  if (savedLocale === 'zh-CN' || savedLocale === 'en-US') return savedLocale;
  for (const value of preferredLocales) {
    const language = value.toLowerCase();
    if (language.startsWith('en')) return 'en-US';
    if (language.startsWith('zh')) return 'zh-CN';
  }
  return 'zh-CN';
}

export function applyDocumentLocale(
  target: Document,
  locale: SupportedLocale,
  title: string
): void {
  target.documentElement.lang = locale;
  target.title = title;
}
```

`messages.ts` 用 `zh-CN` 对象推导 `MessageKey`，`en-US` 使用 `satisfies Record<MessageKey, string>` 阻止键漂移。消息集合至少覆盖以下稳定前缀，完整列出当前壳层全部可见文案：

```ts
export const zhCN = {
  'locale.label': '语言',
  'locale.zhCN': '简体中文',
  'locale.enUS': 'English',
  'a11y.skipToMain': '跳到主要内容',
  'session.restoring': '正在恢复安全会话',
  'auth.title': '管理员登录',
  'auth.username': '账号',
  'auth.password': '密码',
  'auth.submit': '进入控制台',
  'auth.submitting': '正在建立会话…',
  'shell.mainNavigation': '主导航',
  'shell.managementDomain': '管理域',
  'shell.logout': '退出登录',
  'navigation.overview.title': '工作台',
  'navigation.overview.caption': '平台运行概览',
  'navigation.tenantContext.title': '租户上下文',
  'navigation.tenantContext.caption': '进入租户或返回 Host',
  'overview.title': '早上好，系统管理员',
  'overview.probe': '检查会话',
  'tenant.title': '租户上下文',
  'tenant.enter': '进入租户',
  'tenant.returnHost': '返回 Host',
  'tenant.activeCount': '{count} 个活动范围',
  'status.back': '返回工作台',
  'status.403.title': '没有访问权限',
  'status.404.title': '页面没有找到',
  'status.500.title': '服务暂时不可用'
} as const;
```

`translate` 只进行命名占位符纯文本替换，缺少参数时保留占位符以便测试发现字典错误：

```ts
export type MessageKey = keyof typeof zhCN;
export type MessageParameters = Readonly<Record<string, string | number>>;

export function translate(
  locale: SupportedLocale,
  key: MessageKey,
  parameters: MessageParameters = {}
): string {
  return messages[locale][key].replace(/\{([a-zA-Z][a-zA-Z0-9]*)\}/g,
    (placeholder, name: string) => name in parameters
      ? String(parameters[name])
      : placeholder);
}
```

- [ ] **Step 4: 导出接口并验证包**

`src/index.ts` 只重新导出公开接口；运行：

```powershell
pnpm --filter @fullnet/admin-i18n test
pnpm --filter @fullnet/admin-i18n build
```

Expected: 国际化包测试全部 PASS，TypeScript 构建无错误。

- [ ] **Step 5: 提交共享契约**

```powershell
git add packages/admin-i18n pnpm-lock.yaml
git commit -m "feat: add shared admin i18n contract"
```

---

### Task 2: Vue 国际化适配与语义可访问性

**Files:**
- Create: `ui/admin/src/i18n/adminI18n.ts`
- Create: `ui/admin/src/i18n/adminI18n.test.ts`
- Modify: `ui/admin/package.json`
- Modify: `ui/admin/src/App.vue`
- Modify: `ui/admin/src/App.test.ts`
- Modify: `ui/admin/src/navigation/catalog.ts`
- Modify: `ui/admin/src/navigation/catalog.test.ts`
- Modify: `ui/admin/src/views/LoginView.vue`
- Modify: `ui/admin/src/views/LoginView.test.ts`
- Modify: `ui/admin/src/views/OverviewView.vue`
- Modify: `ui/admin/src/views/OverviewView.test.ts`
- Modify: `ui/admin/src/views/TenantContextView.vue`
- Modify: `ui/admin/src/views/TenantContextView.test.ts`
- Modify: `ui/admin/src/views/StatusView.vue`
- Modify: `ui/admin/src/styles/app.css`
- Modify: `pnpm-lock.yaml`

**Interfaces:**
- Consumes: Task 1 的 `SupportedLocale`、`MessageKey`、`resolveLocale`、`translate` 和 `applyDocumentLocale`。
- Produces: `createAdminI18n(options)`、`useAdminI18n()`，返回 `locale`、`t`、`setLocale`、`setPageTitle`。

- [ ] **Step 1: 写 Vue 语言状态失败测试**

测试使用内存 `Storage` 和 JSDOM，至少包含：

```ts
it('保存语言、更新文档并通知现有视图', () => {
  const i18n = createAdminI18n({
    storage,
    preferredLocales: ['zh-CN'],
    document
  });
  i18n.setLocale('en-US');
  i18n.setPageTitle('navigation.overview.title');
  expect(i18n.locale.value).toBe('en-US');
  expect(storage.getItem(localeStorageKey)).toBe('en-US');
  expect(document.documentElement.lang).toBe('en-US');
  expect(document.title).toBe('Overview · Full.NET');
});
```

组件失败测试断言：登录页存在可见语言标签；切换后标题与按钮变英文；已认证壳层存在跳转链接；导航链接有 `aria-current="page"`；路由视图 `h1` 可由程序聚焦。

- [ ] **Step 2: 运行 Vue 定向测试并确认失败**

Run: `pnpm --filter @fullnet/admin test -- adminI18n App LoginView navigation`

Expected: FAIL，原因是适配器、语言选择器或可访问性语义尚不存在。

- [ ] **Step 3: 实现 Vue 语言控制器**

控制器使用模块单例供组件共享，但保留工厂便于隔离测试：

```ts
export interface AdminI18nOptions {
  storage?: Pick<Storage, 'getItem' | 'setItem'>;
  preferredLocales?: readonly string[];
  document?: Document;
}

export function createAdminI18n(options: AdminI18nOptions = {}) {
  const targetDocument = options.document ?? document;
  const locale = ref(resolveLocale(
    safeRead(options.storage),
    options.preferredLocales ?? globalThis.navigator?.languages ?? []
  ));
  const t = (key: MessageKey, parameters?: MessageParameters) =>
    translate(locale.value, key, parameters);
  const setLocale = (value: SupportedLocale) => {
    locale.value = value;
    safeWrite(options.storage, value);
    applyDocumentLocale(targetDocument, value, targetDocument.title);
  };
  const setPageTitle = (key: MessageKey) =>
    applyDocumentLocale(targetDocument, locale.value, `${t(key)} · Full.NET`);
  return { locale: readonly(locale), t, setLocale, setPageTitle };
}
```

存储异常在 `safeRead/safeWrite` 内捕获；中文注释说明“偏好失败不得阻断认证”。

- [ ] **Step 4: 把 Vue 当前壳层全部可见文案接入消息键**

按以下精确职责修改：

```text
App.vue                 语言选择、跳转链接、壳层/导航/租户选择/退出文案、路由标题与焦点
LoginView.vue           登录说明、表单标签、占位符、提交状态、错误回退
OverviewView.vue        标题、指标、面板、活动、待办、异步结果
TenantContextView.vue   当前范围、Host/租户动作、数量、错误回退
StatusView.vue          返回链接；title/description 由本地状态消息键传入
navigation/catalog.ts   componentKey 到本地 titleKey/captionKey 的精确映射
```

`App.vue` 路由焦点使用 `nextTick` 后聚焦当前可见 `[data-route-heading]`，标题来自本地消息键。语言切换器必须有可见 `<label>`，路由链接使用 `aria-current`，图标统一 `aria-hidden="true"`。

- [ ] **Step 5: 增加 Vue 全局键盘、窄屏和减弱动画样式**

`app.css` 增加：

```css
.skip-link { position: fixed; z-index: 1000; inset: 8px auto auto 8px; transform: translateY(-160%); }
.skip-link:focus-visible { transform: translateY(0); }
:where(a, button, input, select):focus-visible { outline: 3px solid var(--fullnet-color-accent-bright); outline-offset: 3px; }
[data-route-heading] { scroll-margin-top: 24px; }
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { scroll-behavior: auto !important; animation-duration: .01ms !important; animation-iteration-count: 1 !important; transition-duration: .01ms !important; }
}
```

删除或替换当前所有 `outline: none`、`:focus` 和 `transition: all` 违规；输入框改用 `:focus-visible`，不禁止浏览器缩放。

- [ ] **Step 6: 运行 Vue 全量测试、类型检查和生产构建**

```powershell
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin typecheck
pnpm --filter @fullnet/admin build
```

Expected: Vue 测试全部 PASS，类型检查和 Vite 生产构建成功。

- [ ] **Step 7: 提交 Vue 适配**

```powershell
git add ui/admin pnpm-lock.yaml
git commit -m "feat: internationalize accessible vue admin shell"
```

---

### Task 3: Layui 国际化适配与语义可访问性

**Files:**
- Create: `ui/admin-layui/js/core/i18n.js`
- Create: `ui/admin-layui/tests/i18n.test.js`
- Modify: `ui/admin-layui/package.json`
- Modify: `ui/admin-layui/index.html`
- Modify: `ui/admin-layui/js/app.js`
- Modify: `ui/admin-layui/js/core/navigation.js`
- Modify: `ui/admin-layui/tests/app.test.js`
- Modify: `ui/admin-layui/tests/navigation.test.js`
- Modify: `ui/admin-layui/css/app.css`
- Modify: `pnpm-lock.yaml`

**Interfaces:**
- Consumes: Task 1 的无框架国际化公开接口。
- Produces: `createAdminI18n(options)` 和默认 `adminI18n`，快照为 `{ locale, t }`，支持 `setLocale`、`setPageTitle`、`applyBindings`、`subscribe`、`dispose`。

- [ ] **Step 1: 写 Layui 语言控制器与 DOM 绑定失败测试**

```js
it('切换语言后只写入文本并清理订阅', () => {
  document.body.innerHTML = `
    <label data-i18n="locale.label"></label>
    <button data-i18n="auth.submit"></button>`;
  const i18n = createAdminI18n({ storage, document, preferredLocales: ['zh-CN'] });
  i18n.applyBindings(document);
  i18n.setLocale('en-US');
  expect(document.querySelector('label').textContent).toBe('Language');
  expect(document.querySelector('button').textContent).toBe('Open console');
  expect(document.documentElement.lang).toBe('en-US');
  i18n.dispose();
});
```

`app.test.js` 同时断言语言切换不调用 `session.restore/login`、Hash 不改变、当前会话快照仍显示；`navigation.test.js` 断言本地标题键、`aria-current` 和安全文本渲染。

- [ ] **Step 2: 运行 Layui 定向测试并确认失败**

Run: `pnpm --filter @fullnet/admin-layui test -- i18n app navigation`

Expected: FAIL，原因是 `core/i18n.js`、绑定属性或可访问性语义缺失。

- [ ] **Step 3: 实现原生语言控制器**

`applyBindings` 只允许以下属性，不接受任意属性名：

```js
const bindings = [
  ['data-i18n', 'textContent'],
  ['data-i18n-aria-label', 'aria-label'],
  ['data-i18n-placeholder', 'placeholder'],
  ['data-i18n-title', 'title']
];
```

`setLocale` 保存合法语言、更新 `html lang`、重新应用静态绑定并通知订阅者。`subscribe` 立即返回当前快照，`dispose` 清空监听器且使后续通知无效；存储异常只回退到当前内存语言。

- [ ] **Step 4: 为 Layui HTML 建立完整声明式消息绑定**

`index.html` 必须包含：

```html
<a class="fn-skip-link" href="#main-content" data-i18n="a11y.skipToMain"></a>
<label for="admin-locale" data-i18n="locale.label"></label>
<select id="admin-locale" data-locale-select name="locale">
  <option value="zh-CN" data-i18n="locale.zhCN"></option>
  <option value="en-US" data-i18n="locale.enUS"></option>
</select>
```

登录控件补齐 `label for`、`name`、`autocomplete`、用户名 `spellcheck="false"`；当前主视图统一获得 `id="main-content"`，每个可见视图只有一个 `h1` 且带 `data-route-heading tabindex="-1"`；异步错误和状态容器使用 `aria-live`/`role="alert"`；技术标识增加 `translate="no"`。

- [ ] **Step 5: 让 app/navigation 的所有动态文案使用本地消息键**

`app.js` 在初始化时订阅 i18n，语言变化时调用：

```js
i18n.applyBindings(root);
renderSession(root, latestSnapshot, i18n.snapshot());
renderRoute(root, latestSnapshot, i18n.snapshot(), { focusHeading: false });
```

Hash 路由变化调用 `renderRoute(..., { focusHeading: true })`，在更新标题后聚焦当前可见 `h1`。`navigation.js` 的本地目录增加 `titleKey/captionKey`，忽略服务端 title/caption；活动链接设置 `aria-current="page"`。

- [ ] **Step 6: 修正 Layui 键盘、窄屏和减弱动画样式**

增加与 Vue 等价的跳转链接和 `:focus-visible` 规则；删除 `outline: none` 与 `transition: all`；在已有 `prefers-reduced-motion` 查询中覆盖所有非必要动画。320 CSS px 时主操作、语言选择和退出按钮必须可见，页面根元素不产生水平滚动。

- [ ] **Step 7: 运行 Layui 全量测试和生产构建**

```powershell
pnpm --filter @fullnet/admin-layui test
pnpm --filter @fullnet/admin-layui build
```

Expected: Layui 测试全部 PASS，Vite 生产构建成功，产物不包含 Vue/React 运行时。

- [ ] **Step 8: 提交 Layui 适配**

```powershell
git add ui/admin-layui pnpm-lock.yaml
git commit -m "feat: internationalize accessible layui admin shell"
```

---

### Task 4: 建立双端可访问性和国际化 E2E 门禁

**Files:**
- Modify: `tests/e2e/admin-parity/package.json`
- Create: `tests/e2e/admin-parity/tests/accessibility-i18n.spec.mjs`
- Modify: `tests/e2e/admin-parity/tests/shell-parity.spec.mjs`
- Modify: `pnpm-lock.yaml`

**Interfaces:**
- Consumes: Task 2/3 相同的可见标签、`data-locale-select`、`#main-content`、`[data-route-heading]` 和本地路由语义。
- Produces: 两个 Playwright project 共用的 axe、语言持久化、键盘、焦点、窄屏与减弱动画验收。

- [ ] **Step 1: 加入锁定的测试依赖**

Run: `pnpm --filter @fullnet/admin-parity-e2e add -D @axe-core/playwright@4.12.1`

Expected: 仅 `tests/e2e/admin-parity/package.json` 与 `pnpm-lock.yaml` 增加开发依赖；根生产依赖树不增加该包。

- [ ] **Step 2: 写双端失败 E2E**

核心扫描函数固定标签且禁止排除：

```js
import AxeBuilder from '@axe-core/playwright';

async function expectNoWcagViolations(page) {
  const result = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22a', 'wcag22aa'])
    .analyze();
  expect(result.violations, JSON.stringify(result.violations, null, 2)).toEqual([]);
}
```

测试场景必须逐一执行：匿名登录页、已认证工作台、租户上下文、403、404、500；切换 `en-US` 后断言 `html[lang="en-US"]`、英文标题、英文导航和刷新持久化；按 `Tab` 激活跳转链接后断言焦点进入 `#main-content`；导航切换后断言 `[data-route-heading]` 获得焦点；320×800 视口断言 `document.documentElement.scrollWidth <= clientWidth`；`reducedMotion: 'reduce'` 断言关键元素动画时长近似零。

- [ ] **Step 3: 运行 E2E 并确认它能发现真实缺口**

Run: `pnpm --filter @fullnet/admin-parity-e2e test -- accessibility-i18n.spec.mjs`

Expected: 若 Task 2/3 仍有语义、颜色、焦点或溢出缺口则 FAIL，并输出具体 axe rule/selector 或断言；不得先添加排除项。

- [ ] **Step 4: 最小修复 E2E 揭示的问题**

只修改产生违规的 Vue/Layui HTML、CSS 或焦点逻辑。颜色对比通过调整设计令牌或局部颜色解决；第三方 Element Plus 结构优先使用正确组件 API/语义，不关闭 axe 规则。

- [ ] **Step 5: 运行双端完整 E2E**

```powershell
pnpm --filter @fullnet/admin-parity-e2e test
```

Expected: 所有场景在 `vue-admin` 与 `layui-admin` 两个项目均 PASS，无 axe 排除。

- [ ] **Step 6: 提交自动验收**

```powershell
git add tests/e2e/admin-parity ui/admin ui/admin-layui pnpm-lock.yaml
git commit -m "test: enforce dual admin accessibility parity"
```

---

### Task 5: 同步 CI、文档和诚实状态

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `THIRD-PARTY-NOTICES`

**Interfaces:**
- Consumes: Task 1-4 的真实命令和测试结果。
- Produces: CI 门禁、开发者运行说明、C1 当前状态与开发/生产许可证边界。

- [ ] **Step 1: 先用检索测试确认文档仍是旧状态**

```powershell
rg -n "国际化入口与完整可访问性验收|@axe-core/playwright|admin-i18n|NVDA" README.md docs .github
```

Expected: 路线图仍把国际化列为未完成，且没有新的共享包/axe/NVDA 验收说明。

- [ ] **Step 2: 更新 CI 与开发命令**

CI 的现有 `pnpm test:workspace`、`pnpm test:clients`、`pnpm build:clients` 和 `pnpm test:e2e` 已覆盖新包及 E2E，不复制第二套命令。只在上传路径中保留 Playwright 报告；开发文档增加：

```powershell
pnpm --filter @fullnet/admin-i18n test
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin-layui test
pnpm --filter @fullnet/admin-parity-e2e test
```

- [ ] **Step 3: 更新路线图与许可证说明**

路线图把“国际化入口、自动 WCAG/键盘/焦点/窄屏基线”移入 C1 已完成项，把“Windows Edge + NVDA、强制颜色人工验收”保留在尚未完成项，因此 C1 仍为 `Implemented`，不提前标记为 `Verified`。许可证清单将 `@axe-core/playwright` 标为开发测试依赖、MPL-2.0、未进入发布物，并记录上游仓库 URL。

- [ ] **Step 4: 验证文档链接、状态与许可证边界**

```powershell
pnpm licenses list --prod --json > client-production-licenses.json
pnpm licenses list --json > client-development-licenses.json
rg -n "@axe-core/playwright" client-production-licenses.json client-development-licenses.json
python tests/skills/validate_project_skills.py
git diff --check
```

Expected: 生产清单不含 axe Playwright，开发清单包含；项目 Skill 契约 PASS；差异检查无输出。生成的许可证报告只用于验证，不提交。

- [ ] **Step 5: 提交文档与 CI**

```powershell
git add .github/workflows/ci.yml README.md docs
git commit -m "docs: record admin i18n accessibility baseline"
```

---

### Task 6: 全量验证、审查与项目演进复盘

**Files:**
- Modify only if evidence meets threshold: `rules/development-quality.md`
- Modify only if evidence meets threshold: `rules/rule-evolution.md`
- Modify only if evidence meets threshold: `rules/skill-evolution.md`
- Modify only if a proven workflow gap exists: `.agents/skills/fullnet-module-delivery/**`

**Interfaces:**
- Consumes: 当前分支全部提交和新鲜构建/测试输出。
- Produces: 可审查交付证据、规则复盘与 Skills 复盘结论。

- [ ] **Step 1: 运行完整客户端工作区验证**

```powershell
pnpm install --frozen-lockfile
pnpm test:workspace
pnpm test:clients
pnpm build:clients
pnpm test:e2e
```

Expected: 所有共享包、Vue、Layui 测试与生产构建通过；双端 E2E 全部通过。

- [ ] **Step 2: 运行后端回归验证**

```powershell
dotnet restore Full.NET.slnx --locked-mode
dotnet build Full.NET.slnx --configuration Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 116
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 4
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 9
```

Expected: 构建 0 errors；三套非容器测试达到数量门槛且无失败。若 Docker 可用，再运行 SQL Server/MySQL 集成测试 8 项；若不可用，交付中明确未验证原因。

- [ ] **Step 3: 按最新 Web Interface Guidelines 自审 UI**

重新读取 `https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md`，逐文件检查 `ui/admin` 和 `ui/admin-layui`。对每个真实问题建立失败测试后修复；若无问题，记录审查通过，不制造格式性修改。

- [ ] **Step 4: 执行规则与 Skills 复盘**

先用 `rg` 查重本次证据。并发退出竞态已由现有并发/安全规则覆盖，国际化和双端同步已由新 AGENTS 基线覆盖时不得添加近义规则。`fullnet-dual-admin-feature` 只有在首个列表/表单/权限/租户 CRUD 达到 `Verified` 后才升级；本次壳层工作最多更新真实命中次数，不提前创建 Skill。

- [ ] **Step 5: 最终 Git 和分支检查**

```powershell
git diff --check
git status --short
git log --oneline --decorate -8
git branch --show-current
git rev-list --left-right --count main...HEAD
```

Expected: 工作树清洁；当前分支为 `codex/session-race-c1-quality`；所有提交只包含本计划和前序审查修复；报告相对 `main` 的准确提交数量。

- [ ] **Step 6: 如复盘产生变更则独立提交**

```powershell
git add rules .agents/skills
git commit -m "chore: evolve project delivery safeguards"
```

仅在实际存在规则或 Skill 差异时执行；否则交付中写明“本次无新增规则/Skills 变化”及查重依据。
