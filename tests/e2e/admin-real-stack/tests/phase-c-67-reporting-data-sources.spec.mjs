import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import {
  clickMainNavLink,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';
import {
  createReportingDataSourceViaApi,
  expectReportingDataSourceDetailSafe,
  expectReportingDataSourceListItemMasked,
  getReportingDataSourceViaApi,
  listReportingDataSourcesViaApi,
  testReportingDataSourceViaApi
} from './support/reporting-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Reporting 数据源页仅 Vue 交付线');
}

test('API：列表脱敏、禁止连接串分隔符、test 端点（清单 67）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: listResponse } = await listReportingDataSourcesViaApi(request, clientKind);
  expect(listResponse.ok()).toBeTruthy();
  const pageResult = await listResponse.json();
  expect(Array.isArray(pageResult.items)).toBeTruthy();
  for (const item of pageResult.items) {
    expectReportingDataSourceListItemMasked(item);
  }

  if (pageResult.items.length > 0) {
    const first = pageResult.items[0];
    const { response: detailResponse } = await getReportingDataSourceViaApi(
      request,
      clientKind,
      first.id
    );
    expect(detailResponse.ok()).toBeTruthy();
    expectReportingDataSourceDetailSafe(await detailResponse.json());
  }

  const { response: invalidCreate } = await createReportingDataSourceViaApi(request, clientKind, {
    tenantId: null,
    name: `e2e-invalid-${randomUUID().slice(0, 8)}`,
    providerKey: 'sql_server',
    serverHost: 'evil;Server=other',
    port: 1433,
    databaseName: 'reporting',
    username: 'reader',
    password: 'NotARealPassword1!',
    trustServerCertificate: true,
    isEnabled: false
  });
  expect(invalidCreate.status()).toBe(422);

  const missingId = randomUUID();
  const { response: testMissing } = await testReportingDataSourceViaApi(
    request,
    clientKind,
    missingId
  );
  expect(testMissing.status()).toBe(404);
});

test('UI：报表数据源页 Host 导航（清单 67）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /报表数据源/);

  await expect(page.getByRole('heading', { name: '报表数据源', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
