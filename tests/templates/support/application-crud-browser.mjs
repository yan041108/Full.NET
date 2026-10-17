import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createWriteStream, mkdirSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';

const requireFromE2e = createRequire(new URL('../../e2e/admin-real-stack/package.json', import.meta.url));
const requireFromParity = createRequire(new URL('../../e2e/admin-parity/package.json', import.meta.url));
const { chromium, expect } = requireFromE2e('@playwright/test');
const AxeBuilder = requireFromParity('@axe-core/playwright');
const origin = 'http://localhost:25183';

// 诊断不能阻塞原错误传播或清理；页面执行上下文卡住时也按短截止时间放行。
export async function collectApplicationBrowserDiagnostic(action, fallback, timeoutMs = 2_000) {
  let timer;
  try {
    return await Promise.race([
      Promise.resolve().then(action),
      new Promise(resolve => { timer = setTimeout(() => resolve(fallback), timeoutMs); })
    ]);
  } catch {
    return fallback;
  } finally {
    clearTimeout(timer);
  }
}

export async function waitForApplicationTenantLanding(page, tenantName, timeout = 20_000) {
  await expect(page.getByTestId('shell-current-context')).toHaveText(tenantName, { timeout });
  // 会话先发布新上下文，切换页随后跳转首页；等待落地后才能点击产品菜单，避免被迟到的跳转覆盖。
  await expect(page).toHaveURL(origin + '/#/', { timeout });
}

async function auditAccessibility(page, selector, evidence, surface) {
  // 弹窗刚变为可见时仍可能处在淡入过渡；在稳定画面上测量对比度。
  await page.locator(selector).evaluate(async element => {
    const animatedRoot = element.closest('.el-overlay') ?? element;
    const finiteAnimations = animatedRoot.getAnimations({ subtree: true })
      .filter(animation => animation.playState === 'running'
        && animation.effect?.getTiming().iterations !== Infinity);
    await Promise.all(finiteAnimations.map(animation => animation.finished.catch(() => {})));
  });
  const result = await new AxeBuilder({ page }).include(selector)
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const violations = result.violations.map(({ id, impact, nodes }) => ({
    id, impact, count: nodes.length, targets: nodes.map(node => node.target),
  }));
  evidence.accessibility.push({ surface, violations });
  assert.equal(violations.length, 0,
    `${surface} accessibility violations: ${violations.map(value => `${value.id} (${value.targets.map(String).join('; ')})`).join(', ')}`);
}

async function waitForVue(process, timeoutMs = 60_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    assert.equal(process.exitCode, null, 'generated Vue server exited before becoming ready');
    try {
      const response = await fetch(origin, { signal: AbortSignal.timeout(2_000) });
      if (response.ok) return;
    } catch {
      // 开发服务器仍在启动；到期后由统一断言报错。
    }
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  throw new Error('generated Vue server did not become ready');
}

