import { expect, test } from '@playwright/test';
import {
  adminOrigin,
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
