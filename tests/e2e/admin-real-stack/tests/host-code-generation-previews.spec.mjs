import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginHostAdminAccessToken,
  statusPath
} from './support/real-stack-auth.mjs';
import { toOrganizationOwnedExplicitSchema } from './support/organization-owned-codegen-schema.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const previewRequest = {
  ownerKey: 'acme',
  moduleKey: 'catalog',
  entityKey: 'product',
  databaseTableName: 'acme_catalog_product',
  rootNamespace: 'Acme.Modules.Catalog',
  clrTypeName: 'Product',
  apiResourceName: 'products',
  permissionResourceName: 'products',
  dataScope: 'TenantRequired',
  hasVersion: true,
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
      databaseName: 'IsActive',
      clrPropertyName: 'IsActive',
      jsonPropertyName: 'isActive',
      scalarType: 'Boolean',
      isNullable: false,
      maxLength: null,
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

function codeGenerationView(page, clientKind) {
  return clientKind === 'layui'
    ? page.locator('[data-route-view="code-generation-previews"]')
    : page.locator('.codegen-workbench');
}

function generatedContent(view, clientKind) {
  return clientKind === 'layui'
    ? view.locator('[data-codegen-content] code')
    : view.getByTestId('codegen-content').locator('code');
}

function schemaInput(view) {
  return view.getByRole('textbox', { name: 'Schema 输入', exact: true });
}

function runHistory(view, clientKind) {
  return clientKind === 'layui'
    ? view.locator('[data-codegen-run-history]')
    : view.getByTestId('codegen-run-history');
}

test('Host 管理员可通过双管理端执行受跟踪预览并回读无源码历史', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /代码生成预览/, '代码生成');

  const view = codeGenerationView(page, clientKind);
  await expect(
    view.getByRole('heading', { name: 'CRUD 产物预览', exact: true })
  ).toBeVisible();
  await expect(view.getByText('受跟踪预览', { exact: true })).toBeVisible();
  const trackedResponse = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && response.url().endsWith('/api/v1/code-generation/runs/preview')
  );
  await view.getByRole('button', { name: '生成预览', exact: true }).click();
  const tracked = await (await trackedResponse).json();
  expect(typeof tracked.runId).toBe('string');

  await expect(view.getByText('acme_catalog_product', { exact: true }))
    .toBeVisible({ timeout: 15_000 });
  await expect(runHistory(view, clientKind)).toContainText(tracked.runId);
  await expect(view.getByText('catalog.products.read', { exact: true }))
    .toBeVisible();
  const artifactPath = 'clients/vue/products.generated.ts';
  await view
    .getByRole('navigation', { name: '生成产物' })
    .getByRole('button', { name: new RegExp(artifactPath.replaceAll('.', '\\.')) })
    .click();
  await expect(generatedContent(view, clientKind))
    .toContainText('catalogListProducts');

  await page.reload();
  await expect(runHistory(codeGenerationView(page, clientKind), clientKind))
    .toContainText(tracked.runId);

  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const historyResponse = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/runs?page=1&pageSize=100`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind)
      }
    }
  );
  expect(historyResponse.ok()).toBeTruthy();
  const history = await historyResponse.json();
  const persisted = history.items.find(run => run.id === tracked.runId);
  expect(persisted).toBeTruthy();
  expect(persisted).not.toHaveProperty('schema');
  expect(persisted).not.toHaveProperty('preview');
  expect(persisted).not.toHaveProperty('content');
  expect(persisted).not.toHaveProperty('errorMessage');
});

test('Host 管理员可预览组织归属 Schema 并生成写入授权片段', async ({
  page
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /代码生成预览/, '代码生成');

  const view = codeGenerationView(page, clientKind);
  const explicitSchema = toOrganizationOwnedExplicitSchema(
    JSON.parse(await schemaInput(view).inputValue())
  );
  await schemaInput(view).fill(JSON.stringify(explicitSchema, null, 2));
  await view.getByRole('button', { name: '生成预览', exact: true }).click();
  await expect(view.getByText('acme_catalog_product', { exact: true }))
    .toBeVisible({ timeout: 15_000 });
  await view
    .getByRole('navigation', { name: '生成产物' })
    .getByRole('button', { name: /backend\/ProductFeature\.g\.cs/ })
    .click();
  await expect(generatedContent(view, clientKind))
    .toContainText('IOrganizationOwnedEntityWriteAuthorizer');
  await expect(generatedContent(view, clientKind))
    .toContainText('BuildOrganizationUnitFilter');
});

test('受限 Host 账号访问预览 API 被拒绝且双端导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginAccessToken(request, clientKind);
  const response = await request.post(
    `${apiBaseUrl}/api/v1/code-generation/previews`,
    {
      data: previewRequest,
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind),
        'Content-Type': 'application/json'
      }
    }
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: '代码生成模板' })).toHaveCount(0);
  await expect(navigation.getByRole('link', { name: '代码生成预览' })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'code-generation/previews'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(
    page.getByRole('heading', { name: '没有访问权限' })
  ).toBeVisible();
});
