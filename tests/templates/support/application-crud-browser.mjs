import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createWriteStream, mkdirSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';

const requireFromE2e = createRequire(new URL('../../e2e/admin-real-stack/package.json', import.meta.url));
const { chromium, expect } = requireFromE2e('@playwright/test');
const origin = 'http://localhost:25183';

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
    async verify({ mode, username, password, tenantName }) {
      assert.ok(['read', 'none', 'create', 'update', 'delete'].includes(mode), 'unknown browser permission mode');
      const evidence = { mode, completed: false, login: false, tenantContext: false, navigation: false,
        buttons: { create: false, update: false, delete: false }, authResponses: [] };
      const context = await browser.newContext();
      try {
        const page = await context.newPage();
        let tenantSwitchStarted = false;
        let activeAccessToken;
        page.on('request', request => {
          if (!tenantSwitchStarted || new URL(request.url()).pathname !== '/api/v1/navigation') return;
          const authorization = request.headers().authorization;
          if (authorization?.startsWith('Bearer ')) activeAccessToken = authorization.slice('Bearer '.length);
        });
        page.on('response', response => {
          const path = new URL(response.url()).pathname;
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

        await page.goto(origin + '/#/tenant-context');
        const tenantRow = page.locator('.tenant-context-view .el-table__row').filter({ hasText: tenantName });
        await expect(tenantRow).toBeVisible({ timeout: 15_000 });
        tenantSwitchStarted = true;
        await tenantRow.getByRole('button', { name: '进入租户' }).click();
        await expect(page.getByTestId('shell-current-context')).toHaveText(tenantName, { timeout: 20_000 });
        evidence.tenantContext = true;

        const navigation = page.getByRole('navigation', { name: '主导航' }).first();
        const link = navigation.locator('a[href*="/catalog/products"]');
        const canRead = mode !== 'none';
        await expect(link).toHaveCount(canRead ? 1 : 0);
        evidence.navigation = canRead;
        if (canRead) {
          const groups = navigation.locator('.el-sub-menu');
          for (let index = 0; index < await groups.count(); index += 1) {
            const group = groups.nth(index);
            if (!await group.evaluate(element => element.classList.contains('is-opened'))) {
              await group.locator(':scope > .el-sub-menu__title').click();
            }
          }
          await link.click();
          await expect(page).toHaveURL(/#\/catalog\/products$/u);
          const view = page.locator('.generated-crud-view');
          await expect(view).toBeVisible();
          await expect(view.locator('.el-table__row').filter({ hasText: 'Read permission product' }))
            .toBeVisible({ timeout: 20_000 });
          for (const [action, label] of [['create', '创建'], ['update', '编辑'], ['delete', '删除']]) {
            const visible = mode === action;
            await expect(view.getByRole('button', { name: label, exact: true })).toHaveCount(visible ? 1 : 0);
            evidence.buttons[action] = visible;
          }
        } else {
          await page.goto(origin + '/#/catalog/products');
          await expect.poll(() => new URL(page.url()).hash).not.toBe('#/catalog/products');
          await expect(page.locator('.generated-crud-view')).toHaveCount(0);
        }
        assert.ok(activeAccessToken, 'browser navigation did not use a tenant access token');
        evidence.completed = true;
        return activeAccessToken;
      } finally {
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
