# uni-app 三目标多语言基础 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `clients/uniapp` 建立可同时构建 H5、微信小程序和支付宝小程序的 Vue 3 + TypeScript 基础应用，并完整接入 Full.NET 的规范语言、账号偏好和 ProblemDetails 契约。

**Architecture:** 业务层只识别 `zh-CN/en-US`，`zh-Hans/en` 仅存在于 uni-app 平台适配器。活动语言控制器、HTTP 客户端和账号偏好端口均依赖可注入的窄接口，便于在无平台运行时的 Vitest 中验证；页面只消费这些端口，不自行解释 Token、语言别名或错误文本。首期使用一个原创的移动端语言设置页验证完整链路，不建立假登录、原生 App 或原生 tabBar。

**Tech Stack:** uni-app Vue 3、Vue 3.4.21、Vue I18n 9.1.9、Vite 5.2.8、TypeScript 5.9.3、Vitest 2.1.9、vue-tsc 2.2.12、Playwright 1.61.1、pnpm 10.26.0、Node.js 24。

## Global Constraints

- `@dcloudio/uni-app`、`@dcloudio/uni-components`、`@dcloudio/uni-h5`、`@dcloudio/uni-mp-weixin`、`@dcloudio/uni-mp-alipay`、`@dcloudio/uni-cli-shared` 与 `@dcloudio/vite-plugin-uni` 必须统一固定为 `3.0.0-5010520260709002`。
- `@dcloudio/types` 必须固定为 `3.4.31`；Vue 与 `@vue/compiler-sfc` 固定为 `3.4.21`；Vite 固定为 `5.2.8`；禁止 `latest`、星号或版本范围。
- Vue I18n 按 uni-app 官方国际化文档固定为 `9.1.9`，业务资源键在 `zh-CN/en-US` 两份消息中必须完全一致。
- 对外 API、存储的账号偏好和测试断言只使用 `zh-CN/en-US`；`zh-Hans/en` 只允许出现在 uni-app 平台适配层和平台资源文件名。
- 匿名切换立即提交本地语言；已认证切换只有 `PUT /api/v1/me/locale` 响应通过完整守卫后才原子提交语言与 `ProfileVersion`，失败保留旧语言、版本、会话与租户。
- `/api/v1/me` 是已保存偏好的唯一可信来源；JWT 与 TokenResponse 禁止增加语言偏好字段。
- HTTP 错误遵守标准状态码与 ProblemDetails；逻辑分支只读取稳定 `status/code/violations.code`，未知 code 回退服务端安全 `title` 并显示 `traceId`。
- 所有手写源代码注释使用中文并解释意图、边界或风险；代码标识符使用英文。
- 页面不得加载远程字体、图标、语言包或插件市场资产；首期无 tabBar，因此不存在运行时原生 tabBar 文案漂移。
- H5、微信和支付宝必须分别构建。缺少平台开发者工具时只能记录 `Build-verified`，禁止把构建成功写成开发者工具或真机验收通过。

---

## File Structure

| 文件 | 职责 |
|---|---|
| `clients/uniapp/src/i18n/locale-adapter.ts` | 规范语言、平台语言和别名的唯一映射边界 |
| `clients/uniapp/src/i18n/locale-controller.ts` | 设备语言、匿名持久化、账号快照和原子切换状态机 |
| `clients/uniapp/src/i18n/index.ts` | Vue I18n 实例及 uni-app 运行时装配 |
| `clients/uniapp/src/api/problem-details.ts` | ProblemDetails 守卫、稳定错误码和本地化展示模型 |
| `clients/uniapp/src/api/http.ts` | `uni.request` Promise 适配、逐请求语言和认证 Header |
| `clients/uniapp/src/api/locale-preference.ts` | `/api/v1/me` 快照守卫和语言偏好 PUT 端口 |
| `clients/uniapp/src/pages/settings/locale.vue` | 可运行的语言设置与错误回退样板页 |
| `clients/uniapp/tests/*.test.ts` | 无平台运行时的契约、状态机、HTTP 和资源完整性测试 |
| `tests/e2e/uniapp-h5/*` | H5 浏览器冒烟，不代替两个小程序的平台验收 |
| `docs/verification/uniapp-localization.md` | 工具版本、构建证据和未执行平台项 |

### Task 1: 固定工作区、许可证和构建入口

