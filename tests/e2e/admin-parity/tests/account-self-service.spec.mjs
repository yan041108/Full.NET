import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test.use({ locale: 'zh-CN' });

const id = '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60';
const challenge = { challengeId: id, expiresAtUtc: '2099-01-01T00:00:00Z' };
const invitation = { invitationId: id, tenantId: id, registrationWayId: id, email: 'invite@example.test', expiresAtUtc: challenge.expiresAtUtc };
const invitationKey = 'fullnet.registration.invitation';
async function json(route, body, status = 200) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'en-US'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 401, contentType: 'application/problem+json', body: JSON.stringify({ status: 401, code: 'identity.refresh_missing', title: 'No session' }) }));
});

for (const kind of ['register', 'recover-password']) {
  test(`${kind}: native Enter submission preserves locale and rejects weak passwords`, async ({ page }) => {
    const registration = kind === 'register';
    const submissions = [];
    await page.route(`**/api/v1/auth/${registration ? 'register/email-challenge' : 'recover-password'}`, route => json(route, challenge, 202));
    await page.route(`**/api/v1/auth/${registration ? 'register' : 'recover-password/confirm'}`, async route => {
      expect(route.request().headers()['accept-language']).toBe('en-US');
      submissions.push(route.request().postDataJSON());
      if (registration) await json(route, { userId: id }, 201); else await route.fulfill({ status: 204 });
    });
    await page.goto(`/#/${kind}`);
    const email = page.locator('input[name="email"]');
    await email.fill('user@example.test');
    await page.locator('select[name="locale"]').selectOption('zh-CN');
    await expect(page.getByRole('heading', { name: registration ? '创建账号' : '恢复密码', exact: true })).toBeVisible();
    await expect(email).toHaveValue('user@example.test');
    await page.locator('select[name="locale"]').selectOption('en-US');
    if (registration) await page.getByRole('textbox', { name: 'Display name', exact: true }).fill('Fixture User');
    await page.getByRole('button', { name: 'Send code', exact: true }).click();
    await expect(page.locator('.el-message--success')).toBeVisible();
    await page.locator('input[name="challengeCode"]').fill('123456');
    const password = page.locator('input[type="password"]');
    await password.fill('weak');
    await password.press('Enter');
    await expect(page.locator('.el-message--warning')).toBeVisible();
    expect(submissions).toEqual([]);
    await password.fill('Valid!Password123');
    await password.press('Enter');
    await expect(page).toHaveURL(/#\/login$/);
    expect(submissions).toHaveLength(1);
    expect(submissions[0]).toMatchObject({ challengeId: id, challengeCode: '123456', ...(registration ? { email: 'user@example.test', password: 'Valid!Password123' } : { newPassword: 'Valid!Password123' }) });
  });

  test(`${kind}: public form passes WCAG automated checks`, async ({ page }) => {
    await page.goto(`/#/${kind}`);
    await expect(page.locator('form')).toBeVisible();
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
}

test('invitation: scrub token, wait for verification, preserve invited target and bind the challenge', async ({ page }) => {
  let release;
  const ready = new Promise(resolve => { release = resolve; });
  const requests = [];
  await page.route('**/api/v1/auth/invitations/verify', async route => { await ready; await json(route, invitation); });
  await page.route('**/api/v1/auth/register/email-challenge', async route => { requests.push(route.request().postDataJSON()); await json(route, challenge, 202); });
  await page.goto(`/#/register?invitationId=${id}&invitationToken=fixture-token`);
  await expect(page).toHaveURL(/#\/register$/);
  await expect(page.getByRole('button', { name: 'Send code', exact: true })).toBeDisabled();
  await expect(page.locator('input[name="email"]')).toBeDisabled();
  release();
  await expect(page.locator('input[name="email"]')).toHaveValue(invitation.email);
  await expect(page.locator('input[name="email"]')).toHaveAttribute('readonly', '');
  await page.getByRole('button', { name: 'Send code', exact: true }).click();
  await expect(page.locator('.el-message--success')).toBeVisible();
  expect(requests).toEqual([{ email: invitation.email, purpose: 3, invitationId: id, invitationToken: 'fixture-token' }]);
});

test('invitation: valid-looking response for another invitation fails closed', async ({ page }) => {
  let sends = 0;
  await page.route('**/api/v1/auth/invitations/verify', route => json(route, { ...invitation, invitationId: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f61' }));
  await page.route('**/api/v1/auth/register/email-challenge', route => { sends++; return json(route, challenge, 202); });
  await page.goto(`/#/register?invitationId=${id}&invitationToken=fixture-token`);
  await expect(page.locator('.el-message--error')).toContainText('Invitation is invalid or expired.');
  await expect(page.getByRole('button', { name: 'Send code', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Register', exact: true })).toBeDisabled();
  expect(await page.evaluate(key => sessionStorage.getItem(key), invitationKey)).toBeNull();
  expect(sends).toBe(0);
});

test('malformed invitation cache renders safely and is removed', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(key => sessionStorage.setItem(key, 'null'), invitationKey);
  await page.goto('/#/register');
  await expect(page.getByRole('button', { name: 'Send code', exact: true })).toBeEnabled();
  await expect(page.getByRole('heading', { name: 'Create account', exact: true })).toBeVisible();
  expect(await page.evaluate(key => sessionStorage.getItem(key), invitationKey)).toBeNull();
  expect(errors).toEqual([]);
});
