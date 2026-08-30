import { expect, test } from '@playwright/test';

const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const apiBaseUrl = process.env.FULLNET_E2E_API_URL
  ?? `http://localhost:${process.env.FULLNET_E2E_API_PORT ?? '5159'}`;

test('真实 API 登录后仅以内存保存访问令牌，并可通过 Cookie 恢复会话', async ({ page, context }) => {
  await page.goto('/');
  await expect(page.getByText('登录 Full.NET', { exact: true })).toBeVisible();

  const inputs = page.getByRole('textbox');
  await inputs.nth(0).fill(username);
  await inputs.nth(1).fill(password);

  const loginResponsePromise = page.waitForResponse(response =>
    response.url().endsWith('/api/v1/auth/login') && response.request().method() === 'POST');
  await page.locator('.primary').click();
  const loginResponse = await loginResponsePromise;

  expect(loginResponse.status()).toBe(200);
  expect(loginResponse.headers()['access-control-allow-origin']).toBe('http://localhost:5175');
  await expect(page.getByRole('banner').getByText('我的待办', { exact: true })).toBeVisible();

  const cookies = await context.cookies(apiBaseUrl);
  const refreshCookie = cookies.find(cookie => cookie.name === 'fullnet-refresh');
  const csrfCookie = cookies.find(cookie => cookie.name === 'fullnet-csrf');
  expect(refreshCookie).toMatchObject({ httpOnly: true, sameSite: 'Strict' });
  expect(csrfCookie).toMatchObject({ httpOnly: false, sameSite: 'Strict' });

  const browserStorageKeys = await page.evaluate(() => [
    ...Object.keys(window.localStorage),
    ...Object.keys(window.sessionStorage)
  ]);
  expect(browserStorageKeys.filter(key => /access|refresh|token/iu.test(key))).toEqual([]);

  const refreshResponsePromise = page.waitForResponse(response =>
    response.url().endsWith('/api/v1/auth/refresh') && response.request().method() === 'POST');
  await page.reload();
  const refreshResponse = await refreshResponsePromise;

  expect(refreshResponse.status()).toBe(200);
  await expect(page.getByRole('banner').getByText('我的待办', { exact: true })).toBeVisible();
});