**Files:**
- Create: `clients/uniapp/package.json`
- Create: `clients/uniapp/tsconfig.json`
- Create: `clients/uniapp/vite.config.ts`
- Create: `clients/uniapp/src/env.d.ts`
- Create: `clients/uniapp/tests/workspace-contract.test.ts`
- Modify: `tests/client-workspace.test.mjs`
- Modify: `THIRD-PARTY-NOTICES`
- Modify: `pnpm-lock.yaml`

**Interfaces:**
- Produces: `@fullnet/uniapp` 包及 `test`、`typecheck`、`build:h5`、`build:mp-weixin`、`build:mp-alipay` 脚本。
- Consumes: 根 pnpm 工作区的 `clients/*` 和 Node 24/pnpm 10.26.0 基线。

- [ ] **Step 1: 写工作区 RED 契约**

在 `tests/client-workspace.test.mjs` 增加 `clients/uniapp/package.json` 读取与断言；在 `workspace-contract.test.ts` 解析自身 package.json，断言所有 DCloud 包版本完全一致、全部版本不含 `^`/`~`/`*`/`latest`、三目标脚本存在、`THIRD-PARTY-NOTICES` 包含 uni-app Apache-2.0 与 Vue I18n MIT。

- [ ] **Step 2: 确认测试先失败**

Run: `pnpm test:workspace`

Expected: FAIL，因为 `clients/uniapp/package.json` 尚不存在。

- [ ] **Step 3: 建立最小精确依赖清单**

`clients/uniapp/package.json` 至少包含：

```json
{
  "name": "@fullnet/uniapp",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "scripts": {
    "dev:h5": "uni -p h5 --host 127.0.0.1 --port 5175",
    "test": "vitest run",
    "typecheck": "vue-tsc --noEmit -p tsconfig.json",
    "build": "pnpm build:h5 && pnpm build:mp-weixin && pnpm build:mp-alipay",
    "build:h5": "uni build -p h5",
    "build:mp-weixin": "uni build -p mp-weixin",
    "build:mp-alipay": "uni build -p mp-alipay"
  },
  "dependencies": {
    "@dcloudio/uni-app": "3.0.0-5010520260709002",
    "@dcloudio/uni-components": "3.0.0-5010520260709002",
    "@dcloudio/uni-h5": "3.0.0-5010520260709002",
    "@dcloudio/uni-mp-alipay": "3.0.0-5010520260709002",
    "@dcloudio/uni-mp-weixin": "3.0.0-5010520260709002",
    "vue": "3.4.21",
    "vue-i18n": "9.1.9"
  },
  "devDependencies": {
    "@dcloudio/types": "3.4.31",
    "@dcloudio/uni-cli-shared": "3.0.0-5010520260709002",
    "@dcloudio/vite-plugin-uni": "3.0.0-5010520260709002",
    "@vue/compiler-sfc": "3.4.21",
    "typescript": "5.9.3",
    "vite": "5.2.8",
    "vitest": "2.1.9",
    "vue-tsc": "2.2.12"
  }
}
```

Vite 只注册官方 `uni()` 插件；tsconfig 包含 `@dcloudio/types`、Vitest 类型与 JSON 模块解析。通知文件写明 uni-app/DCloud Apache-2.0、Vue I18n MIT，并链接官方仓库。

- [ ] **Step 4: 安装并确认契约转绿**

Run: `pnpm install`

Run: `pnpm test:workspace && pnpm --filter @fullnet/uniapp test -- workspace-contract`

Expected: 两条命令退出 0，lockfile 只出现精确解析版本。

- [ ] **Step 5: 提交基础清单**

```powershell
git add clients/uniapp/package.json clients/uniapp/tsconfig.json clients/uniapp/vite.config.ts clients/uniapp/src/env.d.ts clients/uniapp/tests/workspace-contract.test.ts tests/client-workspace.test.mjs THIRD-PARTY-NOTICES pnpm-lock.yaml
git commit -m "build: scaffold uniapp workspace"
```

### Task 2: 交付规范语言适配器与原子状态机

**Files:**
- Create: `clients/uniapp/src/i18n/locale-adapter.ts`
- Create: `clients/uniapp/src/i18n/locale-controller.ts`
- Create: `clients/uniapp/src/i18n/messages.zh-CN.json`
- Create: `clients/uniapp/src/i18n/messages.en-US.json`
- Create: `clients/uniapp/tests/locale-adapter.test.ts`
- Create: `clients/uniapp/tests/locale-controller.test.ts`
- Create: `clients/uniapp/tests/resource-contract.test.ts`

