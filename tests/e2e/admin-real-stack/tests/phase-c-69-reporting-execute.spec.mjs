import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import {
  createReportingDefinitionViaApi,
  createReportingGroupViaApi,
  executeReportingDefinitionViaApi,
  listReportingDataSourcesViaApi,
  listReportingDefinitionsViaApi,
  reportingDatabaseEngineVersionPortKey
} from './support/reporting-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Reporting 执行页仅 Vue 交付线');
}

test('API：未发布定义与缺失定义 execute 契约（清单 69）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const missingId = randomUUID();
  const { response: missingExecute } = await executeReportingDefinitionViaApi(
    request,
    clientKind,
    missingId,
    { versionNumber: null, parameters: [] }
  );
  expect(missingExecute.status()).toBe(404);

  const suffix = randomUUID().slice(0, 8);
  const { response: groupResponse } = await createReportingGroupViaApi(request, clientKind, {
    parentId: null,
    name: `e2e-rpt-exec-${suffix}`,
    sortOrder: 0,
    isEnabled: true
  });
  expect(groupResponse.status()).toBe(201);
  const group = await groupResponse.json();

  const { response: sourcesResponse } = await listReportingDataSourcesViaApi(request, clientKind);
  expect(sourcesResponse.ok()).toBeTruthy();
  const sourcesPage = await sourcesResponse.json();
  const dataSource = sourcesPage.items?.find((item) => item.isEnabled && item.hasPassword);
  if (!dataSource) {
    test.skip(true, '无已配置密码的启用数据源，跳过未发布 execute 422 路径');
  }

  const { response: createDefResponse } = await createReportingDefinitionViaApi(
    request,
    clientKind,
    {
      groupId: group.id,
      dataSourceId: dataSource.id,
      definitionKey: `e2e.exec_draft_${suffix}`,
      name: `E2E Exec Draft ${suffix}`,
      description: null,
      queryPortKey: reportingDatabaseEngineVersionPortKey,
      parameterSchema: [],
      layoutConfigJson: null,
      isEnabled: true
    }
  );
  expect(createDefResponse.status()).toBe(201);
  const draft = await createDefResponse.json();
  expect(draft.latestPublishedVersionNumber).toBe(0);

  const { response: unpublishedExecute } = await executeReportingDefinitionViaApi(
    request,
    clientKind,
    draft.id,
    { versionNumber: null, parameters: [] },
    1,
    50
  );
  expect(unpublishedExecute.status()).toBe(422);

  const { response: definitionsResponse } = await listReportingDefinitionsViaApi(
    request,
    clientKind
  );
  expect(definitionsResponse.ok()).toBeTruthy();
  const definitions = await definitionsResponse.json();
  const published = definitions.find(
    (item) => item.isEnabled && item.latestPublishedVersionNumber > 0
  );
  if (published) {
    const { response: pagedExecute } = await executeReportingDefinitionViaApi(
      request,
      clientKind,
      published.id,
      { versionNumber: null, parameters: [] },
      1,
      10
    );
    expect([200, 403, 422]).toContain(pagedExecute.status());
    if (pagedExecute.ok()) {
      const pageResult = await pagedExecute.json();
      expect(pageResult.page).toBe(1);
      expect(pageResult.pageSize).toBe(10);
      expect(Array.isArray(pageResult.columns)).toBeTruthy();
      expect(Array.isArray(pageResult.rows)).toBeTruthy();
      expect(typeof pageResult.commandTimeoutSeconds).toBe('number');
    }
  }
});

test('UI：报表执行页参数与运行按钮（清单 69）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /报表执行/);

  await expect(page.getByText('报表执行').first()).toBeVisible({ timeout: 20_000 });
  await expect(page.getByTestId('reporting-execute-definition')).toBeVisible();
  await expect(page.getByTestId('reporting-execute-run')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
