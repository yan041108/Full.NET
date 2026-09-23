import { expect, test } from '@playwright/test';
import { computeTotpCode, decodeBase32Secret } from '../scripts/totp-utils.mjs';
import {
  adminOrigin,
  clickMainNavLink,
  createHostUserViaApi,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const adminPassword = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const targetPassword = 'FullNet!2026SaTarget';
const stackProfile = process.env.FULLNET_E2E_STACK_PROFILE ?? 'development';

test.beforeEach(async ({ page }) => {
  test.skip(
    stackProfile !== 'production-totp',
    'Production TOTP 闸门仅在 FULLNET_E2E_STACK_PROFILE=production-totp 真实栈下执行。'
  );
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Production 栈登记 TOTP 后远程授予必须携带验证码', async ({ page, request }, testInfo) => {
  test.setTimeout(180_000);
  test.skip(testInfo.project.metadata.clientKind === 'layui', 'Layui 已冻结。');

  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const adminToken = await loginHostAdminAccessToken(request, clientKind);
  const authHeaders = {
    Authorization: `Bearer ${adminToken}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };

  const beginResponse = await request.post(`${apiBaseUrl}/api/v1/identity/me/mfa/totp/begin`, {
    headers: authHeaders
  });
  expect(beginResponse.status()).toBe(200);
  const beginBody = await beginResponse.json();
  expect(beginBody.sharedSecretBase32).toBeTruthy();

  const totpKey = decodeBase32Secret(beginBody.sharedSecretBase32);
  const confirmResponse = await request.post(`${apiBaseUrl}/api/v1/identity/me/mfa/totp/confirm`, {
    headers: authHeaders,
    data: { totpCode: computeTotpCode(totpKey) }
  });
  expect(confirmResponse.status()).toBe(200);

  const targetUsername = `sa-totp-${Date.now().toString(36)}`;
  await createHostUserViaApi(request, clientKind, {
    username: targetUsername,
    displayName: 'TOTP 授予目标',
    password: targetPassword
  });

  // 创建用户的共用准备函数会再次登录；单会话策略使此前的管理员令牌失效。
  const grantToken = await loginHostAdminAccessToken(request, clientKind);
  const grantHeaders = {
    ...authHeaders,
    Authorization: `Bearer ${grantToken}`
  };

  const missingTotpResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/super-administrators/grant`,
    {
      headers: grantHeaders,
      data: { username: targetUsername, currentPassword: adminPassword }
    }
  );
  expect(missingTotpResponse.status()).toBe(401);
  const missingTotpBody = await missingTotpResponse.json();
  expect(missingTotpBody.code).toBe('identity.mfa.totp_required');

  const grantResponse = await request.post(`${apiBaseUrl}/api/v1/identity/super-administrators/grant`, {
    headers: grantHeaders,
    data: {
      username: targetUsername,
      currentPassword: adminPassword,
      totpCode: computeTotpCode(totpKey)
    }
  });
  expect(grantResponse.status()).toBe(200);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /超级管理员/, '系统管理');
  await expect(page.getByRole('heading', { name: '超级管理员', exact: true })).toBeVisible();
  await expect(page.getByText(targetUsername, { exact: true }).first()).toBeVisible({
    timeout: 15_000
  });
});