**Interfaces:**
- Produces: `CanonicalLocale = 'zh-CN' | 'en-US'`、`toCanonicalLocale(value)`、`toUniLocale(locale)`、`createLocaleController(dependencies)`、`LocaleSnapshot`。
- Consumes: `LocaleRuntime.getLocale/setLocale/onLocaleChange`、`LocaleStorage.get/set` 与由账号端口传入的 `AccountLocaleSnapshot`。

- [ ] **Step 1: 写适配器和状态机 RED 测试**

必须覆盖：`zh/zh-CN/zh-Hans/zh_CN → zh-CN`、`en/en-US/en_US → en-US`、未知值回退 `zh-CN`；匿名成功立即持久化；平台事件规范化；`hydrateAccount` 只接受完整受支持快照；认证保存成功同时提交 locale/version；冲突、网络错误或畸形响应时两者都不改变。

```typescript
expect(toCanonicalLocale('zh-Hans')).toBe('zh-CN');
expect(toCanonicalLocale('en')).toBe('en-US');
expect(toUniLocale('zh-CN')).toBe('zh-Hans');
expect(toUniLocale('en-US')).toBe('en');
```

- [ ] **Step 2: 确认 RED**

Run: `pnpm --filter @fullnet/uniapp test -- locale-adapter locale-controller resource-contract`

Expected: FAIL，因为适配器、状态机和资源尚不存在。

- [ ] **Step 3: 实现唯一映射边界**

实现以下公开类型；别名表不得扩散到页面和 HTTP 层：

```typescript
export type CanonicalLocale = 'zh-CN' | 'en-US';
export type UniLocale = 'zh-Hans' | 'en';

export function toCanonicalLocale(value: unknown): CanonicalLocale;
export function toUniLocale(locale: CanonicalLocale): UniLocale;
export function isCanonicalLocale(value: unknown): value is CanonicalLocale;
```

- [ ] **Step 4: 实现可注入原子状态机**

```typescript
export interface AccountLocaleSnapshot {
  preferredLocale: CanonicalLocale;
  profileVersion: number;
}

export interface LocaleSnapshot extends AccountLocaleSnapshot {
  authenticated: boolean;
  saving: boolean;
}

export interface LocaleController {
  initialize(): LocaleSnapshot;
  subscribe(listener: (snapshot: LocaleSnapshot) => void): () => void;
  setAnonymousLocale(locale: CanonicalLocale): LocaleSnapshot;
  hydrateAccount(snapshot: AccountLocaleSnapshot): LocaleSnapshot;
  saveAuthenticatedLocale(
    locale: CanonicalLocale,
    persist: (request: AccountLocaleSnapshot) => Promise<AccountLocaleSnapshot>
  ): Promise<LocaleSnapshot>;
}
```

初始化优先本地明确选择，再使用设备语言；平台事件只改变匿名状态。认证保存期间拒绝并发重复提交，响应必须同时匹配请求语言且 `profileVersion` 严格递增，否则按失败处理并恢复原快照。

- [ ] **Step 5: 建立双语资源完整性门禁**

两份 JSON 至少覆盖应用名、设置页、保存中/成功/失败、稳定错误码 `localization.unsupported_locale`、`identity.profile_version_conflict`、常见验证错误与 traceId 标签。测试递归比较键集合、拒绝空字符串和 `TODO/TBD`。

- [ ] **Step 6: 转绿并提交**

Run: `pnpm --filter @fullnet/uniapp test -- locale-adapter locale-controller resource-contract`

Expected: 全部测试通过。

```powershell
git add clients/uniapp/src/i18n clients/uniapp/tests/locale-adapter.test.ts clients/uniapp/tests/locale-controller.test.ts clients/uniapp/tests/resource-contract.test.ts
git commit -m "feat: add uniapp locale state"
```

### Task 3: 交付标准 HTTP、ProblemDetails 与账号偏好端口

**Files:**
- Create: `clients/uniapp/src/api/problem-details.ts`
- Create: `clients/uniapp/src/api/http.ts`
- Create: `clients/uniapp/src/api/locale-preference.ts`
- Create: `clients/uniapp/tests/problem-details.test.ts`
- Create: `clients/uniapp/tests/http-locale.test.ts`
- Create: `clients/uniapp/tests/locale-preference.test.ts`

