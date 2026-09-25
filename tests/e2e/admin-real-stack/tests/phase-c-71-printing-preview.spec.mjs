import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import {
  clickMainNavLink,
  enterDevelopmentTenant,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';
import {
  expectPrintingFormSchemaCatalog,
  getPrintingFormSchemaViaApi,
  listPrintingFormSchemasViaApi,
  listPrintingTemplatesViaApi,
  previewPrintingTemplateViaApi,
  printingTenantProfileCardSchemaKey
} from './support/printing-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Printing 预览页仅 Vue 交付线');
}

test('API：固定表单 Schema、模板列表与预览契约（清单 71）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: schemasResponse } = await listPrintingFormSchemasViaApi(request, clientKind);
  expect(schemasResponse.ok()).toBeTruthy();
  expectPrintingFormSchemaCatalog(await schemasResponse.json());

  const { response: schemaDetail } = await getPrintingFormSchemaViaApi(
    request,
    clientKind,
    printingTenantProfileCardSchemaKey
  );
  expect(schemaDetail.ok()).toBeTruthy();

  const { response: unknownSchema } = await getPrintingFormSchemaViaApi(
    request,
    clientKind,
    'printing.custom_form'
  );
  expect(unknownSchema.status()).toBe(404);

  const { response: templatesResponse } = await listPrintingTemplatesViaApi(request, clientKind);
  expect(templatesResponse.ok()).toBeTruthy();
  expect(Array.isArray(await templatesResponse.json())).toBeTruthy();

  const { response: previewMissing } = await previewPrintingTemplateViaApi(
    request,
    clientKind,
    randomUUID()
  );
  expect(previewMissing.status()).toBe(404);
});

test('UI：打印预览页入口（清单 71）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);
  await clickMainNavLink(page, /打印预览/);

  await expect(page.getByTestId('printing-preview-run')).toBeVisible({ timeout: 20_000 });
  await expect(page.getByTestId('printing-preview-create')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
