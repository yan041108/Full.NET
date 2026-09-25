import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import {
  createReportingDefinitionViaApi,
  createReportingGroupViaApi,
  expectReportingQueryPortCatalogSafe,
  getReportingQueryPortViaApi,
  listReportingDefinitionsViaApi,
  listReportingQueryPortsViaApi,
  reportingDatabaseEngineVersionPortKey
} from './support/reporting-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Reporting 定义页仅 Vue 交付线');
}

test('API：Query Port 目录、分组与拒绝任意 SQL 端口（清单 68）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: portsResponse } = await listReportingQueryPortsViaApi(request, clientKind);
  expect(portsResponse.ok()).toBeTruthy();
  expectReportingQueryPortCatalogSafe(await portsResponse.json());

  const suffix = randomUUID().slice(0, 8);
  const { response: groupResponse } = await createReportingGroupViaApi(request, clientKind, {
    parentId: null,
    name: `e2e-rpt-group-${suffix}`,
    sortOrder: 0,
    isEnabled: true
  });
  expect(groupResponse.status()).toBe(201);
  const group = await groupResponse.json();

  const { response: invalidPort } = await createReportingDefinitionViaApi(request, clientKind, {
    groupId: group.id,
    dataSourceId: randomUUID(),
    definitionKey: `e2e.invalid_port_${suffix}`,
    name: `E2E Invalid Port ${suffix}`,
    description: null,
    queryPortKey: 'reporting.arbitrary_client_sql',
    parameterSchema: [],
    layoutConfigJson: null,
    isEnabled: true
  });
  expect(invalidPort.status()).toBe(422);

  const { response: definitionsResponse } = await listReportingDefinitionsViaApi(
    request,
    clientKind
  );
  expect(definitionsResponse.ok()).toBeTruthy();
  expect(Array.isArray(await definitionsResponse.json())).toBeTruthy();

  const { response: portDetail } = await getReportingQueryPortViaApi(
    request,
    clientKind,
    reportingDatabaseEngineVersionPortKey
  );
  expect(portDetail.ok()).toBeTruthy();
  const portBody = await portDetail.json();
  expect(portBody.queryPortKey).toBe(reportingDatabaseEngineVersionPortKey);
  expect(portBody).not.toHaveProperty('sql');
});

test('UI：报表定义页分组与定义入口（清单 68）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /报表定义/);

  await expect(page.getByRole('heading', { name: '报表定义', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('reporting-group-create')).toBeVisible();
  await expect(page.getByTestId('reporting-definition-create')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