**Interfaces:**
- Produces: `createHttpClient`、`HttpProblem`、`toProblemPresentation`、`getCurrentProfile`、`saveLocalePreference`。
- Consumes: 每次请求时读取的 `getLocale()`、可选 `getAccessToken()`、`uni.request` 窄适配器和 Task 2 的账号快照。

- [ ] **Step 1: 写 HTTP 与错误 RED 测试**

覆盖：每次请求读取最新规范语言；调用方 Header 被保留但不能覆盖 `Accept-Language`；有 Token 时才写 `Authorization: Bearer`；2xx 返回数据；标准 ProblemDetails 保留 status/code/traceId/violations；非 JSON 失败生成稳定安全回退；已认证偏好 PUT body 只含 `preferredLocale/profileVersion`；`/me` 和 PUT 响应拒绝 Token 字段、缺字段、别名语言和非整数版本。

- [ ] **Step 2: 确认 RED**

Run: `pnpm --filter @fullnet/uniapp test -- problem-details http-locale locale-preference`

Expected: FAIL，因为 API 文件尚不存在。

- [ ] **Step 3: 实现 Promise HTTP 适配**

```typescript
export interface HttpClientDependencies {
  request: UniApp.Request;
  getLocale: () => CanonicalLocale;
  getAccessToken?: () => string | undefined;
}

export interface HttpClient {
  request<T>(options: {
    path: string;
    method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
    data?: unknown;
    headers?: Record<string, string>;
  }): Promise<T>;
}
```

HTTP 客户端逐次读取 locale/token，不缓存认证状态；响应状态不在 200–299 时抛出 `HttpProblem`，禁止统一包络或把错误改写成 HTTP 200。

- [ ] **Step 4: 实现稳定错误展示模型**

`toProblemPresentation(problem, translate)` 优先按 `violations[].code` 和结构化 arguments 生成字段消息，其次按稳定顶层 code 翻译；未知 code 使用安全服务端 title，始终把 traceId 作为独立可复制字段，业务代码不得比较 title/detail。

- [ ] **Step 5: 实现账号偏好守卫**

```typescript
export interface CurrentProfileLocale {
  preferredLocale: CanonicalLocale;
  profileVersion: number;
}

export function getCurrentProfile(http: HttpClient): Promise<CurrentProfileLocale>;
export function saveLocalePreference(
  http: HttpClient,
  request: CurrentProfileLocale
): Promise<CurrentProfileLocale>;
```

守卫只接收规范语言和非负安全整数版本；返回对象中出现 `accessToken`、`refreshToken` 或语言别名时拒绝整个快照，不能部分提交。

- [ ] **Step 6: 转绿并提交**

Run: `pnpm --filter @fullnet/uniapp test -- problem-details http-locale locale-preference`

Expected: 全部测试通过。

```powershell
git add clients/uniapp/src/api clients/uniapp/tests/problem-details.test.ts clients/uniapp/tests/http-locale.test.ts clients/uniapp/tests/locale-preference.test.ts
git commit -m "feat: add uniapp api localization"
```

### Task 4: 建立 Vue I18n 应用与原创设置页

**Files:**
- Create: `clients/uniapp/src/main.ts`
- Create: `clients/uniapp/src/App.vue`
- Create: `clients/uniapp/src/pages.json`
- Create: `clients/uniapp/src/manifest.json`
- Create: `clients/uniapp/src/locale/zh-Hans.json`
- Create: `clients/uniapp/src/locale/en.json`
- Create: `clients/uniapp/src/locale/uni-app.zh-Hans.json`
- Create: `clients/uniapp/src/locale/uni-app.en.json`
- Create: `clients/uniapp/src/i18n/index.ts`
- Create: `clients/uniapp/src/pages/settings/locale.vue`
- Create: `clients/uniapp/tests/app-config.test.ts`

**Interfaces:**
- Produces: `i18n`、`localeController`、`setActiveLocale` 与 H5/小程序入口页面。
- Consumes: Task 2 状态机、Task 3 账号偏好端口和 uni-app 生命周期。

- [ ] **Step 1: 写应用配置 RED 测试**

