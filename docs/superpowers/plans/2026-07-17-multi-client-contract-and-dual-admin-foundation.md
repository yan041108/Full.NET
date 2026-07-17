# Multi-client Contract and Dual Admin Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立多客户端公共契约底座，以及 Vue/Layui 两套功能对等管理端的可运行壳层和双端验收门禁。

**Architecture:** 后端 OpenAPI 是 API 单一事实来源；共享包只包含协议解析、稳定错误码和设计令牌，不包含 UI 组件。Vue 与 Layui 分别实现会话、租户、权限和导航适配，使用同一组 Playwright 业务场景验证功能对等。

**Tech Stack:** Node.js 24、pnpm 10、TypeScript 7、Vue 3、Vite 8、Element Plus 2、Layui 2.13.8、Vitest、Playwright、ASP.NET Core OpenAPI。

## Global Constraints

- Vue 管理端固定目录为 `ui/admin`，Layui 管理端固定目录为 `ui/admin-layui`。
- Vue 与 Layui 覆盖相同后台功能；单端完成不能标记管理端功能 `Implemented` 或 `Verified`。
- Layui 使用 HTML、CSS 和原生 JavaScript，禁止引入 Vue、React 或另一套 SPA 运行时。
- 所有客户端默认使用 `/api/v1`、标准 HTTP 状态码和 ProblemDetails；Admin.NET 包络只能由兼容适配器显式启用。
- 客户端逻辑只能依据稳定错误 `code` 分支，不得依据本地化消息文本分支。
- Access Token 保存在浏览器内存；Refresh Token 使用 `HttpOnly + Secure + SameSite` Cookie，并启用 CSRF 防护。
- OpenAPI、公共模型或权限码变更必须运行 Vue/Layui 双端契约和 E2E 检查。
- Layui 2.13.8 静态资源必须本地分发并保留 MIT 许可证，生产环境不依赖公共 CDN。
- `layuiAdmin` 只作为公开页面的布局和交互参考；未经允许公开源码并以 MIT 再发布的明确书面授权，禁止下载、复制或提交其源码、CSS、图片、字体和模板。
- Layui 后台采用洁净室独立设计：重新定义设计令牌、布局尺寸、图标组合、DOM、CSS 类名和 JavaScript 模块，不使用截图切图或像素级复刻作为实现方法。
- 所有手写源代码注释和 JSDoc 使用中文，解释意图、边界或风险，不逐行复述代码。

---

## Scope Boundary

本计划只实现 C0 的浏览器公共契约部分和 C1 双管理端壳层。Identity、Tenancy、Organization 的完整 CRUD 需要在后端契约稳定后建立第一个双管理端业务切片计划。uni-app 与 Flutter 是独立子系统，分别在执行 C3/C4 前建立独立实现计划，并在各自计划中补齐 `uni.request` 与 Dart 契约验证，不能塞入本计划造成不可验收的大提交。

## File Map

| 路径 | 职责 |
|---|---|
| `package.json` | 客户端工作区统一命令和工具版本 |
| `pnpm-workspace.yaml` | Vue、Layui、公共包和 E2E 工作区 |
| `packages/client-contracts/` | ProblemDetails、分页和 HTTP 契约解析 |
| `packages/design-tokens/` | 跨客户端语义令牌，不含组件实现 |
| `ui/admin/` | Vue 管理端壳层 |
| `ui/admin-layui/` | Layui 原生 JS/HTML 管理端壳层 |
| `tests/e2e/admin-parity/` | 双管理端共享业务场景和分别执行的 E2E |
| `.github/workflows/ci.yml` | 客户端安装、构建、测试和契约门禁 |

### Task 1: Create the client workspace and license baseline

**Files:**
- Create: `.nvmrc`
- Create: `package.json`
- Create: `pnpm-workspace.yaml`
- Create: `packages/client-contracts/package.json`
- Create: `packages/design-tokens/package.json`
- Modify: `.gitignore`
- Modify: `THIRD-PARTY-NOTICES`

