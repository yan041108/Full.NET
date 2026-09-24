import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  createHostUserViaApi,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const adminPassword = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const targetPassword = 'FullNet!2026SaTarget';

test.describe.configure({ mode: 'serial' });

async function listSuperAdministrators(request, clientKind) {
  const origin = adminOrigin(clientKind);
  const adminToken = await loginHostAdminAccessToken(request, clientKind);
  const headers = { Authorization: `Bearer ${adminToken}`, Origin: origin };
  const listResponse = await request.get(`${apiBaseUrl}/api/v1/identity/super-administrators`, {
    headers
  });
  expect(listResponse.status()).toBe(200);
  return { administrators: await listResponse.json(), headers, origin, adminToken };
}

async function revokeSuperAdministratorViaApi(request, userId, headers) {
  const revokeResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/super-administrators/${userId}/revoke`,
    {
      data: { currentPassword: adminPassword },
      headers: { ...headers, 'Content-Type': 'application/json' }
    }
  );
  return revokeResponse;
}

/** 真实栈偶发残留额外超管时，先撤销非 bootstrap admin 直至仅剩一名。 */
async function ensureSingleActiveSuperAdministrator(request, clientKind) {
  let { administrators, headers } = await listSuperAdministrators(request, clientKind);
  while (administrators.length > 1) {
    const extra = administrators.find(item => item.username !== 'admin') ?? administrators[1];
    const revokeResponse = await revokeSuperAdministratorViaApi(request, extra.userId, headers);
    expect(revokeResponse.status(), await revokeResponse.text()).toBe(200);
    ({ administrators, headers } = await listSuperAdministrators(request, clientKind));
  }
  expect(administrators.length).toBe(1);
  return administrators[0];
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

async function openSuperAdministratorsPage(page) {
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /超级管理员/, '系统管理');
  await expect(page.getByRole('heading', { name: '超级管理员', exact: true })).toBeVisible();
  await expect(page.getByTestId('super-admin-action-grant')).toBeVisible();
}

async function grantSuperAdministratorViaUi(page, targetUsername, password) {
  await page.getByTestId('super-admin-action-grant').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Host 账号', { exact: true }).fill(targetUsername);
  await dialog.getByLabel('当前密码', { exact: true }).fill(password);
  await dialog.getByTestId('super-admin-grant-submit').click();
  await expect(dialog).toBeHidden({ timeout: 20_000 });
}

async function filterSuperAdministratorTable(page, username) {
  const searchBar = page.locator('.super-admin-view .art-search-bar');
  await searchBar.getByPlaceholder('搜索用户名').fill(username);
  await searchBar.getByRole('button', { name: '查询' }).click();
  const row = page.locator('.el-table__body-wrapper .el-table__row').filter({ hasText: username });
  await expect(row).toHaveCount(1, { timeout: 15_000 });
  return row;
}

async function revokeSuperAdministratorViaUi(page, targetUsername, password) {
  const row = await filterSuperAdministratorTable(page, targetUsername);
  // 底部分页条在真实栈会挡住行内操作按钮，DOM click 与用户对图标的意图一致。
  await row.getByTestId('super-admin-action-revoke').evaluate(button => button.click());
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible({ timeout: 15_000 });
  await expect(dialog.getByLabel('目标账号')).toHaveValue(targetUsername);
  await dialog.getByLabel('当前密码', { exact: true }).fill(password);
  await dialog.getByTestId('super-admin-revoke-submit').click();
}

test('Host 管理员可从真实 API 加载超管目录', async ({ page, request }, testInfo) => {
  test.setTimeout(60_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const { administrators } = await listSuperAdministrators(request, clientKind);
  expect(administrators.length).toBeGreaterThanOrEqual(1);

  await openSuperAdministratorsPage(page);
  await expect(page.getByText('admin', { exact: true }).first()).toBeVisible({ timeout: 15_000 });
});

test('撤销最后一名超级管理员时 API 与 Vue 对话框均返回稳定错误码', async ({
  page,
  request
}, testInfo) => {
  test.setTimeout(90_000);
  const clientKind = testInfo.project.metadata.clientKind;
  test.skip(clientKind === 'layui', 'Layui 管理端已冻结，最后一名保护只验收 Vue。');

  const lastAdmin = await ensureSingleActiveSuperAdministrator(request, clientKind);
  const { headers } = await listSuperAdministrators(request, clientKind);

  const revokeResponse = await revokeSuperAdministratorViaApi(
    request,
    lastAdmin.userId,
    headers
  );
  expect(revokeResponse.status()).toBe(403);
  const problem = await revokeResponse.json();
  expect(problem.code).toBe('identity.super_administrator.last_remaining');

  await openSuperAdministratorsPage(page);
  const adminUsername = lastAdmin.username;
  await revokeSuperAdministratorViaUi(page, adminUsername, adminPassword);
  const alert = page.locator('.art-inline-alert[role="alert"]');
  await expect(alert).toBeVisible({ timeout: 15_000 });
  await expect(alert.locator('strong')).toHaveText('identity.super_administrator.last_remaining');
});

test('Host 管理员可通过 Vue 对话框完成密码重认证授予与撤销', async ({ page, request }, testInfo) => {
  test.setTimeout(120_000);
  test.skip(testInfo.project.metadata.clientKind === 'layui', 'Layui 管理端已冻结，超管 UI 只验收 Vue。');

  const clientKind = testInfo.project.metadata.clientKind;
  await ensureSingleActiveSuperAdministrator(request, clientKind);

  const targetUsername = `sa-ui-${Date.now().toString(36)}`;
  await createHostUserViaApi(request, clientKind, {
    username: targetUsername,
    displayName: '超管 UI 授予目标',
    password: targetPassword
  });

  await openSuperAdministratorsPage(page);
  await grantSuperAdministratorViaUi(page, targetUsername, adminPassword);
  await expect(page.getByText(targetUsername, { exact: true }).first()).toBeVisible({
    timeout: 15_000
  });

  await revokeSuperAdministratorViaUi(page, targetUsername, adminPassword);
  await expect(page.getByText(targetUsername, { exact: true })).toHaveCount(0, {
    timeout: 20_000
  });
  const { administrators } = await listSuperAdministrators(request, clientKind);
  expect(administrators.some(item => item.username === 'admin')).toBe(true);
  expect(administrators.some(item => item.username === targetUsername)).toBe(false);
});