测试解析 pages/manifest/平台 locale JSON，断言启动页唯一指向 `pages/settings/locale`、默认语言为 `zh-Hans`、`%app.name%` 和 `%settings.title%` 键存在、无原生 tabBar、无远程 URL、所有平台资源双语同键。

- [ ] **Step 2: 确认 RED**

Run: `pnpm --filter @fullnet/uniapp test -- app-config`

Expected: FAIL，因为应用配置和页面尚不存在。

- [ ] **Step 3: 装配 Vue I18n 与平台生命周期**

使用 `createI18n({ legacy: false, locale: 'zh-CN', fallbackLocale: 'zh-CN', messages })`。启动时从控制器初始化；每次快照提交后同步 Vue I18n、`uni.setLocale(toUniLocale(locale))`、`uni.setNavigationBarTitle`，H5 额外同步 `document.documentElement.lang`。`uni.onLocaleChange` 只通过控制器入口更新，避免事件回环。

- [ ] **Step 4: 实现设置页**

视觉方向为“安静、可信的跨端控制面板”：深墨蓝背景、青绿色状态强调、紧凑但不拥挤的双选语言卡、明确的当前/待保存状态；不使用远程字体或图片。页面使用真实 `button`/`radio` 语义和可见焦点，支持窄屏、安全区、减少动画偏好；保存失败展示本地化错误与 traceId，不能清理会话或乐观改变语言。

页面只展示“当前为匿名模式”或由上层注入的认证状态，不实现假 Token、假账号或平台登录。无 tabBar 是明确的首期设计；动态标题统一调用公开 `uni.setNavigationBarTitle`。

- [ ] **Step 5: 转绿、类型检查并提交**

Run: `pnpm --filter @fullnet/uniapp test -- app-config`

Run: `pnpm --filter @fullnet/uniapp typecheck`

Expected: 测试和类型检查退出 0。

```powershell
git add clients/uniapp/src clients/uniapp/tests/app-config.test.ts
git commit -m "feat: add uniapp locale settings"
```

### Task 5: 三目标构建、H5 冒烟、CI 与如实状态文档

**Files:**
- Create: `tests/e2e/uniapp-h5/package.json`
- Create: `tests/e2e/uniapp-h5/playwright.config.mjs`
- Create: `tests/e2e/uniapp-h5/tests/localization.spec.mjs`
- Create: `docs/verification/uniapp-localization.md`
- Modify: `package.json`
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `docs/superpowers/specs/2026-07-17-full-stack-localization-design.md`
- Modify: `docs/superpowers/plans/2026-07-17-full-stack-localization.md`
- Modify: `pnpm-lock.yaml`

**Interfaces:**
- Produces: 可重复的三目标构建门禁、H5 浏览器冒烟和具备证据的 L3/C3 状态。
- Consumes: Task 1–4 完整应用。

- [ ] **Step 1: 写 H5 冒烟与 CI RED 契约**

Playwright 启动 `pnpm --filter @fullnet/uniapp dev:h5`，只运行 Chromium/Edge 一个 H5 项目。场景断言：中文启动、切换英文、刷新保持、`html lang=en-US`、英文导航标题/核心文案、匿名请求发送 `Accept-Language: en-US`、模拟认证保存失败时语言与会话视图保持原值、未知 ProblemDetails 显示 title 与 traceId。

根脚本增加 `test:e2e:uniapp`。CI 必须运行 uni-app 单测、类型检查、三目标构建和 H5 E2E，并上传 H5 Playwright 报告；不得声称 CI 替代微信/支付宝开发者工具。

- [ ] **Step 2: 确认新门禁先失败**

Run: `pnpm test:e2e:uniapp`

Expected: FAIL，因为 E2E 配置、脚本或完整交互尚未接入。

- [ ] **Step 3: 完成 H5 E2E 和 CI 接线**

`tests/e2e/uniapp-h5` 精确依赖 `@playwright/test: 1.61.1`，本地使用 Edge、CI 使用安装的 Chromium。CI 在客户端依赖安装后执行：

```yaml
- name: Verify uni-app targets
  run: |
    pnpm --filter @fullnet/uniapp test
    pnpm --filter @fullnet/uniapp typecheck
    pnpm --filter @fullnet/uniapp build:h5
    pnpm --filter @fullnet/uniapp build:mp-weixin
    pnpm --filter @fullnet/uniapp build:mp-alipay

- name: Verify uni-app H5
  run: pnpm test:e2e:uniapp
```

