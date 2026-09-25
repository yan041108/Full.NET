import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：存储 Provider 目录含 OSS、摘要无密钥、Resolve 边界（清单 51）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: origin
  };

  const listResponse = await request.get(`${apiBaseUrl}/api/v1/files/storage-providers`, {
    headers
  });
  expect(listResponse.ok()).toBeTruthy();
  const providers = await listResponse.json();
  expect(Array.isArray(providers)).toBeTruthy();

  const keys = providers.map((item) => item.providerKey);
  expect(keys).toContain('local');
  expect(keys).toContain('s3');
  expect(keys).toContain('oss');

  const oss = providers.find((item) => item.providerKey === 'oss');
  expect(oss?.kind).toBe('oss');
  expect(oss?.supportsConnectivityTest).toBe(true);
  if (oss?.configurationSummary) {
    expect(oss.configurationSummary.toLowerCase()).not.toMatch(/secret|accesskey/i);
  }

  const localTest = await request.post(`${apiBaseUrl}/api/v1/files/storage-providers/local/test`, {
    headers
  });
  expect(localTest.ok()).toBeTruthy();
  const localResult = await localTest.json();
  expect(localResult.succeeded).toBe(false);

  const missingTest = await request.post(
    `${apiBaseUrl}/api/v1/files/storage-providers/not-a-provider/test`,
    { headers }
  );
  expect(missingTest.status()).toBe(404);
});

test('UI：存储提供商管理页（清单 51，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '存储 Provider 页仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /存储提供商/);

  await expect(page.getByText('存储提供商', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('oss', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