// 复用独立应用自己的 Vue 与 API；每种权限使用全新浏览器上下文，避免会话互相污染。
export async function startApplicationCrudBrowser(appRoot, apiUrl, reportDirectory) {
  mkdirSync(reportDirectory, { recursive: true });
  const logStream = createWriteStream(join(reportDirectory, 'vite.log'), { flags: 'a' });
  const viteProcess = spawn(process.execPath,
    [join(appRoot, 'ui/admin/node_modules/vite/bin/vite.js'), '--host', 'localhost', '--port', '25183', '--strictPort', '--logLevel', 'error'],
    { cwd: join(appRoot, 'ui/admin'), env: { ...process.env, VITE_API_PROXY_TARGET: apiUrl, VITE_STRICT_CSP: '1' }, stdio: 'pipe' });
  viteProcess.stdout?.pipe(logStream, { end: false });
  viteProcess.stderr?.pipe(logStream, { end: false });
  let browser;
  try {
    await waitForVue(viteProcess);
    browser = await chromium.launch();
  } catch (error) {
    await stopLoggedProcess(viteProcess, logStream);
    throw error;
  }

  return {
    async verify({ mode, username, password, tenantName, actionTargetName }) {
      assert.ok(['read', 'none', 'create', 'update', 'delete'].includes(mode), 'unknown browser permission mode');
      if (mode === 'update' || mode === 'delete') assert.ok(actionTargetName, 'browser action target is required');
      const evidence = { mode, completed: false, login: false, tenantContext: false, navigation: false,
        buttons: { create: false, update: false, delete: false }, action: null, accessibility: [], authResponses: [],
        pageErrors: [], failedRequests: [], productModuleRequests: [], productModuleResponses: [], routeEvents: [] };
      const context = await browser.newContext();
      let page;
      let activeAccessToken;
      let stage = 'login';
      const startedAt = Date.now();
      const redact = value => {
        let text = String(value);
        for (const secret of [password, activeAccessToken]) if (secret) text = text.replaceAll(secret, '[REDACTED]');
        return text.slice(0, 1200);
      };
      try {
        page = await context.newPage();
        let tenantSwitchStarted = false;
        let deleteRequests = 0;
        // 仅保留模块路径、状态和有界错误；不记录凭据、请求体或业务响应正文。
        page.on('pageerror', error => { if (evidence.pageErrors.length < 16) evidence.pageErrors.push(redact(error.message)); });
        page.on('requestfailed', request => {
          if (evidence.failedRequests.length < 32) evidence.failedRequests.push({
            path: new URL(request.url()).pathname, error: redact(request.failure()?.errorText), elapsedMs: Date.now() - startedAt
          });
        });
        page.on('request', request => {
          const requestPath = new URL(request.url()).pathname;
          if (evidence.productModuleRequests.length < 32 && /CatalogProductsView|products-page|products\.generated/u.test(requestPath))
            evidence.productModuleRequests.push({ path: requestPath, elapsedMs: Date.now() - startedAt });
          if (new URL(request.url()).pathname.endsWith('/delete') && request.method() === 'POST') {
            deleteRequests += 1;
          }
          if (!tenantSwitchStarted || new URL(request.url()).pathname !== '/api/v1/navigation') return;
          const authorization = request.headers().authorization;
          if (authorization?.startsWith('Bearer ')) activeAccessToken = authorization.slice('Bearer '.length);
        });
        page.on('response', response => {
          const path = new URL(response.url()).pathname;
          if (evidence.productModuleResponses.length < 32 && /CatalogProductsView|products-page|products\.generated/u.test(path))
            evidence.productModuleResponses.push({ path, status: response.status(), elapsedMs: Date.now() - startedAt });
          if (['/api/v1/auth/login', '/api/v1/me', '/api/v1/navigation', '/api/v1/tenancy/context'].includes(path)) {
            evidence.authResponses.push({ path, status: response.status() });
          }
        });
        await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
        await page.goto(origin);
        await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
        await page.getByLabel('账号', { exact: true }).fill(username);
        await page.getByLabel('密码', { exact: true }).fill(password);
        await page.getByRole('button', { name: '进入控制台' }).click();
        await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({ timeout: 30_000 });
        evidence.login = true;

        stage = 'tenant-switch';
        await page.goto(origin + '/#/tenant-context');
        const tenantRow = page.locator('.tenant-context-view .el-table__row').filter({ hasText: tenantName });
        await expect(tenantRow).toBeVisible({ timeout: 15_000 });
        evidence.routeObserverInstalled = await collectApplicationBrowserDiagnostic(() => page.evaluate(() => {
          const router = document.querySelector('#app')?.__vue_app__?.config.globalProperties.$router;
          if (!router?.afterEach || !router?.onError) return false;
          window.__fullnetCrudRouteEvents = [];
          const record = event => { if (window.__fullnetCrudRouteEvents.length < 64) window.__fullnetCrudRouteEvents.push(event); };
          router.afterEach((to, from, failure) => record({ to: to.path, from: from.path, failureType: failure?.type }));
          router.onError(error => record({ error: String(error.message).split('\n')[0] }));
          return true;
        }), false);
        tenantSwitchStarted = true;
        await tenantRow.getByRole('button', { name: '进入租户' }).click();
        await waitForApplicationTenantLanding(page, tenantName);
        evidence.tenantContext = true;

        const navigation = page.getByRole('navigation', { name: '主导航' }).first();
        const link = navigation.locator('a[href*="/catalog/products"]');
        const canRead = mode !== 'none';
        await expect(link).toHaveCount(canRead ? 1 : 0);
        evidence.navigation = canRead;
        if (canRead) {
          stage = 'product-navigation';
          const groups = navigation.locator('.el-sub-menu');
          for (let index = 0; index < await groups.count(); index += 1) {
            const group = groups.nth(index);
            if (!await group.evaluate(element => element.classList.contains('is-opened'))) {
              await group.locator(':scope > .el-sub-menu__title').click();
            }
          }
          evidence.productClickStartedMs = Date.now() - startedAt;
          await link.click();
          evidence.productClickCompletedMs = Date.now() - startedAt;
          await expect(page).toHaveURL(/#\/catalog\/products$/u);
          stage = 'product-view';
          const view = page.locator('.generated-crud-view');
          await expect(view).toBeVisible();
          await expect(view.locator('.el-table__row').filter({ hasText: 'Read permission product' }))
            .toBeVisible({ timeout: 20_000 });
          if (mode === 'read') await auditAccessibility(page, '.generated-crud-view', evidence, 'product-list');
          const originalRow = view.locator('.el-table__row').filter({ hasText: 'Read permission product' });
          for (const [action, label] of [['create', '创建'], ['update', '编辑'], ['delete', '删除']]) {
            const visible = mode === action;
            const owner = action === 'create' ? view.locator('.generated-crud-view__toolbar') : originalRow;
            await expect(owner.getByRole('button', { name: label, exact: true })).toHaveCount(visible ? 1 : 0);
            evidence.buttons[action] = visible;
          }
          if (mode === 'create') {
            const createButton = view.getByRole('button', { name: '创建', exact: true });
            await createButton.focus();
            await page.keyboard.press('Enter');
            const dialog = page.getByRole('dialog', { name: '创建' });
            await expect(dialog).toBeVisible();
            await dialog.evaluate(element => element.setAttribute('data-fullnet-audit-dialog', 'create'));
            await auditAccessibility(page, '[data-fullnet-audit-dialog="create"]', evidence, 'create-dialog');
            await dialog.locator('.el-form-item').filter({ hasText: 'Name' }).locator('input').fill('Browser created product');
            const [response] = await Promise.all([
              page.waitForResponse(value => new URL(value.url()).pathname.replace(/\/$/u, '') === '/api/v1/catalog/products'
                && value.request().method() === 'POST'),
              dialog.getByRole('button', { name: '保存' }).click(),
            ]);
            assert.equal(response.status(), 201, 'browser create did not return HTTP 201');
            await expect(dialog).not.toBeVisible();
            await expect(view.locator('.el-table__row').filter({ hasText: 'Browser created product' })).toBeVisible();
            evidence.action = { type: 'create', status: response.status(), rowVisible: true };
          } else if (mode === 'update') {
            const target = view.locator('.el-table__row').filter({ hasText: actionTargetName });
            await expect(target).toBeVisible();
            await target.getByRole('button', { name: '编辑' }).click();
            const dialog = page.getByRole('dialog', { name: '编辑' });
            await expect(dialog).toBeVisible();
            await dialog.evaluate(element => element.setAttribute('data-fullnet-audit-dialog', 'update'));
            await auditAccessibility(page, '[data-fullnet-audit-dialog="update"]', evidence, 'update-dialog');
            await dialog.locator('.el-form-item').filter({ hasText: 'Name' }).locator('input').fill('Browser updated product');
            const [response] = await Promise.all([
              page.waitForResponse(value => new URL(value.url()).pathname.startsWith('/api/v1/catalog/products/')
                && value.request().method() === 'PUT'),
              dialog.getByRole('button', { name: '保存' }).click(),
            ]);
            assert.equal(response.status(), 200, 'browser update did not return HTTP 200');
            await expect(dialog).not.toBeVisible();
            await expect(view.locator('.el-table__row').filter({ hasText: 'Browser updated product' })).toBeVisible();
            await expect(target).toHaveCount(0);
            evidence.action = { type: 'update', status: response.status(), rowVisible: true };
          } else if (mode === 'delete') {
            const target = view.locator('.el-table__row').filter({ hasText: actionTargetName });
            await expect(target).toBeVisible();
            const deleteButton = target.getByRole('button', { name: '删除' });
            await deleteButton.focus();
            await page.keyboard.press('Enter');
            const dialog = page.getByRole('dialog', { name: '确认删除' });
            await expect(dialog).toBeVisible();
            await dialog.evaluate(element => element.setAttribute('data-fullnet-audit-dialog', 'delete'));
            await auditAccessibility(page, '[data-fullnet-audit-dialog="delete"]', evidence, 'delete-dialog');
            assert.equal(deleteRequests, 0, 'opening delete confirmation sent a delete request');
            await page.keyboard.press('Escape');
            await expect(dialog).not.toBeVisible();
            await expect(target).toBeVisible();
            assert.equal(deleteRequests, 0, 'cancelling delete confirmation sent a delete request');
            await deleteButton.click();
            await expect(dialog).toBeVisible();
            assert.equal(deleteRequests, 0, 'reopening delete confirmation sent a delete request');
            const confirmButton = dialog.getByRole('button', { name: '确认删除' });
            await confirmButton.focus();
            const [response] = await Promise.all([
              page.waitForResponse(value => new URL(value.url()).pathname.endsWith('/delete')
                && value.request().method() === 'POST'),
              page.keyboard.press('Enter'),
            ]);
            assert.equal(response.status(), 200, 'browser delete did not return HTTP 200');
            assert.equal(deleteRequests, 1, 'delete confirmation must send exactly one delete request');
            await expect(target).toHaveCount(0);
            evidence.action = { type: 'delete', status: response.status(), requests: deleteRequests, rowVisible: false };
          }
        } else {
          await page.goto(origin + '/#/catalog/products');
          await expect.poll(() => new URL(page.url()).hash).not.toBe('#/catalog/products');
          await expect(page.locator('.generated-crud-view')).toHaveCount(0);
        }
        assert.ok(activeAccessToken, 'browser navigation did not use a tenant access token');
        evidence.completed = true;
        return activeAccessToken;
      } catch (error) {
        evidence.failure = { stage, error: redact(error.message), path: page ? new URL(page.url()).pathname + new URL(page.url()).hash.split('?')[0] : undefined };
        evidence.clientNavigation = await collectApplicationBrowserDiagnostic(() => page?.evaluate(() => {
          const app = document.querySelector('#app')?.__vue_app__;
          const router = app?.config.globalProperties.$router;
          const session = app?.config.globalProperties.$pinia?._s.get('identity-session');
          return { currentRoute: router?.currentRoute.value.path, productRouteRegistered: router?.hasRoute('m7-catalog-products'),
            sessionState: session?.state, switching: session?.switching,
            canReadProduct: session?.can('catalog.products.read'), hasTenant: Boolean(session?.currentUser?.tenantId) };
        }), undefined);
        await collectApplicationBrowserDiagnostic(() => page?.screenshot({
          path: join(reportDirectory, mode + '-failure.png'), timeout: 2_000
        }), undefined);
        throw error;
      } finally {
        if (page) evidence.routeEvents = (await collectApplicationBrowserDiagnostic(
          () => page.evaluate(() => window.__fullnetCrudRouteEvents ?? []), []))
          .map(event => ({ ...event, error: event.error ? redact(event.error) : undefined }));
        await context.close();
        writeFileSync(join(reportDirectory, mode + '.json'), JSON.stringify(evidence, null, 2));
      }
    },
    async close() {
      try {
        await browser.close();
      } finally {
        await stopLoggedProcess(viteProcess, logStream);
      }
    },
  };
}