**Interfaces:**
- Consumes: 仓库现有 .NET 根目录和 MIT 发布边界。
- Produces: `pnpm install` 可还原的工作区，以及后续任务使用的 `@fullnet/client-contracts`、`@fullnet/design-tokens` 包名。

- [ ] **Step 1: Write the workspace contract check**

Create `tests/client-workspace.test.mjs`:

```javascript
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const workspace = await readFile('pnpm-workspace.yaml', 'utf8');
const notices = await readFile('THIRD-PARTY-NOTICES', 'utf8');

assert.match(workspace, /ui\/\*/);
assert.match(workspace, /clients\/\*/);
assert.match(workspace, /packages\/\*/);
assert.match(notices, /Layui/i);
assert.match(notices, /MIT/i);
```

- [ ] **Step 2: Run the check and verify it fails**

Run: `node tests/client-workspace.test.mjs`

Expected: FAIL because `pnpm-workspace.yaml` and the Layui notice do not exist.

- [ ] **Step 3: Create the minimal workspace**

Create `.nvmrc` containing `24`, and create `pnpm-workspace.yaml`:

```yaml
packages:
  - "packages/*"
  - "ui/*"
  - "clients/*"
  - "tests/e2e/*"
```

Create root `package.json`:

```json
{
  "name": "fullnet-clients",
  "private": true,
  "packageManager": "pnpm@10.13.1",
  "engines": {
    "node": ">=24 <25"
  },
  "scripts": {
    "test:workspace": "node tests/client-workspace.test.mjs",
    "test:clients": "pnpm --recursive --if-present test",
    "build:clients": "pnpm --recursive --if-present build"
  }
}
```

Create the two package manifests with `private: true`, ESM module type and no runtime dependencies. Append `node_modules/`, `dist/` and Playwright output to `.gitignore`. Add Layui 2.13.8, MIT, official project URL and redistributed asset path to `THIRD-PARTY-NOTICES`.

- [ ] **Step 4: Install and verify the workspace**

Run: `corepack enable`

Run: `pnpm install`

Run: `pnpm test:workspace`

Expected: PASS and a committed `pnpm-lock.yaml` is produced.

- [ ] **Step 5: Commit**

```bash
git add .nvmrc package.json pnpm-workspace.yaml pnpm-lock.yaml packages tests/client-workspace.test.mjs .gitignore THIRD-PARTY-NOTICES
git commit -m "build: add client workspace baseline"
```

### Task 2: Implement the shared ProblemDetails contract

**Files:**
- Create: `packages/client-contracts/tsconfig.json`
- Create: `packages/client-contracts/src/problem-details.ts`
- Create: `packages/client-contracts/src/index.ts`
- Test: `packages/client-contracts/tests/problem-details.test.ts`

**Interfaces:**
- Consumes: RFC 9457 ProblemDetails plus Full.NET extensions `code`, `traceId`, `errors`.
- Produces: `FullNetProblemDetails`, `isFullNetProblemDetails(value)` and `readProblemDetails(response)` for both browser clients.

- [ ] **Step 1: Write failing parsing tests**

```typescript
import { describe, expect, it } from 'vitest';
import { isFullNetProblemDetails, readProblemDetails } from '../src/problem-details';

describe('FullNet ProblemDetails', () => {
  it('accepts a stable Full.NET error contract', () => {
    expect(isFullNetProblemDetails({
      type: 'https://full.net/errors/validation.failed',
      status: 400,
      code: 'validation.failed',
      traceId: 'trace-1',
      errors: { name: ['名称不能为空'] }
    })).toBe(true);
  });

  it('falls back safely when a proxy returns non-JSON content', async () => {
    const response = new Response('<html>bad gateway</html>', {
      status: 502,
      headers: { 'content-type': 'text/html' }
    });

    await expect(readProblemDetails(response)).resolves.toMatchObject({
      status: 502,
      code: 'http.unexpected_response'
    });
  });
});
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `pnpm --filter @fullnet/client-contracts test`

Expected: FAIL because the exported contract does not exist.

- [ ] **Step 3: Implement the contract parser**

```typescript
export interface FullNetProblemDetails {
  type?: string;
  title?: string;
  status: number;
  detail?: string;
  instance?: string;
  code: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

/** 判断未知响应是否满足 Full.NET 稳定错误契约。 */
export function isFullNetProblemDetails(value: unknown): value is FullNetProblemDetails {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Record<string, unknown>;
  return typeof candidate.status === 'number' && typeof candidate.code === 'string';
}

/** 读取错误响应；网关或代理返回非 JSON 时构造可追踪的安全错误。 */
export async function readProblemDetails(response: Response): Promise<FullNetProblemDetails> {
  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('application/problem+json') || contentType.includes('application/json')) {
    const value: unknown = await response.clone().json();
    if (isFullNetProblemDetails(value)) return value;
  }

