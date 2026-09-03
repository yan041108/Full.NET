import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  createSettingsConfigEntryViaApi,
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

function uniqueConfigKey(clientKind, prefix) {
  // 双端并行时用时间戳与客户端后缀保证 Host 全局唯一配置键。
  const stamp = Date.now().toString(36);
  const suffix = clientKind === 'layui' ? 'l' : 'v';
  return `${prefix}.${stamp}.${suffix}`;
}

test('Host 管理员可从真实 API 加载系统配置项', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const configKey = uniqueConfigKey(clientKind, 'e2e.config');
  await createSettingsConfigEntryViaApi(request, clientKind, {
    configKey,
    displayName: `真实栈配置 ${clientKind}`,
    description: 'real-stack',
    valueKind: 'string',
    value: 'hello',
    displayOrder: 42
  });

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /系统配置/);

  const configEntriesView = clientKind === 'layui'
    ? page.locator('[data-route-view="config-entries"]')
    : page.locator('.config-entries-view');

  await expect(configEntriesView.getByRole('heading', { name: '系统配置', exact: true })).toBeVisible();
  const configRow = configEntriesView.getByRole('article').filter({ hasText: configKey });
  await expect(configRow).toBeVisible();
  await expect(configRow.getByText(`真实栈配置 ${clientKind}`, { exact: true })).toBeVisible();
});

test('受限 Host 账号访问系统配置 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/settings/config-entries?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /系统配置/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'settings/config-entries'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
