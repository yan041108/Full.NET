import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  createSettingsTenantDictItemViaApi,
  createSettingsTenantDictTypeViaApi,
  enterDevelopmentTenant,
  enterTenantAccessToken,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function uniqueCode(clientKind, prefix) {
  const stamp = Date.now().toString(36);
  const suffix = clientKind === 'layui' ? 'l' : 'v';
  return `${prefix}_${stamp}_${suffix}`;
}

test('Host 管理员在租户上下文中可从真实 API 加载并创建租户数据字典', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const typeCode = uniqueCode(clientKind, 'e2e_tdict');
  const itemValue = uniqueCode(clientKind, 'e2e_titem');
  const dictType = await createSettingsTenantDictTypeViaApi(request, clientKind, {
    code: typeCode,
    name: `真实栈租户字典 ${clientKind}`,
    description: 'real-stack',
    displayOrder: 42
  });
  await createSettingsTenantDictItemViaApi(request, clientKind, dictType.id, {
    label: '真实栈租户项',
    value: itemValue,
    color: '#409eff',
    displayOrder: 1
  });

  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);

  await clickMainNavLink(page, /租户数据字典/);

  const dictTypesView = clientKind === 'layui'
    ? page.locator('[data-route-view="tenant-dict-types"]')
    : page.locator('.dict-types-view');

  await expect(dictTypesView.getByRole('heading', { name: '数据字典', exact: true })).toBeVisible();
  const dictTypeRow = dictTypesView.getByRole('article').filter({ hasText: typeCode });
  await expect(dictTypeRow).toBeVisible();
  await expect(dictTypeRow.getByText(`真实栈租户字典 ${clientKind}`, { exact: true })).toBeVisible();

  await dictTypeRow
    .getByRole('button', { name: '字典项', exact: true })
    .click();
  const itemsPanel = clientKind === 'layui'
    ? dictTypesView.locator('[data-tenant-dict-items-panel]')
    : dictTypesView.locator('[data-dict-items-panel]');
  await expect(itemsPanel).toBeVisible();
  await expect(itemsPanel.getByText('真实栈租户项', { exact: true })).toBeVisible();
  await expect(itemsPanel.locator('code', { hasText: itemValue })).toBeVisible();
});

test('受限 Host 账号在租户上下文中访问租户数据字典 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const hostToken = await loginAccessToken(request, clientKind);
  const tenantToken = await enterTenantAccessToken(request, clientKind, hostToken);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/settings/tenant-dict-types?page=1&pageSize=20`,
    {
      headers: {
        Authorization: `Bearer ${tenantToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  await enterDevelopmentTenant(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /租户数据字典/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'settings/tenant-dict-types'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
