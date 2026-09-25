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

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const catalogKey = 'settings.config_value_kind';
const encodedCatalogKey = encodeURIComponent(catalogKey);

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可从真实 API 加载枚举常量目录', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /枚举常量/);

  const enumCatalogsView = clientKind === 'layui'
    ? page.locator('[data-route-view="enum-catalogs"]')
    : page.locator('.enum-catalogs-view');

  await expect(enumCatalogsView.getByRole('heading', { name: '枚举常量', exact: true })).toBeVisible();
  await expect(enumCatalogsView.getByText('settings.config_value_kind', { exact: true })).toBeVisible();
  await enumCatalogsView.getByRole('row', {
    name: /配置值类型 settings\.config_value_kind/u
  }).getByRole('button', { name: '查看', exact: true }).click();
  await expect(enumCatalogsView.getByRole('cell', {
    name: 'string',
    exact: true
  }).first()).toBeVisible();
});

test('受限 Host 账号访问枚举目录 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/settings/enum-catalogs`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /枚举常量/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'settings/enum-catalogs'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});

test('Host 管理员可预览并生成枚举字典且二次生成幂等（清单 25）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin
  };

  const previewResponse = await request.get(
    `${apiBaseUrl}/api/v1/settings/enum-catalogs/${encodedCatalogKey}/dict-generation-preview`,
    { headers }
  );
  expect(previewResponse.ok()).toBeTruthy();
  const preview = await previewResponse.json();
  expect(preview.catalogKey).toBe(catalogKey);
  expect(preview.dictTypeCode).toBe(catalogKey);
  expect(Array.isArray(preview.items)).toBe(true);
  expect(preview.items.length).toBeGreaterThan(0);

  const generateResponse = await request.post(
    `${apiBaseUrl}/api/v1/settings/enum-catalogs/${encodedCatalogKey}/dict-generation`,
    { headers }
  );
  expect(generateResponse.ok()).toBeTruthy();
  const generated = await generateResponse.json();
  expect(generated.dictTypeCode).toBe(catalogKey);
  expect(generated.dictTypeId).toBeTruthy();

  const previewAgain = await request.get(
    `${apiBaseUrl}/api/v1/settings/enum-catalogs/${encodedCatalogKey}/dict-generation-preview`,
    { headers }
  );
  expect(previewAgain.ok()).toBeTruthy();
  const previewBody = await previewAgain.json();
  expect(previewBody.dictTypeExists).toBe(true);
  expect(previewBody.willCreateDictType).toBe(false);
  const createActions = previewBody.items.filter(item => item.action === 'create');
  expect(createActions).toHaveLength(0);
});

test('Vue 枚举页可打开生成字典预览对话框（清单 25）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '生成字典 UI 仅验收 Vue');
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /枚举常量/);

  const enumCatalogsView = page.locator('.enum-catalogs-view');
  await enumCatalogsView
    .getByRole('row', { name: /配置值类型 settings\.config_value_kind/u })
    .getByRole('button', { name: '查看', exact: true })
    .click();
  await expect(enumCatalogsView.getByTestId('enum-catalogs-action-generate-dict')).toBeVisible();
  await enumCatalogsView.getByTestId('enum-catalogs-action-generate-dict').click();
  await expect(page.getByRole('dialog', { name: '生成字典预览' })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('enum-catalogs-action-confirm-generate-dict')).toBeVisible();
  await page.getByRole('button', { name: '取消', exact: true }).click();
  await expect(page.getByRole('dialog', { name: '生成字典预览' })).toHaveCount(0);
});
