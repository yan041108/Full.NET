import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  createHostUserViaApi,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostUser,
  loginHostAdminAccessToken,
  provisionLimitedHostUserViaApi
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const defaultPassword = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Vue 仅读权限不渲染导入与批量启停按钮，直接 API 返回 403', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '精确按钮权限仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'identity.users.read'
    ]
  });
  const accessToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );

  for (const path of [
    '/api/v1/identity/users/import',
    '/api/v1/identity/users/batch-disable',
    '/api/v1/identity/users/batch-enable'
  ]) {
    const response = await request.post(`${apiBaseUrl}${path}`, {
      data: path.endsWith('/import')
        ? { rows: [{ username: 'x', displayName: 'x', password: defaultPassword }] }
        : { userIds: [limited.userId] },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    });
    expect(response.status(), path).toBe(403);
    const problem = await response.json();
    expect(problem.code).toBe('authorization.permission_denied');
  }

  await loginAsHostUser(page, limited.username, limited.password);
  await clickMainNavLink(page, /用户管理/, '系统管理');
  const view = page.locator('.users-view');
  await expect(view.getByRole('heading', { name: '用户管理', exact: true })).toBeVisible();
  await expect(view.getByTestId('users-action-import')).toHaveCount(0);
  await expect(view.getByTestId('users-action-batch-disable')).toHaveCount(0);
  await expect(view.getByTestId('users-action-batch-enable')).toHaveCount(0);
});

test('Host 管理员可通过真实 API 导入用户并批量停用启用', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '导入/批量启停只验收 Vue');
  test.setTimeout(90_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const stamp = Date.now().toString(36);
  const importUsername = `e2e-imp-${stamp}`;
  const batchUsername = `e2e-bat-${stamp}`;

  const importResponse = await request.post(`${apiBaseUrl}/api/v1/identity/users/import`, {
    data: {
      rows: [
        {
          username: importUsername,
          displayName: `导入用户 ${stamp}`,
          password: defaultPassword
        }
      ]
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(importResponse.status()).toBe(200);
  const imported = await importResponse.json();
  expect(imported.succeededCount).toBe(1);
  expect(imported.results[0].succeeded).toBe(true);

  const rejectedImport = await request.post(`${apiBaseUrl}/api/v1/identity/users/import`, {
    data: {
      rows: [
        {
          username: `e2e-sa-${stamp}`,
          displayName: '拒绝超管',
          password: defaultPassword,
          accountType: 'super_admin'
        }
      ]
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(rejectedImport.status()).toBe(200);
  const rejected = await rejectedImport.json();
  expect(rejected.succeededCount).toBe(0);

  const batchUser = await createHostUserViaApi(request, clientKind, {
    username: batchUsername,
    displayName: `批量用户 ${stamp}`,
    password: defaultPassword
  });

  const disableResponse = await request.post(`${apiBaseUrl}/api/v1/identity/users/batch-disable`, {
    data: { userIds: [batchUser.id] },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(disableResponse.status()).toBe(200);
  expect((await disableResponse.json()).succeededCount).toBe(1);

  const enableResponse = await request.post(`${apiBaseUrl}/api/v1/identity/users/batch-enable`, {
    data: { userIds: [batchUser.id] },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(enableResponse.status()).toBe(200);
  expect((await enableResponse.json()).succeededCount).toBe(1);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /用户管理/, '系统管理');
  const view = page.locator('.users-view');
  await expect(view.getByTestId('users-action-import')).toBeVisible();
  await expect(view.getByTestId('users-action-batch-disable')).toBeVisible();
  await expect(view.getByTestId('users-action-batch-enable')).toBeVisible();
  await expect(view.getByText(importUsername, { exact: true }).first()).toBeVisible({
    timeout: 15_000
  });
});
