import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  crudTableRow,
  findSeedAdminUserViaApi,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

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

async function createQuotaClient(request, clientKind) {
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
        name: `E2E OA Obs ${stamp}`,
        description: 'checklist-40',
        remark: null,
        permissions: ['identity.users.read'],
        expiresAtUtc: null,
        dailyRequestQuota: 1
      }
    }
  );
  expect(createResponse.status()).toBe(201);
  const created = await createResponse.json();
  return { token, origin, headers, created, stamp };
}

test('Host 管理员可读取接入方用量、访问日志并验算签名（清单 40 API）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const { token, origin, headers, created } = await createQuotaClient(request, clientKind);
  const clientId = created.client.id;
  const secret = created.secret;

  expect((await authenticateUsersApi(request, clientKind, secret)).ok()).toBeTruthy();

  const usageResponse = await request.get(
    `${apiBaseUrl}/api/v1/identity/open-access-clients/${clientId}/usage`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(usageResponse.ok()).toBeTruthy();
  const usage = await usageResponse.json();
  expect(usage.dailyRequestQuota).toBe(1);
  expect(usage.todaySuccessCount).toBeGreaterThanOrEqual(1);

  const logsResponse = await request.get(
    `${apiBaseUrl}/api/v1/identity/open-access-clients/${clientId}/access-logs?page=1&pageSize=20`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(logsResponse.ok()).toBeTruthy();
  const logs = await logsResponse.json();
  expect(logs.total).toBeGreaterThanOrEqual(1);
  expect(logs.items.some(
    item => item.eventType === 'open_access_api_key_authentication' && item.succeeded
  )).toBe(true);

  expect((await authenticateUsersApi(request, clientKind, secret)).status()).toBe(401);

  const debugResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/open-access-clients/${clientId}/signature-debug`,
    {
      headers,
      data: {
        secret,
        method: 'GET',
        path: '/api/v1/identity/users',
        query: 'page=1&pageSize=1',
        bodyBase64: null,
        timestamp: Math.floor(Date.now() / 1000).toString(),
        nonce: 'nonceabcdefghijklm',
        signature: 'a'.repeat(64),
        signatureVersion: '1'
      }
    }
  );
  expect(debugResponse.ok()).toBeTruthy();
  const debug = await debugResponse.json();
  expect(debug.canonicalString?.length).toBeGreaterThan(0);
  const debugJson = JSON.stringify(debug);
  expect(debugJson.includes(secret)).toBe(false);

  const foreignLogs = await request.get(
    `${apiBaseUrl}/api/v1/identity/open-access-clients/${crypto.randomUUID()}/access-logs?page=1&pageSize=5`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(foreignLogs.status()).toBe(404);
});

test('Host 管理员可在详情抽屉查看日志、用量与签名调试（清单 40 UI）', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '接入方详情抽屉仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const { created } = await createQuotaClient(request, clientKind);
  const secret = created.secret;
  const displayName = created.client.name;

  expect((await authenticateUsersApi(request, clientKind, secret)).ok()).toBeTruthy();

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /接入方应用/, '身份');
  const row = crudTableRow(page.locator('main'), 'vue', displayName);
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByTestId('open-access-clients-action-detail').click();

  const drawer = page.getByTestId('open-access-client-detail-drawer');
  await expect(drawer).toBeVisible();
  await expect(drawer.getByText('open_access_api_key_authentication')).toBeVisible({
    timeout: 15_000
  });

  await drawer.getByRole('tab', { name: '用量' }).click();
  await expect(drawer.getByText('今日成功')).toBeVisible();
  await expect(drawer.locator('.el-descriptions')).toContainText('1');

  await drawer.getByRole('tab', { name: '签名调试' }).click();
  await drawer.getByLabel('客户端 Secret', { exact: true }).fill(secret);
  await drawer.getByTestId('open-access-client-debug-submit').click();
  await expect(drawer.locator('code').first()).toBeVisible({ timeout: 15_000 });
  const drawerText = await drawer.innerText();
  expect(drawerText.includes(secret)).toBe(false);
});
