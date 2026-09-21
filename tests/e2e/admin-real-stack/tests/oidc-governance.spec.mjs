import { expect, test } from '@playwright/test';
import {
  loginAsHostAdmin,
  loginAsHostViewer,
  loginAccessToken,
  statusPath
} from './support/real-stack-auth.mjs';
import { resolveApiBase } from './support/identity-oidc-fixtures.mjs';

const apiBase = resolveApiBase();

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可在治理页创建并停用 OIDC 客户端', async ({ page }) => {
  await loginAsHostAdmin(page);
  const clientId = `e2e-gov-${Date.now().toString(36)}`;
  await page.goto('/#/identity/oidc-clients');
  await expect(page.getByRole('heading', { name: 'OIDC 客户端' })).toBeVisible({
    timeout: 15_000
  });
  await page.getByTestId('oidc-clients-action-create').click();
  const editor = page.getByRole('dialog').last();
  await editor.getByLabel('Client Id', { exact: true }).fill(clientId);
  await editor.getByLabel('显示名称', { exact: true }).fill('E2E 治理客户端');
  await editor.getByLabel('回调地址（每行一个）', { exact: true }).fill('https://localhost:5199/callback');
  const createResponsePromise = page.waitForResponse(
    response =>
      response.request().method() === 'POST'
      && response.url().includes('/api/v1/identity/oidc-clients')
  );
  await editor.getByTestId('oidc-clients-editor-submit').click();
  expect((await createResponsePromise).status()).toBe(201);
  await expect(page.getByText('OIDC 客户端已创建')).toBeVisible({ timeout: 15_000 });
  const row = page.locator('.el-table__row').filter({ hasText: clientId });
  await expect(row).toBeVisible();
  await row.getByTestId('oidc-clients-action-disable').click();
  await page.getByRole('button', { name: '确定' }).click();
  await expect(page.getByText('OIDC 客户端已停用')).toBeVisible();
  await expect(row.getByText('已停用')).toBeVisible();
});

test('机密客户端轮换密钥后仅展示一次明文', async ({ page }) => {
  await loginAsHostAdmin(page);
  const clientId = `e2e-rotate-${Date.now().toString(36)}`;
  await page.goto('/#/identity/oidc-clients');
  await page.getByTestId('oidc-clients-action-create').click();
  const editor = page.getByRole('dialog').last();
  await editor.getByLabel('Client Id', { exact: true }).fill(clientId);
  await editor.getByLabel('显示名称', { exact: true }).fill('E2E 轮换客户端');
  await editor.getByLabel('回调地址（每行一个）', { exact: true }).fill('https://localhost:5198/callback');
  const createResponsePromise = page.waitForResponse(
    response =>
      response.request().method() === 'POST'
      && response.url().includes('/api/v1/identity/oidc-clients')
  );
  await editor.getByTestId('oidc-clients-editor-submit').click();
  expect((await createResponsePromise).status()).toBe(201);
  await expect(page.getByText('OIDC 客户端已创建')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('oidc-client-secret')).toBeVisible();
  const secretText = await page.getByTestId('oidc-client-secret').locator('code').textContent();
  expect(secretText?.trim().length).toBeGreaterThan(10);

  const row = page.locator('.el-table__row').filter({ hasText: clientId });
  await row.getByTestId('oidc-clients-action-rotate').click();
  await page.getByRole('button', { name: '确定' }).click();
  await expect(page.getByText('OIDC 客户端密钥已轮换')).toBeVisible();
  await expect(page.getByTestId('oidc-client-secret')).toBeVisible();
});

test('授权列表可撤销有效授权', async ({ page }) => {
  await loginAsHostAdmin(page);
  await page.goto('/#/identity/oidc-authorizations');
  await expect(page.getByRole('heading', { name: 'OIDC 授权授予' })).toBeVisible({
    timeout: 15_000
  });
  const validRow = page.locator('.el-table__row').filter({ hasText: 'valid' }).first();
  await expect(validRow).toBeVisible({ timeout: 15_000 });
  await validRow.getByTestId('oidc-authorizations-action-revoke').click();
  await page.getByRole('button', { name: '确定' }).click();
  await expect(page.getByText('OIDC 授权已撤销')).toBeVisible();
});

test('签名密钥页可查看密钥列表', async ({ page }) => {
  await loginAsHostAdmin(page);
  await page.goto('/#/identity/oidc-signing-keys');
  await expect(page.getByRole('heading', { name: 'OIDC 签名密钥' })).toBeVisible({
    timeout: 15_000
  });
  await expect(page.locator('.el-table__row').first()).toBeVisible();
});

test('无 OIDC 治理权限账号访问治理 API 被拒绝', async ({ page, request }) => {
  const token = await loginAccessToken(request, 'vue');
  const response = await request.get(`${apiBase}/api/v1/identity/oidc-clients?page=1&pageSize=1`, {
    headers: { authorization: `Bearer ${token}` }
  });
  expect(response.status()).toBe(403);

  await loginAsHostViewer(page);
  await page.goto(statusPath('vue', 'identity/oidc-clients'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
});
