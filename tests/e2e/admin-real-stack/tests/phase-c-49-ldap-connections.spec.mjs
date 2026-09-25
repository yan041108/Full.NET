import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

function buildE2eConnectionPayload(stamp) {
  return {
    tenantId: null,
    name: `E2E49 ${stamp}`,
    host: 'ldap.e2e49.invalid',
    port: 389,
    useTls: false,
    baseDn: 'DC=example,DC=com',
    bindDn: 'CN=svc,DC=example,DC=com',
    bindPassword: 'E2e49-Secret-Not-Returned',
    userSearchFilter: '(sAMAccountName={0})',
    userAccountAttribute: 'sAMAccountName',
    employeeIdAttribute: null,
    departmentCodeAttribute: null,
    syncSearchBaseDn: 'OU=Users,DC=example,DC=com',
    isEnabled: false
  };
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：LDAP 连接不回显凭据且预览 DN 白名单 fail-closed（清单 49）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };

  const stamp = Date.now().toString(36);
  const createResponse = await request.post(`${apiBaseUrl}/api/v1/identity/ldap-connections`, {
    headers,
    data: buildE2eConnectionPayload(stamp)
  });
  expect(createResponse.status()).toBe(201);
  const created = await createResponse.json();
  expect(created.bindPassword).toBeUndefined();
  expect(created.name).toBe(`E2E49 ${stamp}`);

  const getResponse = await request.get(
    `${apiBaseUrl}/api/v1/identity/ldap-connections/${created.id}`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(getResponse.ok()).toBeTruthy();
  const loaded = await getResponse.json();
  expect(loaded.bindPassword).toBeUndefined();

  const previewResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/ldap-connections/${created.id}/preview-sync`,
    {
      headers,
      data: { searchBaseDn: 'DC=other,DC=com', maxEntries: 10 }
    }
  );
  expect(previewResponse.status()).toBe(400);
  const previewProblem = await previewResponse.json();
  expect(previewProblem.code).toBe('identity.ldap_connections.preview_search_base_out_of_scope');

  const deleteResponse = await request.delete(
    `${apiBaseUrl}/api/v1/identity/ldap-connections/${created.id}`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(deleteResponse.status()).toBe(204);
});

test('UI：LDAP 连接管理页（清单 49，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', 'LDAP 页仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /LDAP 连接/);

  await expect(page.getByText('LDAP 连接', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('ldap-connections-action-create')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
