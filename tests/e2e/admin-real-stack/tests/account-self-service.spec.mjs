import { expect, test } from '@playwright/test';
import { adminOrigin, loginAsHostUser, loginHostAdminAccessToken, provisionLimitedHostUserViaApi, trackUiAccessToken } from './support/real-stack-auth.mjs';
import { computeTotpCode, decodeBase32Secret } from '../scripts/totp-utils.mjs';

async function withTestAccount(page, run) {
  const stamp = crypto.randomUUID().slice(0, 8);
  const account = await provisionLimitedHostUserViaApi(page.request, 'vue-admin', { username: 'e2e-self-' + stamp, roleCode: 'e2e-self-' + stamp, permissionCodes: ['platform.dashboard.read', 'identity.navigation.read'] });
  try {
    await loginAsHostUser(page, account.username, account.password);
    await run(account);
  } finally {
    // 即使登录、改密或断言失败，也只禁用本例专用账号并撤销其会话。
    const adminToken = await loginHostAdminAccessToken(page.request, 'vue-admin');
    const disabled = await page.request.post(process.env.FULLNET_E2E_API_URL + '/api/v1/identity/users/' + account.userId + '/disable', { headers: { Authorization: 'Bearer ' + adminToken, Origin: adminOrigin('vue-admin') } });
    expect(disabled.status()).toBe(200);
  }
}

// 真实 API/Worker/Migrator 与数据库，禁止 route mock；不把恢复码消费原语宣称为匿名 MFA 登录。
test('匿名恢复请求返回中性受理，错误验证码不能改密', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'en-US'));
  await page.goto('/#/recover-password');
  await page.locator('input[name="email"]').fill(`missing-${Date.now()}@example.test`);
  const accepted = page.waitForResponse(response => response.url().endsWith('/api/v1/auth/recover-password') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Send code', exact: true }).click();
  const requestResponse = await accepted;
  expect(requestResponse.status()).toBe(200);
  expect(await requestResponse.json()).toMatchObject({ challengeId: expect.any(String), expiresAtUtc: expect.any(String) });
  await expect(page.locator('.el-message--success')).toBeVisible();
  await page.locator('input[name="challengeCode"]').fill('000000');
  await page.locator('input[name="newPassword"]').fill('Fixture!Password123');
  const rejected = page.waitForResponse(response => response.url().endsWith('/api/v1/auth/recover-password/confirm'));
  await page.locator('input[name="newPassword"]').press('Enter');
  expect((await rejected).status()).toBe(400);
  await expect(page).toHaveURL(/#\/recover-password$/);
});

test('认证账号通过页面生成恢复码，消费一次且再生成使旧码失效', async ({ page }) => {
  const token = trackUiAccessToken(page);
  await withTestAccount(page, async () => {
  await page.evaluate(() => { location.hash = '/account/security'; });
  const regenerate = page.getByRole('button', { name: '重新生成恢复码', exact: true });
  const firstResponse = page.waitForResponse(response => response.url().endsWith('/mfa/recovery-codes/regenerate'));
  await regenerate.click();
  const first = await firstResponse; expect(first.status()).toBe(200);
  const { recoveryCodes } = await first.json(); expect(recoveryCodes.length).toBeGreaterThan(1);
  await expect(page.locator('.security-settings-recovery-codes li')).toHaveCount(recoveryCodes.length);
  const consume = code => page.request.post('/api/v1/identity/me/mfa/recovery-codes/consume', { headers: { Authorization: `Bearer ${token()}`, Origin: new URL(page.url()).origin }, data: { recoveryCode: code } });
  const consumed = await consume(recoveryCodes[0]); expect(consumed.status()).toBe(200); expect(await consumed.json()).toEqual({ consumed: true });
  expect((await consume(recoveryCodes[0])).status()).toBe(400);
  await page.getByRole('button', { name: '隐藏恢复码', exact: true }).click();
  await expect(page.locator('.security-settings-recovery-codes')).toBeHidden();
  const secondResponse = page.waitForResponse(response => response.url().endsWith('/mfa/recovery-codes/regenerate'));
  await regenerate.click(); expect((await secondResponse).status()).toBe(200);
  expect((await consume(recoveryCodes[1])).status()).toBe(400);
  await page.evaluate(() => { location.hash = '/'; });
  await expect(regenerate).toBeHidden();
  await page.evaluate(() => { location.hash = '/account/security'; });
  await expect(regenerate).toBeVisible(); await expect(page.locator('.security-settings-recovery-codes')).toBeHidden();
  });
});

