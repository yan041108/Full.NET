import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostUser,
  loginHostAdminAccessToken,
  provisionLimitedHostUserViaApi
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

const downloadableSchema = {
  ownerKey: 'acme',
  moduleKey: 'catalog',
  entityKey: 'product',
  databaseTableName: 'acme_catalog_product',
  rootNamespace: 'Acme.Modules.Catalog',
  clrTypeName: 'Product',
  apiResourceName: 'products',
  permissionResourceName: 'products',
  dataScope: 'tenant.required',
  entityCapabilities: {
    deleteMode: 'hard.delete',
    hasCreatedAudit: false,
    hasUpdatedAudit: false,
    hasDeletedAudit: false,
    hasVersion: true,
    ownershipMode: 'none'
  },
  scene: 'single',
  relationships: [],
  columns: [
    {
      databaseName: 'Id',
      clrPropertyName: 'Id',
      jsonPropertyName: 'id',
      scalarType: 'Uuid',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'TenantId',
      clrPropertyName: 'TenantId',
      jsonPropertyName: 'tenantId',
      scalarType: 'Uuid',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'Name',
      clrPropertyName: 'Name',
      jsonPropertyName: 'displayName',
      scalarType: 'String',
      isNullable: false,
      maxLength: 200,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'Version',
      clrPropertyName: 'Version',
      jsonPropertyName: 'version',
      scalarType: 'Int64',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    }
  ]
};

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('模板预览 run 可下载 zip；无 download 权限时按钮不可见且 API 403', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '下载仅验收 Vue');
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const adminToken = await loginHostAdminAccessToken(request, clientKind);
  const stamp = Date.now().toString(36);
  const templateName = `e2e-dl-${stamp}`;

  const createTemplate = await request.post(`${apiBaseUrl}/api/v1/code-generation/templates`, {
    data: { name: templateName, schema: downloadableSchema },
    headers: {
      Authorization: `Bearer ${adminToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  const createBody = await createTemplate.text();
  expect(createTemplate.status(), createBody).toBe(201);
  const template = JSON.parse(createBody);

  const previewResponse = await request.post(`${apiBaseUrl}/api/v1/code-generation/runs/preview`, {
    data: {
      templateId: template.id,
      templateVersion: template.version
    },
    headers: {
      Authorization: `Bearer ${adminToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  const previewBody = await previewResponse.text();
  expect(previewResponse.status(), previewBody).toBe(200);
  const preview = JSON.parse(previewBody);
  expect(typeof preview.runId).toBe('string');

  const zipResponse = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/runs/${preview.runId}/artifacts.zip`,
    {
      headers: {
        Authorization: `Bearer ${adminToken}`,
        Origin: origin
      }
    }
  );
  expect(zipResponse.status()).toBe(200);
  expect((await zipResponse.body()).byteLength).toBeGreaterThan(32);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /代码生成预览/, '代码生成');
  const view = page.locator('.codegen-workbench');
  await expect(view.getByRole('heading', { name: 'CRUD 产物预览', exact: true })).toBeVisible();
  await expect(view.getByTestId('codegen-download')).toBeVisible();

  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'codegen.previews.read',
      'codegen.runs.read',
      'codegen.runs.execute',
      'codegen.templates.read',
      'codegen.catalog.read'
    ]
  });
  const limitedToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );
  const deniedZip = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/runs/${preview.runId}/artifacts.zip`,
    {
      headers: {
        Authorization: `Bearer ${limitedToken}`,
        Origin: origin
      }
    }
  );
  expect(deniedZip.status()).toBe(403);
  expect((await deniedZip.json()).code).toBe('authorization.permission_denied');

  await loginAsHostUser(page, limited.username, limited.password);
  await page.goto('/#/code-generation/previews');
  await expect(page.locator('.codegen-workbench').getByRole('heading', {
    name: 'CRUD 产物预览',
    exact: true
  })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('codegen-download')).toHaveCount(0);
});
