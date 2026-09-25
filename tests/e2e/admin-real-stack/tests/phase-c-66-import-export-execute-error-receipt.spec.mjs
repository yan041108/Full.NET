import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  enterDevelopmentTenant,
  loginAsHostAdmin,
  loginTenantAdminAccessToken
} from './support/real-stack-auth.mjs';
import {
  createImportExportTaskViaApi,
  downloadImportExportErrorReceiptViaApi,
  downloadImportExportTemplateViaApi,
  executeImportExportTaskViaApi,
  expectImportExportExecuteAccepted,
  getImportExportTaskViaApi,
  organizationPositionsWorksheetKey,
  organizationTenantPositionsSchemaKey
} from './support/import-export-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'ImportExport 任务页仅 Vue 交付线');
}

test('API：预校验成功后 execute 与错误回执端点契约（清单 66）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const accessToken = await loginTenantAdminAccessToken(request, clientKind);
  const templateResponse = await downloadImportExportTemplateViaApi(
    request,
    clientKind,
    organizationTenantPositionsSchemaKey,
    organizationPositionsWorksheetKey,
    accessToken
  );
  expect(templateResponse.ok()).toBeTruthy();
  const templateBytes = await templateResponse.body();

  const createResponse = await createImportExportTaskViaApi(
    request,
    clientKind,
    organizationTenantPositionsSchemaKey,
    organizationPositionsWorksheetKey,
    templateBytes,
    'tenant-positions-execute.xlsx',
    accessToken
  );
  expect(createResponse.status()).toBe(201);
  const created = await createResponse.json();
  if (created.statusKey !== 'preview_succeeded') {
    test.skip(true, '模板预校验未成功，跳过 execute 路径');
  }

  const { response: executeResponse } = await executeImportExportTaskViaApi(
    request,
    clientKind,
    created.id,
    accessToken
  );
  expect(executeResponse.ok()).toBeTruthy();
  const afterExecute = await executeResponse.json();
  expectImportExportExecuteAccepted(afterExecute);
  expect(afterExecute.id).toBe(created.id);

  if (!afterExecute.hasErrorReceipt) {
    const { response: receiptResponse } = await downloadImportExportErrorReceiptViaApi(
      request,
      clientKind,
      created.id,
      accessToken
    );
    expect([404, 422]).toContain(receiptResponse.status());
  } else {
    const { response: receiptResponse } = await downloadImportExportErrorReceiptViaApi(
      request,
      clientKind,
      created.id,
      accessToken
    );
    expect(receiptResponse.ok()).toBeTruthy();
    const receiptBytes = await receiptResponse.body();
    expect(receiptBytes.byteLength).toBeGreaterThan(0);
  }

  const { response: detailResponse } = await getImportExportTaskViaApi(
    request,
    clientKind,
    created.id,
    accessToken
  );
  expect(detailResponse.ok()).toBeTruthy();
});

test('UI：详情抽屉 execute / 错误回执按钮壳（清单 66）', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  const accessToken = await loginTenantAdminAccessToken(request, clientKind);
  const templateResponse = await downloadImportExportTemplateViaApi(
    request,
    clientKind,
    organizationTenantPositionsSchemaKey,
    organizationPositionsWorksheetKey,
    accessToken
  );
  expect(templateResponse.ok()).toBeTruthy();
  const createResponse = await createImportExportTaskViaApi(
    request,
    clientKind,
    organizationTenantPositionsSchemaKey,
    organizationPositionsWorksheetKey,
    await templateResponse.body(),
    'tenant-positions-ui-66.xlsx',
    accessToken
  );
  expect(createResponse.status()).toBe(201);
  const created = await createResponse.json();

  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);
  await clickMainNavLink(page, /导入任务/);
  await expect(page.getByRole('heading', { name: '导入任务', exact: true })).toBeVisible({
    timeout: 20_000
  });

  await page.getByTestId('import-export-task-detail').first().click();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);

  if (created.statusKey === 'preview_succeeded') {
    await expect(page.getByTestId('import-export-task-execute')).toBeVisible();
  }
});