  return {
    status: response.status,
    code: 'http.unexpected_response',
    title: response.statusText || '请求失败'
  };
}
```

Export the three symbols from `src/index.ts`. Configure the package to run Vitest and emit ESM declarations.

- [ ] **Step 4: Verify tests and type output**

Run: `pnpm --filter @fullnet/client-contracts test`

Run: `pnpm --filter @fullnet/client-contracts build`

Expected: all tests PASS and `dist/index.d.ts` exists.

- [ ] **Step 5: Commit**

```bash
git add packages/client-contracts pnpm-lock.yaml
git commit -m "feat: add shared client error contract"
```

### Task 3: Establish the Vue admin shell

**Files:**
- Create: `ui/admin/package.json`
- Create: `ui/admin/index.html`
- Create: `ui/admin/src/main.ts`
- Create: `ui/admin/src/App.vue`
- Create: `ui/admin/src/api/http.ts`
- Create: `ui/admin/src/router/index.ts`
- Test: `ui/admin/src/api/http.test.ts`

**Interfaces:**
- Consumes: `readProblemDetails(response)` from `@fullnet/client-contracts`.
- Produces: `request<T>(path, init, signal)` and routes `/`, `/403`, `/404`, `/500`.

- [ ] **Step 1: Write the failing HTTP adapter test**

```typescript
import { afterEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';

afterEach(() => vi.unstubAllGlobals());

describe('Vue HTTP adapter', () => {
  it('throws the stable ProblemDetails code', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 403,
      code: 'authorization.denied',
      traceId: 'trace-vue'
    }), { status: 403, headers: { 'content-type': 'application/problem+json' } })));

    await expect(request('/api/v1/me')).rejects.toMatchObject({
      code: 'authorization.denied',
      traceId: 'trace-vue'
    });
  });
});
```

- [ ] **Step 2: Run the test and verify it fails**

Run: `pnpm --filter @fullnet/admin test -- http.test.ts`

Expected: FAIL because `request` is not implemented.

- [ ] **Step 3: Implement the minimal typed adapter**

```typescript
import { readProblemDetails } from '@fullnet/client-contracts';

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '';

/** 调用 Full.NET 标准 API，并保留取消和稳定错误码。 */
export async function request<T>(
  path: string,
  init: RequestInit = {},
  signal?: AbortSignal
): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    credentials: 'include',
    signal,
    headers: { accept: 'application/json', ...init.headers }
  });

  if (!response.ok) throw await readProblemDetails(response);
  if (response.status === 204) return undefined as T;
  return await response.json() as T;
}
```

Create a minimal Element Plus layout with shell navigation items and the four required error routes. Do not add business modules in this task.

- [ ] **Step 4: Verify unit test, type check and production build**

Run: `pnpm --filter @fullnet/admin test`

Run: `pnpm --filter @fullnet/admin typecheck`

Run: `pnpm --filter @fullnet/admin build`

Expected: PASS, and `ui/admin/dist/index.html` exists.

- [ ] **Step 5: Commit**

```bash
git add ui/admin pnpm-lock.yaml
git commit -m "feat: add Vue admin shell"
```

### Task 4: Establish the Layui admin shell without an SPA runtime

**Files:**
- Create: `ui/admin-layui/package.json`
- Create: `ui/admin-layui/index.html`
- Create: `ui/admin-layui/css/app.css`
- Create: `ui/admin-layui/css/tokens.css`
- Create: `ui/admin-layui/js/core/http.js`
- Create: `ui/admin-layui/js/core/router.js`
- Create: `ui/admin-layui/js/app.js`
- Create: `ui/admin-layui/scripts/copy-layui.mjs`
- Test: `ui/admin-layui/tests/http.test.js`

**Interfaces:**
- Consumes: the same ProblemDetails shape used by Vue.
- Produces: `request(path, init, signal)`, hash routes, Layui shell and static `dist/` output with local Layui assets.

- [ ] **Step 1: Write the failing Layui adapter test**

```javascript
import { afterEach, describe, expect, it, vi } from 'vitest';
import { request } from '../js/core/http.js';

