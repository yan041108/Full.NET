import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  crudTableRow,
  findSeedAdminUserViaApi,
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

function apiKeysView(page, clientKind) {
  return clientKind === 'layui'
    ? page.locator('[data-route-view="api-keys"]')
    : page.locator('.api-keys-view');
}

function secretCodeLocator(view, clientKind) {
  return clientKind === 'vue'
    ? view.locator('[data-testid="api-key-secret"] code')
    : view.locator('[data-api-keys-secret] code');
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

async function readSecretFromView(view, clientKind) {
  const code = secretCodeLocator(view, clientKind);
  await expect(code).toBeVisible({ timeout: 15_000 });
  const secret = await code.textContent();
  expect(secret?.startsWith('fnk_')).toBeTruthy();
  return secret;
}

async function authenticateUsersApi(request, clientKind, secret) {
  return request.get(`${apiBaseUrl}/api/v1/identity/users?page=1&pageSize=1`, {
    headers: {
      Authorization: `ApiKey ${secret}`,
      Origin: adminOrigin(clientKind)
    }
  });
}

async function fillPermissions(view, clientKind, value) {
  if (clientKind === 'vue') {
    const dialog = view.page().getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByTestId('api-key-permissions').fill(value);
    return;
  }
  await view.locator('textarea[name="permissions"]').fill(value);
}

async function openCreateDialog(view, clientKind) {
  if (clientKind === 'vue') {
    await view.getByTestId('api-keys-action-create').click();
    const dialog = view.page().getByRole('dialog');
    await expect(dialog).toBeVisible();
    return dialog;
  }
  return view;
}

test('Host 管理员可从真实 API 加载 API Key 列表', async ({ page }) => {
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /API Key/i);

  await expect(page.getByRole('heading', { name: 'API Key', exact: true })).toBeVisible();
  await expect(page.locator('.api-keys-view')).toBeVisible();
});

test('Host 管理员可通过 UI 完成创建、轮换、认证与禁用', async ({
  page,
  request
}, testInfo) => {
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const displayName = `真实栈密钥 ${clientKind} ${Date.now().toString(36)}`;
  const adminUser = await findSeedAdminUserViaApi(request, clientKind);

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /API Key/i);

  const view = apiKeysView(page, clientKind);
  await expect(view.getByRole('heading', { name: 'API Key', exact: true })).toBeVisible();

  const editor = await openCreateDialog(view, clientKind);
  if (clientKind === 'vue') {
    await editor.getByTestId('api-key-user-id').fill(adminUser.id);
    await editor.getByTestId('api-key-display-name').fill(displayName);
    await fillPermissions(view, clientKind, 'identity.users.read');
    await editor.getByTestId('api-keys-editor-submit').click();
  } else {
    await editor.getByLabel('用户 ID', { exact: true }).fill(adminUser.id);
    await editor.getByLabel('显示名称', { exact: true }).fill(displayName);
    await fillPermissions(view, clientKind, 'identity.users.read');
    await editor.getByRole('button', { name: '创建', exact: true }).click();
  }

  const initialSecret = await readSecretFromView(view, clientKind);
  expect(await authenticateUsersApi(request, clientKind, initialSecret).then(r => r.status()))
    .toBe(200);

  const rowByName = crudTableRow(view, clientKind, displayName);
  const refreshButton = clientKind === 'vue'
    ? view.getByRole('button', { name: '刷新' })
    : view.locator('[data-api-keys-refresh]');

  await expect.poll(async () => {
    if (clientKind === 'vue') {
      await refreshButton.click();
    } else {
      await refreshButton.click();
    }
    return await rowByName.count();
  }, { timeout: 30_000 }).toBeGreaterThan(0);
  await expect(rowByName.filter({ hasText: '有效' })).toBeVisible({ timeout: 15_000 });

  await rowByName.filter({ hasText: '有效' }).getByRole('button', { name: '轮换', exact: true }).click();
  await confirmLayerPrimary(page, clientKind, '轮换');

  const rotatedSecret = await readSecretFromView(view, clientKind);
  expect(rotatedSecret).not.toBe(initialSecret);
  expect(await authenticateUsersApi(request, clientKind, initialSecret).then(r => r.status()))
    .toBe(401);
  expect(await authenticateUsersApi(request, clientKind, rotatedSecret).then(r => r.status()))
    .toBe(200);

  await rowByName.filter({ hasText: '有效' }).getByRole('button', { name: '禁用', exact: true }).click();
  await confirmLayerPrimary(page, clientKind, '禁用');
  await expect(rowByName.filter({ hasText: '有效' })).toHaveCount(0, { timeout: 15_000 });
  await expect(rowByName.getByText('已禁用', { exact: true })).toHaveCount(2, { timeout: 15_000 });

  await expect.poll(
    () => authenticateUsersApi(request, clientKind, rotatedSecret).then(r => r.status()),
    { timeout: 15_000 }
  ).toBe(401);
});

test('受限 Host 账号访问 API Key API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/identity/api-keys?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /API Key/i })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'identity/api-keys'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
