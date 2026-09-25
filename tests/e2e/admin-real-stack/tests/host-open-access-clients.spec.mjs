import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  crudTableRow,
  findSeedAdminUserViaApi,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const viewerUsername = process.env.FULLNET_E2E_VIEWER_USERNAME ?? 'e2e-viewer';
const viewerPassword = process.env.FULLNET_E2E_VIEWER_PASSWORD
  ?? process.env.FULLNET_E2E_PASSWORD
  ?? 'FullNet!2026Secure';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function authHeaders(token, origin) {
  return {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };
}

async function authenticateUsersApi(request, clientKind, secret) {
  return request.get(`${apiBaseUrl}/api/v1/identity/users?page=1&pageSize=1`, {
    headers: {
      Authorization: `ApiKey ${secret}`,
      Origin: adminOrigin(clientKind)
    }
  });
}

test('Host 管理员可创建接入方、轮换密钥并停用（清单 39 API）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = authHeaders(token, origin);
  const stamp = Date.now().toString(36);
  const adminUser = await findSeedAdminUserViaApi(request, clientKind, 'admin', token);

  const createResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/open-access-clients`,
    {
      headers,
      data: {
        username: adminUser.username,
        name: `E2E OpenAccess ${stamp}`,
        description: 'checklist-39',
        remark: null,
        permissions: ['identity.users.read'],
        expiresAtUtc: null,
        dailyRequestQuota: null
      }
    }
  );
  expect(createResponse.status()).toBe(201);
  const created = await createResponse.json();
  expect(created.secret).toBeTruthy();
  expect(created.client.accessKeyId).toMatch(/^fnoa_/u);
  expect(created.client.isActive).toBe(true);

  const authorized = await authenticateUsersApi(request, clientKind, created.secret);
  expect(authorized.ok()).toBeTruthy();

  const rotateResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/open-access-clients/${created.client.id}/rotate`,
    { headers, data: {} }
  );
  expect(rotateResponse.ok()).toBeTruthy();
  const rotated = await rotateResponse.json();
  expect(rotated.secret).not.toBe(created.secret);

  expect((await authenticateUsersApi(request, clientKind, created.secret)).status())
    .toBe(401);
  expect((await authenticateUsersApi(request, clientKind, rotated.secret)).ok())
    .toBeTruthy();

  const disableResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/open-access-clients/${rotated.client.id}/disable`,
    { headers, data: {} }
  );
  expect(disableResponse.ok()).toBeTruthy();
  const disabled = await disableResponse.json();
  expect(disabled.isActive).toBe(false);
  expect((await authenticateUsersApi(request, clientKind, rotated.secret)).status())
    .toBe(401);
});

test('Host 管理员可在接入方应用页完成创建与轮换（清单 39 UI）', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '接入方应用页仅验收 Vue');
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const stamp = Date.now().toString(36);
  const displayName = `E2E OA UI ${stamp}`;
  const adminUser = await findSeedAdminUserViaApi(request, clientKind);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /接入方应用/, '身份');
  await expect(page.getByRole('heading', { name: '接入方应用', exact: true }))
    .toBeVisible({ timeout: 15_000 });

  await page.getByTestId('open-access-clients-action-create').click();
  const dialog = page.getByRole('dialog').last();
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('用户名称', { exact: true }).fill(adminUser.username);
  await dialog.getByLabel('应用名称', { exact: true }).fill(displayName);
  await dialog.getByLabel('权限代码', { exact: true }).fill('identity.users.read');
  await page.getByTestId('open-access-clients-editor-submit').click();

  const secretCard = page.getByTestId('open-access-client-secret');
  await expect(secretCard).toBeVisible({ timeout: 15_000 });
  const secret = await secretCard.locator('code').textContent();
  expect(secret?.startsWith('fnoa_')).toBeTruthy();
  expect((await authenticateUsersApi(request, clientKind, secret)).ok()).toBeTruthy();

  const row = crudTableRow(page.locator('main'), 'vue', displayName);
  await expect(row).toBeVisible();
  await row.getByTestId('open-access-clients-action-rotate').click();
  await page.getByRole('button', { name: '确定' }).click();
  await expect(secretCard).toBeVisible({ timeout: 15_000 });
  const rotatedSecret = await secretCard.locator('code').textContent();
  expect(rotatedSecret).not.toBe(secret);
  expect((await authenticateUsersApi(request, clientKind, secret)).status()).toBe(401);

  await row.getByTestId('open-access-clients-action-disable').click();
  await page.getByRole('button', { name: '确定' }).click();
  await expect(row).toContainText('已停用');
});

test('受限 Host 账号无法创建接入方应用（清单 39）', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  await loginAsHostViewer(page);
  await page.goto('/#/identity/open-access-clients');
  await expect(page.getByTestId('open-access-clients-action-create')).toHaveCount(0);

  const viewerToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    viewerUsername,
    viewerPassword
  );
  const createResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/open-access-clients`,
    {
      headers: authHeaders(viewerToken, origin),
      data: {
        username: 'admin',
        name: 'denied',
        description: null,
        remark: null,
        permissions: ['identity.users.read'],
        expiresAtUtc: null,
        dailyRequestQuota: null
      }
    }
  );
  expect(createResponse.status()).toBe(403);
  expect((await createResponse.json()).code).toBe('authorization.permission_denied');
});
