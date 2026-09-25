import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import {
  clickMainNavLink,
  enterDevelopmentTenant,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';
import {
  createReportingExportTaskViaApi,
  downloadReportingExportTaskViaApi,
  listReportingExportTasksViaApi,
  reportingExportFormatExcel
} from './support/reporting-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Reporting 导出页仅 Vue 交付线');
}

test('API：导出任务列表、拒绝非 Excel 格式与下载授权（清单 70）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: listResponse } = await listReportingExportTasksViaApi(request, clientKind);
  expect(listResponse.ok()).toBeTruthy();
  const pageResult = await listResponse.json();
  expect(Array.isArray(pageResult.items)).toBeTruthy();

  const { response: unsupportedFormat } = await createReportingExportTaskViaApi(
    request,
    clientKind,
    {
      definitionId: randomUUID(),
      formatKey: 'html',
      versionNumber: null,
      parameters: []
    }
  );
  expect(unsupportedFormat.status()).toBe(422);

  const { response: missingDefinition } = await createReportingExportTaskViaApi(
    request,
    clientKind,
    {
      definitionId: randomUUID(),
      formatKey: reportingExportFormatExcel,
      versionNumber: null,
      parameters: []
    }
  );
  expect(missingDefinition.status()).toBe(404);

  const { response: downloadMissing } = await downloadReportingExportTaskViaApi(
    request,
    clientKind,
    randomUUID()
  );
  expect(downloadMissing.status()).toBe(404);
});

test('UI：报表导出任务页（清单 70）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);
  await clickMainNavLink(page, /报表导出/);

  await expect(page.getByRole('heading', { name: '报表导出任务', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('reporting-export-create')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