test('独立账号真实自助改密轮换会话后仍显示成功', async ({ page }) => {
  const token = trackUiAccessToken(page);
  await withTestAccount(page, async () => {
  await page.evaluate(() => { location.hash = '/account/security'; });
  const before = await page.request.get('/api/v1/me', { headers: { Authorization: `Bearer ${token()}` } });
  const originalSession = (await before.json()).sessionId;
  const currentPassword = 'FullNet!2026Cleared';
  const newPassword = 'Fixture!Password123';
  const inputs = page.locator('.security-settings-form input[type="password"]');
  await inputs.nth(0).fill(currentPassword); await inputs.nth(1).fill(newPassword); await inputs.nth(2).fill(newPassword);
  const response = page.waitForResponse(value => value.url().endsWith('/api/v1/me/password'));
  await inputs.nth(2).press('Enter');
  const changed = await response; expect(changed.status()).toBe(200);
  const rotatedToken = (await changed.json()).accessToken;
  await expect(page.locator('.el-message--success')).toContainText('密码已更新');
  const after = await page.request.get('/api/v1/me', { headers: { Authorization: 'Bearer ' + rotatedToken } });
  expect((await after.json()).sessionId).not.toBe(originalSession);
  await expect(inputs.nth(0)).toHaveValue('');
  });
});

test('独立账号通过安全设置登记 TOTP，已启用凭据不可重置', async ({ page }) => {
  const token = trackUiAccessToken(page);
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await withTestAccount(page, async () => {
    await page.evaluate(() => { location.hash = '/account/security'; });
    const card = page.locator('.totp-enrollment-card');
    const begin = async () => {
      const response = page.waitForResponse(value => value.url().endsWith('/mfa/totp/begin'));
      await card.getByRole('button', { name: '开始登记', exact: true }).click();
      const result = await response; expect(result.status()).toBe(200);
      return result.json();
    };
    const first = await begin();
    await expect(card.locator('input[readonly]')).toHaveValue(first.sharedSecretBase32);
    await card.getByRole('button', { name: '隐藏登记材料', exact: true }).click();
    await expect(card.locator('input[readonly]')).toHaveCount(0);
    const restarted = await begin();
    expect(restarted.sharedSecretBase32).not.toBe(first.sharedSecretBase32);
    const key = decodeBase32Secret(restarted.sharedSecretBase32);
    const accepted = new Set([-30_000, 0, 30_000].map(offset => computeTotpCode(key, Date.now() + offset)));
    let invalid = '000000'; while (accepted.has(invalid)) invalid = String(Number(invalid) + 1).padStart(6, '0');
    await card.locator('input[inputmode="numeric"]').fill(invalid);
    const rejected = page.waitForResponse(value => value.url().endsWith('/mfa/totp/confirm'));
    await card.getByRole('button', { name: '确认启用', exact: true }).click();
    expect((await rejected).status()).toBe(400);
    await expect(card.locator('input[readonly]')).toHaveValue(restarted.sharedSecretBase32);
    await card.locator('input[inputmode="numeric"]').fill(computeTotpCode(key));
    const confirmed = page.waitForResponse(value => value.url().endsWith('/mfa/totp/confirm'));
    await card.getByRole('button', { name: '确认启用', exact: true }).click();
    expect((await confirmed).status()).toBe(200);
    await expect(card.getByRole('status')).toHaveText('TOTP 已启用');
    await expect(card.locator('input[readonly]')).toHaveCount(0);
    const forbiddenReset = await page.request.post('/api/v1/identity/me/mfa/totp/begin', {
      headers: { Authorization: `Bearer ${token()}`, Origin: new URL(page.url()).origin }
    });
    expect(forbiddenReset.status()).toBe(409);
    expect(await forbiddenReset.json()).toMatchObject({ code: 'identity.mfa.totp_enrollment_conflict' });
    await page.evaluate(() => { location.hash = '/'; });
    await expect(card).toBeHidden();
    await page.evaluate(() => { location.hash = '/account/security'; });
    await expect(card.getByRole('status')).toHaveText('TOTP 已启用');
    await expect(card.locator('input[readonly]')).toHaveCount(0);
    expect(errors).toEqual([]);
  });
});