afterEach(() => vi.unstubAllGlobals());

describe('Layui HTTP adapter', () => {
  it('preserves the same stable error contract as Vue', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 403,
      code: 'authorization.denied',
      traceId: 'trace-layui'
    }), { status: 403, headers: { 'content-type': 'application/problem+json' } })));

    await expect(request('/api/v1/me')).rejects.toMatchObject({
      code: 'authorization.denied',
      traceId: 'trace-layui'
    });
  });
});
```

- [ ] **Step 2: Run the test and verify it fails**

Run: `pnpm --filter @fullnet/admin-layui test`

Expected: FAIL because the adapter does not exist.

- [ ] **Step 3: Implement the native JavaScript adapter and shell**

```javascript
const apiBaseUrl = globalThis.FULLNET_CONFIG?.apiBaseUrl ?? '';

/** 解析 Full.NET 标准错误；禁止根据提示文本决定业务分支。 */
async function readProblemDetails(response) {
  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('json')) {
    const value = await response.clone().json();
    if (typeof value?.status === 'number' && typeof value?.code === 'string') return value;
  }
  return { status: response.status, code: 'http.unexpected_response', title: response.statusText };
}

/** 调用标准 API；生产环境依靠 HttpOnly Cookie 刷新会话。 */
export async function request(path, init = {}, signal) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    credentials: 'include',
    signal,
    headers: { accept: 'application/json', ...init.headers }
  });
  if (!response.ok) throw await readProblemDetails(response);
  if (response.status === 204) return undefined;
  return await response.json();
}
```

Use Layui layout/menu modules in `app.js`, keep business code out of `index.html`, and implement hash routes for `/`, `/403`, `/404`, `/500`. Define original `--fullnet-*` color, spacing, typography and layout variables in `tokens.css`; do not paste declarations from layuiAdmin. `copy-layui.mjs` copies only `layui.css`, `layui.js`, fonts and required images from the pinned MIT Layui npm package into `dist/vendor/layui/`.

- [ ] **Step 4: Verify tests, static build and runtime dependency boundary**

Run: `pnpm --filter @fullnet/admin-layui test`

Run: `pnpm --filter @fullnet/admin-layui build`

Run: `rg -n "vue|react" ui/admin-layui/package.json ui/admin-layui/js`

Expected: tests PASS, `dist/index.html` and local Layui assets exist, and `rg` returns no SPA runtime dependency.

- [ ] **Step 5: Commit**

```bash
git add ui/admin-layui pnpm-lock.yaml THIRD-PARTY-NOTICES
git commit -m "feat: add Layui admin shell"
```

### Task 5: Add shared dual-admin parity scenarios

**Files:**
- Create: `tests/e2e/admin-parity/package.json`
- Create: `tests/e2e/admin-parity/playwright.config.ts`
- Create: `tests/e2e/admin-parity/fixtures/admin-target.ts`
- Test: `tests/e2e/admin-parity/specs/shell-parity.spec.ts`

**Interfaces:**
- Consumes: Vue dev server on port 5173 and Layui dev server on port 5174.
- Produces: one scenario suite executed once per `vue` and `layui` project.

- [ ] **Step 1: Write the failing parity scenario**

```typescript
import { expect, test } from '@playwright/test';

