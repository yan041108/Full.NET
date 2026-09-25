import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import {
  createAiModelConfigViaApi,
  expectAiModelConfigDetailSafe,
  expectAiModelConfigListItemMasked,
  getAiModelConfigViaApi,
  getAiTenantQuotaViaApi,
  listAiModelConfigsViaApi,
  listAiTenantQuotasViaApi,
  testAiModelConfigViaApi
} from './support/ai-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'AI 模型配置页仅 Vue 交付线');
}

test('API：模型配置脱敏、拒绝虚假供应商与租户配额（清单 72）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: listResponse } = await listAiModelConfigsViaApi(request, clientKind);
  expect(listResponse.ok()).toBeTruthy();
  const pageResult = await listResponse.json();
  expect(Array.isArray(pageResult.items)).toBeTruthy();
  for (const item of pageResult.items) {
    expectAiModelConfigListItemMasked(item);
  }

  if (pageResult.items.length > 0) {
    const { response: detailResponse } = await getAiModelConfigViaApi(
      request,
      clientKind,
      pageResult.items[0].id
    );
    expect(detailResponse.ok()).toBeTruthy();
    expectAiModelConfigDetailSafe(await detailResponse.json());
  }

  const suffix = randomUUID().slice(0, 8);
  const { response: invalidCreate } = await createAiModelConfigViaApi(request, clientKind, {
    tenantId: null,
    name: `e2e-fake-provider-${suffix}`,
    providerKey: 'fake_universal_vendor',
    endpointBaseUrl: 'https://api.example.com/v1',
    modelId: 'gpt-test',
    organizationId: null,
    apiKey: 'sk-test-not-real',
    isDefault: false,
    isEnabled: true
  });
  expect(invalidCreate.status()).toBe(422);

  const { response: testMissing } = await testAiModelConfigViaApi(request, clientKind, randomUUID());
  expect(testMissing.status()).toBe(404);

  const { response: quotasResponse } = await listAiTenantQuotasViaApi(request, clientKind);
  expect(quotasResponse.ok()).toBeTruthy();
  expect(Array.isArray((await quotasResponse.json()).items)).toBeTruthy();

  const { response: quotaMissing } = await getAiTenantQuotaViaApi(
    request,
    clientKind,
    randomUUID()
  );
  expect(quotaMissing.status()).toBe(404);
});

test('UI：AI 模型配置页（清单 72）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /AI 模型配置/);

  await expect(page.getByRole('heading', { name: 'AI 模型配置', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('ai-model-config-create')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
