import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessToken,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostUser,
  loginAsHostViewer,
  provisionLimitedHostUserViaApi,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可从真实 API 加载角色列表', async ({ page }) => {
  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /角色管理/);

  await expect(page.getByRole('heading', { name: '角色管理', exact: true })).toBeVisible();
  await expect(page.getByText('宿主管理员', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('host-administrator', { exact: true }).first()).toBeVisible();
});

test('受限 Host 账号访问角色 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/identity/roles?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /角色管理/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'identity/roles'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});

test('Host 管理员在角色授权树可看到用户页面与操作节点', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '授权树 UI 仅验收 Vue 管理端');
  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /角色管理/);

  const customRoleRow = page.locator('.roles-data-table tbody tr').filter({ hasText: 'e2e-host-viewer' });
  if ((await customRoleRow.count()) === 0) {
    const firstCustomRole = page
      .locator('.roles-data-table tbody tr')
      .filter({ hasNotText: 'host-administrator' })
      .first();
    await firstCustomRole.getByTestId('role-open-permissions').click();
  } else {
    await customRoleRow.getByTestId('role-open-permissions').click();
  }

  const tree = page.getByTestId('role-permission-tree');
  await expect(tree).toBeVisible();
  await expect(tree.getByText('用户管理', { exact: true })).toBeVisible();
  await expect(tree.getByText('创建用户', { exact: true })).toBeVisible();
  await expect(tree.getByText('重置密码', { exact: true })).toBeVisible();
});

test('Vue 只读角色可见目录但无业务操作按钮', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '精确按钮权限仅验收 Vue 管理端');
  const limited = await provisionLimitedHostUserViaApi(request, testInfo.project.metadata.clientKind, {
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'identity.roles.read'
    ]
  });

  await loginAsHostUser(page, limited.username, limited.password);
  await clickMainNavLink(page, /角色管理/);

  const view = page.locator('.roles-view');
  await expect(view.getByRole('heading', { name: '角色管理', exact: true })).toBeVisible();
  await expect(view.getByTestId('roles-create-form')).toHaveCount(0);
  await expect(view.getByTestId('roles-action-edit')).toHaveCount(0);
  await expect(view.getByTestId('role-open-permissions')).toHaveCount(0);
  await expect(view.getByTestId('roles-action-data-scope')).toHaveCount(0);
  await expect(view.getByTestId('roles-action-disable')).toHaveCount(0);
});

test('Vue 仅禁用权限用户只显示禁用按钮', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '精确按钮权限仅验收 Vue 管理端');
  const limited = await provisionLimitedHostUserViaApi(request, testInfo.project.metadata.clientKind, {
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'identity.roles.read',
      'identity.roles.disable'
    ]
  });

  await loginAsHostUser(page, limited.username, limited.password);
  await clickMainNavLink(page, /角色管理/);

  const view = page.locator('.roles-view');
  await expect(view.getByRole('heading', { name: '角色管理', exact: true })).toBeVisible();
  await expect(view.getByTestId('roles-create-form')).toHaveCount(0);
  await expect(view.getByTestId('roles-action-edit')).toHaveCount(0);
  await expect(view.getByTestId('role-open-permissions')).toHaveCount(0);
  await expect(view.getByTestId('roles-action-data-scope')).toHaveCount(0);
  await expect.poll(async () => {
    const activeRow = view
      .locator('.roles-data-table tbody tr')
      .filter({ hasText: '有效' })
      .filter({ hasNotText: '系统角色' });
    return await activeRow.count();
  }, { timeout: 15_000 }).toBeGreaterThan(0);
  const activeRow = view
    .locator('.roles-data-table tbody tr')
    .filter({ hasText: '有效' })
    .filter({ hasNotText: '系统角色' })
    .first();
  await expect(activeRow.getByTestId('roles-action-disable')).toBeVisible();
});

test('仅页面读权限调用相邻写 API 返回 authorization.permission_denied', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: ['identity.roles.read']
  });
  const accessToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );

  const response = await request.post(`${apiBaseUrl}/api/v1/identity/roles`, {
    data: {
      code: `denied-${Date.now()}`,
      name: '拒绝创建'
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');
});