- [ ] **Step 4: 运行三目标和 H5 验证**

Run: `pnpm --filter @fullnet/uniapp test`

Run: `pnpm --filter @fullnet/uniapp typecheck`

Run: `pnpm --filter @fullnet/uniapp build:h5`

Run: `pnpm --filter @fullnet/uniapp build:mp-weixin`

Run: `pnpm --filter @fullnet/uniapp build:mp-alipay`

Run: `pnpm test:e2e:uniapp`

Expected: 所有命令退出 0；三份产物不存在远程语言资源。

- [ ] **Step 5: 记录工具与状态，不伪造平台验收**

`docs/verification/uniapp-localization.md` 逐项记录 Node、pnpm、DCloud、Vue I18n、构建命令、测试数量、产物路径和时间。若微信开发者工具与支付宝小程序开发者工具仍未安装，分别写明 `Not executed — required tool not installed`，L3/C3 状态更新为 `Implementing / Build-verified`，不能写 `Verified`。

README 与 getting-started 说明启动、测试、三目标构建、规范语言边界和后续开发者工具验收步骤；全栈设计与总计划只勾选实际完成的 Task 6 步骤。

- [ ] **Step 6: 全工作区回归和依赖审计**

Run: `pnpm test:workspace`

Run: `pnpm test:localization`

Run: `pnpm test:clients`

Run: `pnpm build:clients`

Run: `pnpm test:e2e`

Run: `pnpm audit --audit-level high`

Expected: 全部退出 0；许可证报告中没有未披露的生产依赖。

- [ ] **Step 7: 提交交付**

```powershell
git add clients/uniapp tests/e2e/uniapp-h5 package.json pnpm-lock.yaml .github/workflows/ci.yml README.md docs THIRD-PARTY-NOTICES tests/client-workspace.test.mjs
git commit -m "feat: add uniapp localization foundation"
```

### Task 6: 独立审查、规则复盘与交付门禁

**Files:**
- Modify only if evidence meets evolution threshold: `rules/*`、`.agents/skills/*`
- Review: all Task 1–5 files and commits

**Interfaces:**
- Produces: 独立规格审查、代码质量审查、规则/Skill 演进结论和最终新鲜验证证据。
- Consumes: 本计划全部验收条件。

- [ ] **Step 1: 独立规格审查**

逐项核对规范语言隔离、认证原子切换、ProblemDetails、无假登录、三目标独立构建、许可证和状态真实性。Critical/Important 问题必须修复后重审。

- [ ] **Step 2: 独立代码质量审查**

重点检查 uni-app 生命周期事件回环、并发保存、畸形服务端快照、Header 覆盖、H5 DOM 条件编译、微信/支付宝不支持的浏览器 API、中文注释与远程资产。Critical/Important 问题必须修复后重审。

- [ ] **Step 3: 执行规则与 Skill 演进复盘**

按 `rules/rule-evolution.md` 和 `rules/skill-evolution.md` 记录这轮是否出现重复且可泛化的遗漏。只有满足证据门槛才修改规则或项目 Skill；否则明确记录“不升级”的理由，禁止为单次问题堆叠规则。

- [ ] **Step 4: 最终新鲜验证**

再次运行 Task 5 Step 4 与 Step 6 全部命令，随后运行：

```powershell
git diff --check
git status --short --branch
git log --oneline -8
```

Expected: 测试、类型检查、三目标构建、双管理端回归与审计全部使用新鲜输出通过；仅微信/支付宝开发者工具冒烟保持明确的未执行状态。

## Self-Review

- Spec coverage: 规范语言、平台映射、设备事件、匿名/认证偏好、HTTP、ProblemDetails、页面/manifest、动态标题、三目标构建、H5 冒烟、许可证和状态真实性均有对应任务。
- Deliberate exclusions: 不实现平台登录、Token 安全存储、原生 tabBar、uni-app 原生 App、Flutter 或业务模块；这些能力没有用占位代码伪装完成。
- Type consistency: Task 2 的 `AccountLocaleSnapshot/LocaleSnapshot` 与 Task 3 的 `CurrentProfileLocale` 字段统一为 `preferredLocale/profileVersion`；HTTP 和页面只消费规范语言。
- Placeholder scan: 计划不包含 TBD、TODO、latest、通配版本或“后续补错误处理”等不可执行步骤。

