import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginHostAdminAccessToken,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可加载并恢复限时诊断策略', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);

  const getResponse = await request.get(`${apiBaseUrl}/api/v1/settings/diagnostic-policy`, {
    headers: { Authorization: `Bearer ${accessToken}`, Origin: origin }
  });
  expect(getResponse.ok()).toBeTruthy();
  const current = await getResponse.json();

  const expiresAtUtc = new Date(Date.now() + 30 * 60 * 1000).toISOString();
  const updateResponse = await request.put(`${apiBaseUrl}/api/v1/settings/diagnostic-policy`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    },
    data: {
      pressureState: 'Degraded',
      rules: [{
        scopeKind: 'Endpoint',
        scopeValue: `e2e/${clientKind}`,
        successSampleRateOverride: 1,
        bestEffortCapacityOverride: 50,
        maxRequestPayloadBytesOverride: 1024,
        maxResponsePayloadBytesOverride: 2048,
        expiresAtUtc
      }],
      configEntryVersion: current.configEntryVersion
    }
  });
  expect(updateResponse.ok()).toBeTruthy();
  const updated = await updateResponse.json();
  expect(updated.isDefault).toBeFalsy();
  expect(updated.pressureState).toBe('Degraded');

  await loginAsHostAdmin(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /限时诊断/ })).toBeVisible();
  await navigation.getByRole('link', { name: /限时诊断/ }).click();

  const view = clientKind === 'layui'
    ? page.locator('[data-route-view="diagnostic-policy"]')
    : page.locator('.diagnostic-policy-view');
  await expect(view.getByRole('heading', { name: '限时诊断策略', exact: true })).toBeVisible();
  await expect(view.getByText('Degraded', { exact: true })).toBeVisible();
  await view.getByRole('button', { name: /恢复安全默认/ }).click();
  await expect(view.getByText('安全默认', { exact: true })).toBeVisible({ timeout: 15_000 });
});

test('受限 Host 账号访问诊断策略 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(`${apiBaseUrl}/api/v1/settings/diagnostic-policy`, {
    headers: { Authorization: `Bearer ${accessToken}`, Origin: origin }
  });
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /限时诊断/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'settings/diagnostic-policy'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});