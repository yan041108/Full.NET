import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  createHostUserViaApi,
  loginAccessToken,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginHostAdminAccessToken,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const adminUsername = process.env.FULLNET_E2E_USERNAME ?? 'admin';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可从真实 API 加载在线会话列表', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /在线用户/);

  const onlineSessionsView = clientKind === 'layui'
    ? page.locator('[data-route-view="online-sessions"]')
    : page.locator('.online-sessions-view');

  await expect(onlineSessionsView.getByRole('heading', { name: '在线用户', exact: true })).toBeVisible();
  await expect(onlineSessionsView.getByRole('cell', {
    name: new RegExp(` ${adminUsername}$`)
  }).first()).toBeVisible({
    timeout: 15_000
  });
});

test('受限 Host 账号访问在线会话 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/identity/online-sessions?page=1&pageSize=20`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /在线用户/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'identity/online-sessions'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
test('Host 管理员可从 UI 强制下线其他在线会话', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const victimPassword = 'FullNet!2026Secure';
  const victimUsername = `e2e-online-${Date.now()}`;
  await createHostUserViaApi(request, clientKind, {
    username: victimUsername,
    displayName: 'E2E 在线会话',
    password: victimPassword
  });
  const victimToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    victimUsername,
    victimPassword
  );

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /在线用户/);

  const onlineSessionsView = clientKind === 'layui'
    ? page.locator('[data-route-view="online-sessions"]')
    : page.locator('.online-sessions-view');

  await expect(onlineSessionsView.getByText(victimUsername, { exact: true }).first()).toBeVisible({
    timeout: 15_000
  });

  const victimRow = onlineSessionsView
    .getByRole('row')
    .filter({ hasText: victimUsername })
    .first();
  const revokeResponse = page.waitForResponse(response =>
    response.url().includes('/api/v1/identity/online-sessions/')
    && response.url().endsWith('/revoke')
    && response.request().method() === 'POST'
  );
  await victimRow.getByRole('button', { name: '强制下线' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '强制下线', exact: true }).click();
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  expect((await revokeResponse).ok()).toBeTruthy();

  const meResponse = await request.get(`${apiBaseUrl}/api/v1/me`, {
    headers: {
      Authorization: `Bearer ${victimToken}`,
      Origin: origin
    }
  });
  expect(meResponse.status()).toBe(401);
});

test('Host 管理员可按用户撤销全部在线会话并读取会话策略（清单 30）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const victimPassword = 'FullNet!2026Secure';
  const victimUsername = `e2e-revoke-all-${Date.now()}`;
  const victim = await createHostUserViaApi(request, clientKind, {
    username: victimUsername,
    displayName: 'E2E 全量下线',
    password: victimPassword
  });
  const tokenA = await loginAccessTokenWithPassword(
    request,
    clientKind,
    victimUsername,
    victimPassword
  );
  const tokenB = await loginAccessTokenWithPassword(
    request,
    clientKind,
    victimUsername,
    victimPassword
  );

  const hostToken = await loginHostAdminAccessToken(request, clientKind);
  const hostHeaders = {
    Authorization: `Bearer ${hostToken}`,
    Origin: origin
  };

  const policyResponse = await request.get(`${apiBaseUrl}/api/v1/identity/session-policy`, {
    headers: hostHeaders
  });
  expect(policyResponse.ok()).toBeTruthy();
  const policy = await policyResponse.json();
  expect(typeof policy.loginPolicy).toBe('number');

  const revokeResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/online-sessions/users/${victim.id}/revoke-all`,
    { headers: hostHeaders }
  );
  expect(revokeResponse.ok()).toBeTruthy();
  const revoked = await revokeResponse.json();
  expect(revoked.userId).toBe(victim.id);
  expect(revoked.username).toBe(victimUsername);
  expect(revoked.revokedSessionCount).toBeGreaterThanOrEqual(1);

  for (const token of [tokenA, tokenB]) {
    const meResponse = await request.get(`${apiBaseUrl}/api/v1/me`, {
      headers: {
        Authorization: `Bearer ${token}`,
        Origin: origin
      }
    });
    expect(meResponse.status()).toBe(401);
  }
});

test('Vue 在线用户页展示会话策略并可全部下线（清单 30）', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '会话策略与全部下线仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const victimPassword = 'FullNet!2026Secure';
  const victimUsername = `e2e-revoke-all-ui-${Date.now()}`;
  await createHostUserViaApi(request, clientKind, {
    username: victimUsername,
    displayName: 'E2E 全量下线 UI',
    password: victimPassword
  });
  const victimToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    victimUsername,
    victimPassword
  );

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /在线用户/);

  const onlineSessionsView = page.locator('.online-sessions-view');
  await expect(onlineSessionsView.getByText(/当前策略/u)).toBeVisible({ timeout: 15_000 });
  await expect(onlineSessionsView.getByText(victimUsername, { exact: true }).first()).toBeVisible({
    timeout: 15_000
  });

  const victimRow = onlineSessionsView
    .getByRole('row')
    .filter({ hasText: victimUsername })
    .first();
  await victimRow.getByRole('button', { name: '全部下线', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: '全部下线', exact: true }).click();

  const meResponse = await request.get(`${apiBaseUrl}/api/v1/me`, {
    headers: {
      Authorization: `Bearer ${victimToken}`,
      Origin: origin
    }
  });
  expect(meResponse.status()).toBe(401);
});
