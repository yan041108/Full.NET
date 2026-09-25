import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';
import { skipCodegenWhenAttachModeWithoutWorkspace } from './support/codegeneration-attach-skip.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const catalogTableName = 'fn_codegeneration_template';

test.beforeEach(() => {
  skipCodegenWhenAttachModeWithoutWorkspace();
});

function authHeaders(accessToken, clientKind) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind),
    'Content-Type': 'application/json'
  };
}

test('Host 管理员 column-sync 保留人工 UI 并报告新增列（清单 36 API）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const headers = authHeaders(accessToken, clientKind);

  const columnsResponse = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/catalog/tables/${catalogTableName}/columns`,
    { headers: { Authorization: headers.Authorization, Origin: headers.Origin } }
  );
  expect(columnsResponse.ok()).toBeTruthy();
  const live = await columnsResponse.json();
  expect(live.tableName).toBe(catalogTableName);
  expect(live.columns.length).toBeGreaterThan(0);

  const nameColumn = live.columns.find(column => column.databaseName === 'Name');
  expect(nameColumn).toBeTruthy();
  const manualUi = {
    controlKind: 'textarea',
    showInList: true,
    includeInCreate: true,
    includeInUpdate: true,
    required: true,
    sortable: false,
    queryable: true,
    queryKind: 'contains',
    unique: false,
    includeInImportExport: true
  };
  const partialRequest = {
    tableName: catalogTableName,
    columns: [{ ...nameColumn, ui: manualUi }]
  };

  const syncResponse = await request.post(
    `${apiBaseUrl}/api/v1/code-generation/catalog/column-sync`,
    { data: partialRequest, headers }
  );
  expect(syncResponse.ok()).toBeTruthy();
  const synced = await syncResponse.json();
  expect(synced.tableName).toBe(catalogTableName);
  expect(synced.columns.length).toBeGreaterThan(partialRequest.columns.length);
  expect(synced.addedColumnNames.length).toBeGreaterThan(0);
  expect(synced.addedColumnNames).not.toContain('Name');

  const mergedName = synced.columns.find(column => column.databaseName === 'Name');
  expect(mergedName?.ui?.controlKind).toBe('textarea');
});

test('Host 管理员可在生成工作台预览列同步并合并到 Schema（清单 36 UI）', async ({
  page
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  test.skip(
    clientKind === 'layui',
    '表结构增量合并入口仅在 Vue 代码生成工作台提供'
  );

  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /代码生成预览/, '代码生成');

  const view = page.locator('.codegen-workbench');
  await expect(
    view.getByRole('heading', { name: 'CRUD 产物预览', exact: true })
  ).toBeVisible();
  await expect(view.getByTestId('codegen-catalog-sync')).toBeVisible();

  const schemaInput = view.getByTestId('codegen-schema');
  const schemaBefore = JSON.parse(await schemaInput.inputValue());
  const nameBefore = schemaBefore.columns.find(
    column => column.databaseName === 'Name'
  );
  expect(nameBefore).toBeTruthy();
  const editedSchema = {
    ...schemaBefore,
    columns: [
      {
        ...nameBefore,
        ui: {
          ...(nameBefore.ui ?? {}),
          controlKind: 'textarea',
          sortable: false
        }
      }
    ]
  };
  await schemaInput.fill(JSON.stringify(editedSchema, null, 2));

  const syncResponse = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && response.url().endsWith('/api/v1/code-generation/catalog/column-sync')
  );
  await view.getByTestId('codegen-catalog-sync-preview').click();
  const syncPayload = await (await syncResponse).json();
  expect(syncPayload.addedColumnNames?.length ?? 0).toBeGreaterThan(0);

  await expect(view.getByTestId('codegen-catalog-sync-diff')).toBeVisible();
  await view.getByTestId('codegen-catalog-sync-apply').click();

  const schemaAfter = JSON.parse(await schemaInput.inputValue());
  expect(schemaAfter.columns.length).toBeGreaterThan(1);
  const nameAfter = schemaAfter.columns.find(
    column => column.databaseName === 'Name'
  );
  expect(nameAfter?.ui?.controlKind).toBe('textarea');
});
