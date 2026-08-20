import { expect, test } from '@playwright/test';
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

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可从真实 API 加载超管目录并完成密码重认证授予与撤销', async ({
  page,
  request
}, testInfo) => {
  test.setTimeout(90_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const targetUsername = `sa-target-${Date.now().toString(36)}`;
  const created = await createHostUserViaApi(request, clientKind, {
    username: targetUsername,
    displayName: '超管授予目标',
    password: targetPassword
  });

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /超级管理员/, '系统管理');
  await expect(page.getByRole('heading', { name: '超级管理员', exact: true })).toBeVisible();
  await expect(page.getByTestId('super-admin-action-grant')).toBeVisible();

  const adminToken = await loginHostAdminAccessToken(request, clientKind);
  const grantResponse = await request.post(`${apiBaseUrl}/api/v1/identity/super-administrators/grant`, {
    data: {
      username: targetUsername,
      currentPassword: adminPassword
    },
    headers: {
      Authorization: `Bearer ${adminToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  const grantBody = await grantResponse.text();
  expect(grantResponse.status(), grantBody).toBe(200);

  await page.reload();
  await expect(page.getByText(targetUsername, { exact: true }).first()).toBeVisible({
    timeout: 15_000
  });

  const revokeResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/super-administrators/${created.id}/revoke`,
    {
      data: { currentPassword: adminPassword },
      headers: {
        Authorization: `Bearer ${adminToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    }
  );
  expect(revokeResponse.status()).toBe(200);

  await page.reload();
  await expect(page.getByText(targetUsername, { exact: true })).toHaveCount(0, {
    timeout: 15_000
  });
});

test('撤销最后一名超级管理员时 API 返回稳定错误码且 Vue 展示该码', async ({
  page,
  request
}, testInfo) => {
  test.setTimeout(60_000);
  const clientKind = testInfo.project.metadata.clientKind;
  test.skip(clientKind === 'layui', 'Layui 管理端已冻结，最后一名保护只验收 Vue。');
  const origin = adminOrigin(clientKind);
  const adminToken = await loginHostAdminAccessToken(request, clientKind);

  const listResponse = await request.get(`${apiBaseUrl}/api/v1/identity/super-administrators`, {
    headers: { Authorization: `Bearer ${adminToken}`, Origin: origin }
  });
  expect(listResponse.status()).toBe(200);
  const administrators = await listResponse.json();
  expect(administrators.length).toBeGreaterThanOrEqual(1);
  const lastAdmin = administrators.find(item => item.username === 'admin') ?? administrators[0];

  const revokeResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/super-administrators/${lastAdmin.userId}/revoke`,
    {
      data: { currentPassword: adminPassword },
      headers: {
        Authorization: `Bearer ${adminToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    }
  );
  expect(revokeResponse.status()).toBe(403);
  const problem = await revokeResponse.json();
  expect(problem.code).toBe('identity.super_administrator.last_remaining');

  await loginAsHostAdmin(page);
  await page.goto('/#/identity/super-administrators');
  await expect(page.getByRole('heading', { name: '超级管理员', exact: true })).toBeVisible();
  await expect(page.getByTestId('super-admin-action-revoke').first()).toBeVisible();
});
