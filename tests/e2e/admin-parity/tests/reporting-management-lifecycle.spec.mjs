import { expect, test } from '@playwright/test';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const date = '2026-10-10T00:00:00Z';
const source = { id, tenantId: null, name: '管理数据源', providerKey: 'sql_server', serverHost: 'private-db',
  port: 1433, databaseName: 'private-database', username: 'private-user', hasPassword: true,
  trustServerCertificate: false, isEnabled: true, lastTestedAtUtc: null, lastTestStatusKey: null,
  lastTestMessage: null, createdAtUtc: date, updatedAtUtc: null, version: 1 };
const directory = { items: [{ ...source, maskedDatabaseName: 'private-***', maskedServerEndpoint: 'private-***',
  maskedUsername: 'private-***' }], page: 1, pageSize: 20, total: 1 };
const definition = { id, groupId: id, dataSourceId: id, definitionKey: 'fixture', name: '管理报表',
  description: null, queryPortKey: 'fixture', parameterSchema: [], layoutConfigJson: '{}',
  latestPublishedVersionNumber: 1, isEnabled: true, createdAtUtc: date, updatedAtUtc: null, version: 1 };
const json = (route, value) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });

async function boot(page) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host',
    tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false,
    permissions: ['reporting.data_sources.read', 'reporting.data_sources.create', 'reporting.data_sources.update',
      'reporting.data_sources.delete', 'reporting.definitions.read', 'reporting.definitions.update',
      'reporting.definitions.publish', 'reporting.definitions.delete', 'reporting.groups.read'],
    sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, ['data-sources', 'definitions'].map((kind, order) => ({
    id: kind, parentId: null, routeName: `reporting-${kind}`, path: `/reporting/${kind}`,
    componentKey: `reporting-${kind}`, title: kind === 'data-sources' ? '数据源管理' : '报表管理', caption: '', icon: 'document', order,
    requiredPermission: kind === 'data-sources' ? 'reporting.data_sources.read' : 'reporting.definitions.read', children: [] }))));
  await page.route('**/api/v1/reporting/data-sources?*', route => json(route, directory));
  await page.route('**/api/v1/reporting/groups', route => json(route, [{ id, parentId: null, name: '管理分组',
    sortOrder: 0, isEnabled: true, createdAtUtc: date, updatedAtUtc: null, version: 1 }]));
  await page.route('**/api/v1/reporting/definitions', route => json(route, [definition]));
  await page.route('**/api/v1/reporting/definitions?*', route => json(route, [definition]));
}

test('数据源真实表单校验、重复提交、确认取消和重试', async ({ page }) => {
  await boot(page); let creates = 0; let deletes = 0; let release;
  const waiting = new Promise(resolve => { release = resolve; });
  await page.route('**/api/v1/reporting/data-sources', async route => {
    creates++; expect(route.request().postDataJSON().password).toBe('not-a-real-secret');
    await waiting; await json(route, source);
  });
  await page.route(`**/api/v1/reporting/data-sources/${id}`, route => { deletes++; return json(route, true); });
  await page.goto('/#/reporting/data-sources'); await page.getByTestId('reporting-data-source-create').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: '确定', exact: true }).click();
  await expect(dialog.locator('.el-form-item__error').first()).toBeVisible(); expect(creates).toBe(0);
  for (const [label, value] of [['名称', '新增数据源'], ['服务器主机', 'localhost'], ['数据库', 'app'], ['只读账号', 'user'], ['密码', 'not-a-real-secret']]) {
    await dialog.locator('.el-form-item').filter({ has: page.locator('label').filter({ hasText: new RegExp(`^${label}$`) }) }).locator('input').fill(value);
  }
  await dialog.getByRole('button', { name: '确定', exact: true }).evaluate(button => { button.click(); button.click(); });
  await expect.poll(() => creates).toBe(1); release(); await expect(dialog).toHaveCount(0);
  await page.getByTestId('reporting-data-source-delete').click();
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click(); expect(deletes).toBe(0);
  await page.getByTestId('reporting-data-source-delete').click(); await page.getByTestId('reporting-data-source-confirm').click();
  await expect.poll(() => deletes).toBe(1); await expect(page.getByTestId('reporting-data-source-confirm')).toHaveCount(0);
});

test('离开数据源页取消详情读取，迟到凭据不能弹出；再次进入仍可编辑', async ({ page }) => {
  await boot(page); let reads = 0; let release; let finished = false;
  const waiting = new Promise(resolve => { release = resolve; });
  await page.route(`**/api/v1/reporting/data-sources/${id}`, async route => {
    reads++; if (reads === 1) await waiting;
    await json(route, source).catch(() => {}); if (reads === 1) finished = true;
  });
  await page.goto('/#/reporting/data-sources'); await page.getByTestId('reporting-data-source-edit').click();
  await expect.poll(() => reads).toBe(1);
  await page.getByRole('menuitem', { name: '报表管理', exact: true }).click();
  await expect(page).toHaveURL(/reporting\/definitions/u); release(); await expect.poll(() => finished).toBe(true);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByRole('menuitem', { name: '数据源管理', exact: true }).click();
  await page.getByTestId('reporting-data-source-edit').click(); await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByRole('dialog').locator('.el-form-item').filter({ has: page.locator('label').filter({ hasText: /^名称$/u }) }).locator('input')).toHaveValue(source.name);
  await expect(page.getByRole('dialog').locator('input[type=password]')).toHaveValue('');
});

test('报表发布防重、版本错误可见、关闭编辑清除发布备注', async ({ page }) => {
  await boot(page); let publishes = 0; let release;
  const waiting = new Promise(resolve => { release = resolve; });
  await page.route(`**/api/v1/reporting/definitions/${id}/publish`, async route => {
    publishes++; expect(route.request().postDataJSON().changeNote).toBeNull(); await waiting;
    await json(route, { id, definitionId: id, versionNumber: 2, dataSourceId: id, queryPortKey: 'fixture',
      parameterSchema: [], layoutConfigJson: '{}', changeNote: null, publishedByUserId: id, publishedAtUtc: date });
  });
  await page.route(`**/api/v1/reporting/definitions/${id}/versions`, route => route.fulfill({ status: 503,
    contentType: 'application/problem+json', body: JSON.stringify({ status: 503, code: 'fixture.unavailable', title: '版本服务不可用' }) }));
  await page.goto('/#/reporting/definitions'); await page.getByTestId('reporting-definition-edit').click();
  await page.getByTestId('reporting-publish-note').fill('未提交的草稿备注');
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  await page.getByTestId('reporting-definition-publish').evaluate(button => { button.click(); button.click(); });
  await expect.poll(() => publishes).toBe(1); release(); await expect(page.locator('.el-message--success')).toBeVisible();
  await page.getByTestId('reporting-definition-versions').click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.locator('.reporting-definitions-view > .el-alert')).toContainText('版本服务不可用');
});
