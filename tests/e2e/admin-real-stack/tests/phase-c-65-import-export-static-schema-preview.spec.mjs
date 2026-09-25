import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  enterDevelopmentTenant,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';
import {
  createImportExportTaskViaApi,
  downloadImportExportTemplateViaApi,
  expectOrganizationPositionsSchema,
  listImportExportSchemasViaApi,
  organizationPositionsWorksheetKey,
  organizationTenantPositionsSchemaKey
} from './support/import-export-real-stack.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'ImportExport 任务页仅 Vue 交付线');
}

test('API：静态 Schema 模板下载与预校验入队（清单 65，职位消费者）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: schemasResponse, accessToken } = await listImportExportSchemasViaApi(
    request,
    clientKind
  );
  expect(schemasResponse.ok()).toBeTruthy();
  const schemas = await schemasResponse.json();
  expectOrganizationPositionsSchema(schemas);

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
    'tenant-positions-import.xlsx',
    accessToken
  );
  expect(createResponse.status()).toBe(201);
  const task = await createResponse.json();
  expect(task.schemaKey).toBe(organizationTenantPositionsSchemaKey);
  expect(task.worksheetKey).toBe(organizationPositionsWorksheetKey);
  expect(['preview_succeeded', 'preview_failed']).toContain(task.statusKey);

  const listResponse = await request.get(
    `${apiBaseUrl}/api/v1/import-export/tasks?page=1&pageSize=10&schemaKey=${encodeURIComponent(organizationTenantPositionsSchemaKey)}`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind)
      }
    }
  );
  expect(listResponse.ok()).toBeTruthy();
  const pageResult = await listResponse.json();
  expect(pageResult.items.some((entry) => entry.id === task.id)).toBe(true);
});

test('UI：导入任务页列表与预校验任务入口占位（清单 65）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);
  await clickMainNavLink(page, /导入任务/);

  await expect(page.getByRole('heading', { name: '导入任务', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('import-export-task-create')).toBeVisible();
  await expect(page.getByTestId('import-export-task-create')).toBeDisabled();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
