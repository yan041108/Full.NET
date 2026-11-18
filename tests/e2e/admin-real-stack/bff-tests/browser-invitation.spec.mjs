import { expect, test } from '@playwright/test';
import { computeTotpCode, decodeBase32Secret } from '../scripts/totp-utils.mjs';

test('已有账号经 MFA 登录后明确认领邀请并进入原企业', async ({ page, context }, testInfo) => {
  const prefix = `FULLNET_BFF_MFA_${testInfo.project.name.toUpperCase()}`;
  const username = process.env[`${prefix}_USER`];
  const userId = process.env[`${prefix}_ID`];
  const secret = process.env[`${prefix}_SECRET`];
  const invitation = process.env[`${prefix}_INVITATION`];
  const tenantId = process.env[`${prefix}_TENANT`];
  expect(username && userId && secret && invitation && tenantId).toBeTruthy();

  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.goto('/');
  await page.getByTestId('login-oidc-center').click();
  await page.locator('input[name="username"]').fill(username);
  await page.locator('input[name="password"]').fill(process.env.FULLNET_BFF_UI_PASSWORD);
  const key = decodeBase32Secret(secret);
  try {
    await page.locator('input[name="second_factor"]').fill(computeTotpCode(key));
    await page.locator('button[type="submit"]').first().click();
    await expect(page.locator('#main-content')).toBeVisible({ timeout: 60_000 });
    const initial = await (await context.request.get('/api/v1/me')).json();
    expect(initial.id).toBe(userId);
    expect(initial.tenantId).toBeNull();
    const available = await (await context.request.get('/api/v1/tenancy/available')).json();
    expect(available.some(item => item.id === tenantId)).toBe(false);

    await page.goto('/#/tenant-context');
    const entry = page.locator('.tenant-context-view__token-row input');
    await entry.fill(invitation);
    const accepted = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/identity/tenant-invitations/accept'
      && response.request().method() === 'POST');
    await page.getByTestId('accept-tenant-invitation-token').click();
    const response = await accepted;
    expect(response.status()).toBe(200);
    const member = await response.json();
    expect(member.userId).toBe(userId);
    expect(member.tenantId).toBe(tenantId);
    expect(member.status).toBe('Active');
    await expect(entry).toHaveValue('');
    const enter = page.locator(`[data-tenant-id="${tenantId}"]`);
    await expect(enter).toBeVisible();
    await enter.click();
    await expect.poll(async () => {
      const me = await context.request.get('/api/v1/me');
      return me.ok() ? (await me.json()).tenantId : null;
    }).toBe(tenantId);
  } finally {
    key.fill(0);
  }
});
