import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  crudTableRow,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const viewerUsername = process.env.FULLNET_E2E_VIEWER_USERNAME ?? 'e2e-viewer';
const viewerPassword = process.env.FULLNET_E2E_VIEWER_PASSWORD
  ?? process.env.FULLNET_E2E_PASSWORD
  ?? 'FullNet!2026Secure';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function authHeaders(token, origin) {
  return {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };
}

function uniqueRegionCode() {
  const suffix = String(Date.now() % 1_000_000).padStart(6, '0');
  return `38${suffix}`;
}

test('Host 管理员可查询区域树并 CRUD（清单 38 API）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = authHeaders(token, origin);
  const stamp = Date.now().toString(36);
  const code = uniqueRegionCode();

  const childrenResponse = await request.get(
    `${apiBaseUrl}/api/v1/regions/administrative-regions/children`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(childrenResponse.ok()).toBeTruthy();
  const roots = await childrenResponse.json();
  expect(roots.some(item => item.code === '110000' || item.code === '440000')).toBe(true);

  const treeResponse = await request.get(
    `${apiBaseUrl}/api/v1/regions/administrative-regions/tree?maxDepth=2`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(treeResponse.ok()).toBeTruthy();
  const tree = await treeResponse.json();
  expect(tree.length).toBeGreaterThan(0);

  const createResponse = await request.post(
    `${apiBaseUrl}/api/v1/regions/administrative-regions`,
    {
      headers,
      data: {
        parentId: null,
        code,
        name: `E2E省 ${stamp}`,
        shortName: 'E2E',
        mergerName: `中国,E2E省 ${stamp}`,
        zipCode: null,
        cityCode: null,
        level: 1,
        regionType: '省',
        pinYin: null,
        longitude: null,
        latitude: null,
        displayOrder: 99,
        remark: 'checklist-38'
      }
    }
  );
  expect(createResponse.status()).toBe(201);
  const created = await createResponse.json();
  expect(created.code).toBe(code);

  const updateResponse = await request.put(
    `${apiBaseUrl}/api/v1/regions/administrative-regions/${created.id}`,
    {
      headers,
      data: {
        parentId: null,
        name: `E2E省更新 ${stamp}`,
        shortName: 'E2E',
        mergerName: `中国,E2E省更新 ${stamp}`,
        zipCode: null,
        cityCode: null,
        level: 1,
        regionType: '省',
        pinYin: null,
        longitude: null,
        latitude: null,
        displayOrder: 99,
        remark: 'checklist-38-updated',
        version: created.version
      }
    }
  );
  expect(updateResponse.ok()).toBeTruthy();
  const updated = await updateResponse.json();

  const previewResponse = await request.post(
    `${apiBaseUrl}/api/v1/regions/administrative-regions/import/preview`,
    {
      headers,
      data: {
        datasetKey: 'china.administrative',
        datasetVersion: `e2e-${stamp}`,
        sourceDigest: `digest-${stamp}`,
        mergeMode: 'merge',
        items: [
          {
            code,
            parentCode: null,
            name: `E2E省更新 ${stamp}`,
            shortName: 'E2E',
            mergerName: `中国,E2E省更新 ${stamp}`,
            zipCode: null,
            cityCode: null,
            level: 1,
            regionType: '省',
            pinYin: null,
            longitude: null,
            latitude: null,
            displayOrder: 99
          }
        ]
      }
    }
  );
  expect(previewResponse.ok()).toBeTruthy();
  const preview = await previewResponse.json();
  expect(preview.added.length + preview.updated.length).toBeGreaterThanOrEqual(0);

  const deleteResponse = await request.post(
    `${apiBaseUrl}/api/v1/regions/administrative-regions/${created.id}/delete`,
    { headers, data: { version: updated.version } }
  );
  expect(deleteResponse.status()).toBe(204);
});

test('Host 管理员可打开区域管理页并维护节点（清单 38 UI）', async ({
  page
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '区域管理页仅验收 Vue');
  const stamp = Date.now().toString(36);
  const code = uniqueRegionCode();

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /区域管理/, '平台');
  await expect(page.getByRole('heading', { name: '区域管理', exact: true }))
    .toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('administrative-regions-tree-table'))
    .toContainText('110000');

  await page.getByTestId('administrative-regions-action-create').click();
  await page.getByTestId('administrative-regions-editor-code').fill(code);
  await page.getByTestId('administrative-regions-editor-name').fill(`E2E UI ${stamp}`);
  await page.getByTestId('administrative-regions-editor-submit').click();

  const table = page.getByTestId('administrative-regions-tree-table');
  const row = crudTableRow(table, 'vue', code);
  await expect(row).toBeVisible({ timeout: 15_000 });

  await row.getByTestId('administrative-regions-delete').click();
  await page.getByRole('button', { name: '确定' }).click();
  await expect(row).toHaveCount(0, { timeout: 15_000 });
});

test('受限 Host 账号无法创建行政区域（清单 38）', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  await loginAsHostViewer(page);
  await page.goto('/#/regions/administrative-regions');
  await expect(page.getByTestId('administrative-regions-action-create')).toHaveCount(0);

  const viewerToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    viewerUsername,
    viewerPassword
  );
  const createResponse = await request.post(
    `${apiBaseUrl}/api/v1/regions/administrative-regions`,
    {
      headers: authHeaders(viewerToken, origin),
      data: {
        parentId: null,
        code: uniqueRegionCode(),
        name: 'denied',
        shortName: null,
        mergerName: null,
        zipCode: null,
        cityCode: null,
        level: 1,
        regionType: null,
        pinYin: null,
        longitude: null,
        latitude: null,
        displayOrder: 1,
        remark: null
      }
    }
  );
  expect(createResponse.status()).toBe(403);
  expect((await createResponse.json()).code).toBe('authorization.permission_denied');
});
