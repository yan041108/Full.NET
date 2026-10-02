import { expect, test } from '@playwright/test';
import { createLoggingDetailsFixture } from './support/logging-details-fixture.mjs';
import { adminOrigin, clickMainNavLink, loginAsHostAdmin, loginAsHostUser, loginHostAdminAccessToken, provisionLimitedHostUserViaApi, trackUiAccessToken } from './support/real-stack-auth.mjs';

const api = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
let fixture;
test.beforeAll(async () => { fixture = await createLoggingDetailsFixture(); });
test.afterAll(async () => { await fixture?.dispose(); });
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
});
async function openList(page) {
  await clickMainNavLink(page, /操作日志/);
  const view = page.locator('.operation-logs-view');
  await view.getByPlaceholder('路径包含', { exact: true }).fill(fixture.prefix);
  const filtered = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.pathname.endsWith('/operation-logs') && url.searchParams.get('pathContains') === fixture.prefix;
  });
  await view.getByRole('button', { name: '查询', exact: true }).click();
  expect((await filtered).status()).toBe(200);
  await expect(view).toHaveAttribute('aria-busy', 'false');
  await expect(view.locator('.el-pagination')).toContainText('23');
  return view;
}
async function openRow(view, index) {
  await view.locator('.el-table__body-wrapper tr').filter({ has: view.page().getByText(`${fixture.actionPrefix}.${index}`, { exact: true }) })
    .getByRole('button', { name: '查看详情', exact: true }).click();
}

test('真实详情三页签按需加载且分页保留筛选', async ({ page }) => {
  await loginAsHostAdmin(page);
  const view = await openList(page);
  const details = [];
  page.on('request', request => { if (/operation-logs\/[^/]+\/details$/.test(new URL(request.url()).pathname)) details.push(request.url()); });
  await openRow(view, 0);
  const drawer = page.getByRole('dialog', { name: `${fixture.actionPrefix}.0`, exact: true });
  await expect(drawer.getByRole('tab', { name: '日志消息', exact: true })).toBeVisible();
  expect(details).toHaveLength(0);
  await drawer.getByRole('tab', { name: '请求参数', exact: true }).click();
  await expect(drawer).toContainText('192.0.2.19');
  await expect(drawer.locator('pre:visible')).toContainText('fromUtc');
  await drawer.getByRole('tab', { name: '返回内容', exact: true }).click();
  await expect(drawer.locator('pre:visible')).toContainText('37');
  expect(details).toHaveLength(1);
  await page.keyboard.press('Escape');
  await expect(drawer).not.toBeVisible();
  const response = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/operation-logs') && new URL(r.url()).searchParams.get('page') === '2');
  await view.locator('.el-pagination .btn-next').click();
  expect((await response).status()).toBe(200);
  await expect(view.locator('.el-table__body-wrapper tr')).toHaveCount(3);
  await expect(view).toContainText(`${fixture.actionPrefix}.22`);
});

test('低视口日志表格保留可操作高度且分页不覆盖数据行', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  await loginAsHostAdmin(page);
  for (const [name, selector] of [
    [/操作日志/, '.operation-logs-view'],
    [/异常日志/, '.exception-logs-view'],
    [/访问日志/, '.access-logs-view']
  ]) {
    await clickMainNavLink(page, name);
    const view = page.locator(selector);
    await expect(view.locator('.el-pagination')).toBeVisible();
    await expect.poll(() => view.evaluate(element => {
      const table = element.querySelector('.art-crud-data-table').getBoundingClientRect();
      const pagination = element.querySelector('.el-pagination').getBoundingClientRect();
      return table.height >= 120 && pagination.top >= table.bottom - 1;
    })).toBe(true);
  }
});

test('到期与历史无详情记录保持摘要且显示不可用', async ({ page, request }, testInfo) => {
  const currentToken = trackUiAccessToken(page);
  await loginAsHostAdmin(page);
  const view = await openList(page);
  const token = currentToken();
  for (const index of [1, 2]) {
    const result = await request.get(`${api}/api/v1/auditing/operation-logs/${fixture.ids[index]}/details`, { headers: { Authorization: `Bearer ${token}`, Origin: adminOrigin('vue-admin') } });
    expect(result.status()).toBe(404);
    await openRow(view, index);
    const drawer = page.getByRole('dialog', { name: `${fixture.actionPrefix}.${index}`, exact: true });
    await drawer.getByRole('tab', { name: '请求参数', exact: true }).click();
    await expect(drawer).toContainText('详情未记录或已到期');
    await expect(drawer).not.toContainText('192.0.2.19');
    await page.keyboard.press('Escape');
    await expect(drawer).not.toBeVisible();
  }
});

test('撤销详情权限后真实会话不再展示受限页签', async ({ page, request }, testInfo) => {
  const currentToken = trackUiAccessToken(page);
  const kind = testInfo.project.metadata.clientKind;
  const permissions = ['platform.dashboard.read', 'identity.navigation.read', 'auditing.operations.read'];
  const account = await provisionLimitedHostUserViaApi(request, kind, { permissionCodes: [...permissions, 'auditing.operations.details.read'] });
  await loginAsHostUser(page, account.username, account.password);
  await openRow(await openList(page), 0);
  await page.getByRole('dialog', { name: `${fixture.actionPrefix}.0`, exact: true }).getByRole('tab', { name: '请求参数', exact: true }).click();
  await expect(page.getByRole('dialog', { name: `${fixture.actionPrefix}.0`, exact: true })).toContainText('192.0.2.19');
  const token = await loginHostAdminAccessToken(request, kind);
  const headers = { Authorization: `Bearer ${token}`, Origin: adminOrigin(kind) };
  const role = await request.get(`${api}/api/v1/identity/roles/${account.roleId}`, { headers });
  expect(role.ok()).toBeTruthy();
  const changed = await request.put(`${api}/api/v1/identity/roles/${account.roleId}/permissions`, { headers, data: { version: (await role.json()).version, permissionCodes: permissions } });
  expect(changed.ok()).toBeTruthy();
  await page.context().clearCookies();
  await page.evaluate(() => { localStorage.clear(); sessionStorage.clear(); });
  await loginAsHostUser(page, account.username, account.password);
  const details = [];
  page.on('request', r => { if (/operation-logs\/[^/]+\/details$/.test(new URL(r.url()).pathname)) details.push(r.url()); });
  await openRow(await openList(page), 0);
  const drawer = page.getByRole('dialog', { name: `${fixture.actionPrefix}.0`, exact: true });
  await expect(drawer.getByRole('tab', { name: '日志消息', exact: true })).toBeVisible();
  await expect(drawer.getByRole('tab', { name: '请求参数', exact: true })).toHaveCount(0);
  await expect(drawer.getByRole('tab', { name: '返回内容', exact: true })).toHaveCount(0);
  await expect(drawer).not.toContainText('192.0.2.19');
  expect(details).toHaveLength(0);
  const denied = await request.get(`${api}/api/v1/auditing/operation-logs/${fixture.ids[0]}/details`, {
    headers: { Authorization: `Bearer ${currentToken()}`, Origin: adminOrigin(kind) }
  });
  expect(denied.status()).toBe(403);
});
