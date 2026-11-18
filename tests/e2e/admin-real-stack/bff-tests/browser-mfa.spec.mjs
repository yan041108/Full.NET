import { expect, test } from '@playwright/test';
import { computeTotpCode, decodeBase32Secret } from '../scripts/totp-utils.mjs';

test('中心登录必须验证 TOTP 才能建立原账号的 Host 会话', async ({ page, context }, testInfo) => {
  const prefix = `FULLNET_BFF_MFA_${testInfo.project.name.toUpperCase()}`;
  const username = process.env[`${prefix}_USER`];
  const userId = process.env[`${prefix}_ID`];
  const secret = process.env[`${prefix}_SECRET`];
  expect(username).toBeTruthy();
  expect(userId).toBeTruthy();
  expect(secret).toBeTruthy();

  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.goto('/');
  await page.getByTestId('login-oidc-center').click();
  await page.locator('input[name="username"]').fill(username);
  await page.locator('input[name="password"]').fill(process.env.FULLNET_BFF_UI_PASSWORD);
  const missing = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/connect/authorize' && response.request().method() === 'POST');
  await page.locator('button[type="submit"]').first().click();
  expect((await missing).status()).toBe(401);
  expect((await context.request.get('/api/v1/me')).status()).toBe(401);
  expect((await context.cookies()).some(cookie => cookie.name === '__Host-fullnet-browser')).toBe(false);

  await page.locator('input[name="username"]').fill(username);
  await page.locator('input[name="password"]').fill(process.env.FULLNET_BFF_UI_PASSWORD);
  const key = decodeBase32Secret(secret);
  try {
    await page.locator('input[name="second_factor"]').fill(computeTotpCode(key));
    await page.locator('button[type="submit"]').first().click();
    await expect(page.locator('#main-content')).toBeVisible({ timeout: 60_000 });
    const me = await page.evaluate(async () => {
      const response = await fetch('/api/v1/me');
      return { status: response.status, body: await response.json() };
    });
    expect(me.status).toBe(200);
    expect(me.body.id).toBe(userId);
    expect(me.body.tenantId).toBeNull();
    expect((await context.cookies()).find(cookie => cookie.name === '__Host-fullnet-browser'))
      .toMatchObject({ httpOnly: true, secure: true, sameSite: 'Lax' });
    const storage = await page.evaluate(() => ({ ...localStorage, ...sessionStorage }));
    expect(Object.keys(storage).filter(value => /access|refresh|pkce|verifier|credential/i.test(value))).toEqual([]);
  } finally {
    key.fill(0);
  }
});
