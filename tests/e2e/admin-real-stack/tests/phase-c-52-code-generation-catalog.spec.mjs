import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';
import { skipCodegenWhenAttachModeWithoutWorkspace } from './support/codegeneration-attach-skip.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const sampleTable = 'fn_codegeneration_template';

test.beforeEach(() => {
  skipCodegenWhenAttachModeWithoutWorkspace();
});

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：只读表/视图目录、元数据与迁移草案（不执行 DDL）（清单 52）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };

  const tablesResponse = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/catalog/tables`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(tablesResponse.ok()).toBeTruthy();
  const tables = await tablesResponse.json();
  expect(tables.some((row) => row.tableName === sampleTable)).toBeTruthy();

  const viewsResponse = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/catalog/views`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(viewsResponse.ok()).toBeTruthy();
  expect(Array.isArray(await viewsResponse.json())).toBeTruthy();

  const objectsResponse = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/catalog/objects`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(objectsResponse.ok()).toBeTruthy();
  const objects = await objectsResponse.json();
  expect(objects.length).toBeGreaterThan(0);

  const metadataResponse = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/catalog/objects/${sampleTable}/metadata`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(metadataResponse.ok()).toBeTruthy();
  const metadata = await metadataResponse.json();
  expect(metadata.objectName).toBe(sampleTable);
  expect(metadata.objectKind).toBe('table');
  expect(metadata.columns.length).toBeGreaterThan(0);

  const draftResponse = await request.post(
    `${apiBaseUrl}/api/v1/code-generation/catalog/migration-draft`,
    {
      headers,
      data: { tableName: sampleTable }
    }
  );
  expect(draftResponse.ok()).toBeTruthy();
  const draft = await draftResponse.json();
  expect(draft.tableName).toBe(sampleTable);
  expect(typeof draft.sqlServerDraft).toBe('string');
  expect(typeof draft.mySqlDraft).toBe('string');
  expect(draft.sqlServerDraft.length).toBeGreaterThan(0);
  expect(draft.mySqlDraft.length).toBeGreaterThan(0);

  const viewOnly = objects.find((item) => item.objectKind === 'view');
  if (viewOnly) {
    const viewDraft = await request.post(
      `${apiBaseUrl}/api/v1/code-generation/catalog/migration-draft`,
      {
        headers,
        data: { tableName: viewOnly.objectName }
      }
    );
    expect(viewDraft.status()).toBe(404);
  }
});

test('UI：数据库目录页（清单 52，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '数据库目录页仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /数据库目录/);

  await expect(page.getByText('数据库目录', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });
  await expect(
    page.getByText('只读浏览当前 Host 数据库的基础表与视图', { exact: false })
  ).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
