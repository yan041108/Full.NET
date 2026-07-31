import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const defaultPassword = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function uniqueUsername(clientKind) {
  const stamp = Date.now().toString(36);
  const suffix = clientKind === 'layui' ? 'l' : 'v';
  return `e2e_user_${stamp}_${suffix}`;
}

function usersView(page, clientKind) {
  return clientKind === 'layui'
    ? page.locator('[data-route-view="users"]')
    : page.locator('.users-view');
}

async function confirmLayerPrimary(page, clientKind, buttonName) {
  if (clientKind === 'vue') {
    const dialog = page.getByRole('dialog').last();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: buttonName, exact: true }).click();
  } else {
    await page.locator('.layui-layer-btn0').last().click();
  }
}

async function fillPromptInput(page, clientKind, value) {
  if (clientKind === 'vue') {
    const prompt = page.locator('.el-message-box').last();
    await expect(prompt.locator('input')).toBeVisible();
    await prompt.locator('input').fill(value);
    await prompt.locator('input').press('Enter');
    await expect(prompt).toBeHidden();
  } else {
    const layer = page.locator('.layui-layer').last();
    await expect(layer.locator('.layui-layer-input')).toBeVisible();
    await layer.locator('.layui-layer-input').fill(value);
    await layer.locator('.layui-layer-btn0').click({ force: true });
  }
}

test('Host 管理员可从真实 API 加载用户列表', async ({ page }) => {
  await loginAsHostAdmin(page);

  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /用户管理/ })).toBeVisible();
  await navigation.getByRole('link', { name: /用户管理/ }).click();

  await expect(page.getByRole('heading', { name: '用户管理', exact: true })).toBeVisible();
  await expect(page.getByText('系统管理员', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('admin', { exact: true }).first()).toBeVisible();
});

test('Host 管理员可通过 UI 完成用户创建、更新、禁用与启用', async ({
  page,
  request
}, testInfo) => {
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const username = uniqueUsername(clientKind);
  const displayName = `真实栈用户 ${clientKind}`;
  const updatedDisplayName = `真实栈用户已更新 ${clientKind}`;

  await loginAsHostAdmin(page);

  const navigation = page.getByRole('navigation', { name: '主导航' });
  await navigation.getByRole('link', { name: /用户管理/ }).click();

  const view = usersView(page, clientKind);
  await expect(view.getByRole('heading', { name: '用户管理', exact: true })).toBeVisible();

  await view.getByLabel('用户名', { exact: true }).fill(username);
  await view.getByLabel('显示名称', { exact: true }).fill(displayName);
  await view.getByLabel('初始密码', { exact: true }).fill(defaultPassword);
  await view.getByRole('button', { name: '创建用户', exact: true }).click();

  const userRow = view.getByRole('article').filter({ hasText: username });
  await expect(userRow).toBeVisible({ timeout: 15_000 });
  await expect(userRow.getByText(displayName, { exact: true })).toBeVisible();

  await userRow.getByRole('button', { name: '编辑', exact: true }).click();
  await fillPromptInput(page, clientKind, updatedDisplayName);
  await expect(userRow.getByText(updatedDisplayName, { exact: true })).toBeVisible({
    timeout: 15_000
  });

  await userRow.getByRole('button', { name: '禁用', exact: true }).click();
  await confirmLayerPrimary(page, clientKind, '禁用');
  await expect(userRow.getByText('已禁用', { exact: true })).toBeVisible({ timeout: 15_000 });

  const disabledLogin = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    data: { username, password: defaultPassword },
    headers: { Origin: origin, 'Content-Type': 'application/json' }
  });
  expect(disabledLogin.status()).toBe(401);
  const disabledProblem = await disabledLogin.json();
  expect(disabledProblem.code).toBe('identity.invalid_credentials');

  await userRow.getByRole('button', { name: '启用', exact: true }).click();
  await confirmLayerPrimary(page, clientKind, '启用');
  await expect(userRow.getByText('有效', { exact: true })).toBeVisible({ timeout: 15_000 });
});

test('受限 Host 账号访问用户 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/identity/users?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /用户管理/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'identity/users'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