test('shell exposes trace id for a standard authorization error', async ({ page }) => {
  await page.route('**/api/v1/me', route => route.fulfill({
    status: 403,
    contentType: 'application/problem+json',
    body: JSON.stringify({
      status: 403,
      code: 'authorization.denied',
      traceId: 'trace-parity'
    })
  }));

  await page.goto('/#/');
  await page.getByTestId('load-current-user').click();
  await expect(page.getByTestId('error-code')).toHaveText('authorization.denied');
  await expect(page.getByTestId('trace-id')).toHaveText('trace-parity');
});
```

- [ ] **Step 2: Run both projects and verify the scenario fails**

Run: `pnpm --filter @fullnet/admin-parity-e2e test`

Expected: FAIL for both `vue` and `layui` because the stable test hooks are missing.

- [ ] **Step 3: Add the minimal testable shell behavior**

Add the same semantic `data-testid` hooks to both shells. Do not share Vue/Layui component code; share only the scenario name, API response and assertions. Configure Playwright projects with base URLs `http://127.0.0.1:5173` and `http://127.0.0.1:5174`.

- [ ] **Step 4: Run parity and accessibility smoke tests**

Run: `pnpm --filter @fullnet/admin-parity-e2e test`

Expected: the scenario PASSes once for Vue and once for Layui.

- [ ] **Step 5: Commit**

```bash
git add tests/e2e/admin-parity ui/admin ui/admin-layui pnpm-lock.yaml
git commit -m "test: add dual admin parity gate"
```

### Task 6: Add CI and developer documentation

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`

**Interfaces:**
- Consumes: root `pnpm` scripts and dual-admin Playwright project.
- Produces: pull-request client quality gate and reproducible local commands.

- [ ] **Step 1: Add a temporary CI assertion and verify the workflow lacks client commands**

Run: `rg -n "pnpm install|test:clients|build:clients|admin-parity" .github/workflows/ci.yml`

Expected: no matches.

- [ ] **Step 2: Add the client CI job**

Add a separate `client-build-test` job using `actions/setup-node`, Corepack, `pnpm install --frozen-lockfile`, workspace tests, production builds, Playwright browser installation and dual-admin E2E. Set a 20-minute timeout and upload Playwright reports on failure.

- [ ] **Step 3: Document exact local commands and update status**

Document Node 24/Corepack setup, `pnpm install --frozen-lockfile`, both dev server commands, unit/build commands and parity E2E. Only after fresh command output succeeds, change the C0 browser-contract item, C1, and the Vue/Layui shell rows to their actual verified state.

- [ ] **Step 4: Run the full foundation verification**

Run: `pnpm install --frozen-lockfile`

Run: `pnpm test:clients`

Run: `pnpm build:clients`

Run: `pnpm --filter @fullnet/admin-parity-e2e test`

Run: `dotnet build Full.NET.slnx --configuration Release`

Run: `git diff --check`

Expected: all commands exit 0; Vue and Layui production outputs exist; no .NET regression is introduced.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/ci.yml README.md docs/development/getting-started.md docs/roadmap/client-delivery-roadmap.md
git commit -m "ci: verify dual admin clients"
```

## Self-Review Results

- Spec coverage: C0 浏览器 API/error contract、C1 Vue/Layui 壳层、双端状态门禁、Layui 无 SPA 运行时、本地静态资源、CI 和许可证均有对应任务。
- Deliberate exclusions: Identity/Tenancy/Organization CRUD、uni-app、Flutter 和 MAUI 均有明确后续独立计划门禁，没有作为未实现占位塞入本计划。
- 禁用词扫描：本计划没有未决项，也没有用“类似上一任务”替代具体步骤。
- Type consistency: `FullNetProblemDetails`、`readProblemDetails(response)` 和 `request<T>(path, init, signal)` 在任务间名称一致；Layui 使用等价 JavaScript 接口。
