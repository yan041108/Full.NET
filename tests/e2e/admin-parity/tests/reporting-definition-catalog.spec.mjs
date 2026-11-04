import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
test.afterEach(async ({ page }, info) => { await page.screenshot({ path: info.outputPath('page.png'), fullPage: true }); });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', parent = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const createdAtUtc = '2026-10-07T00:00:00Z';
const group = { id, parentId: parent, name: '子分组', sortOrder: 3, isEnabled: true, createdAtUtc, updatedAtUtc: null, version: 4 };
const lastId = '019bc2b1-2a40-7cc3-8992-a80de51bf297';
const source = { id: lastId, tenantId: null, name: '末页数据源', providerKey: 'sql_server', maskedServerEndpoint: 'fixture-***',
  maskedDatabaseName: 'fixture-***', maskedUsername: 'fixture-***', hasPassword: true, trustServerCertificate: false, isEnabled: true,
  lastTestedAtUtc: null, lastTestStatusKey: null, lastTestMessage: null, createdAtUtc, updatedAtUtc: null, version: 1 };
const first = Array.from({ length: 100 }, (_, index) => ({ ...source, id: '019bc2b1-2a40-7cc3-8992-' + (index + 1000).toString(16).padStart(12, '0'), name: '数据源 ' + index }));
const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
async function boot(page, waitForSecondPage, extraPermissions = []) {
  const sourcePages = [];
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1,
    permissions: ['reporting.definitions.read', 'reporting.definitions.create', 'reporting.groups.read', 'reporting.groups.update', 'reporting.query_ports.read', 'reporting.data_sources.read', ...extraPermissions] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'definitions', parentId: null, routeName: 'reporting-definitions', path: '/reporting/definitions',
    componentKey: 'reporting-definitions', title: '报表配置', caption: '', icon: 'document', order: 1, requiredPermission: 'reporting.definitions.read', children: [] }]));
  await page.route('**/api/v1/reporting/groups', route => json(route, [group, { ...group, id: parent, parentId: null, name: '父分组' }]));
  await page.route('**/api/v1/reporting/query-ports', route => json(route, [{ queryPortKey: 'metadata.schemas', displayName: '架构目录', description: '', supportedProviderKeys: ['sql_server'], parameters: [] }]));
  await page.route('**/api/v1/reporting/definitions?*', route => json(route, { items: [], page: 1, pageSize: 20, total: 0 }));
  await page.route('**/api/v1/reporting/data-sources?*', route => {
    const query = new URL(route.request().url()).searchParams; expect(query.get('pageSize')).toBe('100');
    const current = Number(query.get('page')); sourcePages.push(current);
    const result = { items: current === 1 ? first : [source], page: current, pageSize: 100, total: 101 };
    return current === 2 && waitForSecondPage ? waitForSecondPage.then(() => json(route, result)) : json(route, result);
  });
  await page.goto('/#/reporting/definitions');
  await expect.poll(() => sourcePages).toEqual([1, 2]);
  return sourcePages;
}
async function fits(page) {
  await expect.poll(async () => { const box = await page.getByRole('dialog').boundingBox(); return !!box && box.x >= 0 && box.x + box.width <= page.viewportSize().width; }).toBe(true);
}
test('真实数据源选择器可选择第101条摘要并保存正确标识', async ({ page }, info) => {
  await boot(page); let writes = 0;
  await page.route('**/api/v1/reporting/definitions', route => {
    writes++; const body = route.request().postDataJSON(); expect(body.dataSourceId).toBe(lastId); expect(body.groupId).toBe(id);
    return json(route, { ...body, id: parent, latestPublishedVersionNumber: 0, createdAtUtc, updatedAtUtc: null, version: 1 });
  });
  await page.getByTestId('reporting-definition-create').click();
  await page.getByTestId('reporting-config-key').fill('catalog-fixture'); await page.getByTestId('reporting-config-name').fill('末页选择验收');
  await page.getByRole('dialog').locator('.el-select').nth(1).click();
  await page.getByRole('option', { name: '末页数据源', exact: true }).click();
  await expect(page.getByRole('option', { name: '末页数据源', exact: true })).toBeHidden();
  await fits(page); await page.screenshot({ path: info.outputPath('definition-open.png'), fullPage: true });
  await page.getByTestId('reporting-config-draft-save').click();
  await expect.poll(() => writes).toBe(1); await expect(page.getByRole('dialog')).toHaveCount(0);
});
test('真实子分组编辑保留父组与版本，窄屏和键盘可操作', async ({ page }, info) => {
  await boot(page); let writes = 0;
  await page.route('**/api/v1/reporting/groups/' + id, route => {
    writes++; const body = route.request().postDataJSON(); expect(body).toEqual({ parentId: parent, name: '修改后的子分组', sortOrder: 3, isEnabled: true, version: 4 });
    return json(route, { ...group, name: body.name, version: 5 });
  });
  await page.getByRole('button', { name: '编辑', exact: true }).first().click();
  const dialog = page.getByRole('dialog'); await dialog.locator('input').first().fill('修改后的子分组');
  await fits(page);
  await expect.poll(async () => (await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations.map(item => item.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath('group-open.png'), fullPage: true });
  await page.getByTestId('reporting-config-group-save').focus(); await page.keyboard.press('Enter');
  await expect.poll(() => writes).toBe(1); await expect(dialog).toHaveCount(0);
});

test('末页读取完成前不开放新建，完成后恢复当前选择', async ({ page }) => {
  let release; const pending = new Promise(resolve => { release = resolve; });
  await boot(page, pending);
  await expect(page.getByTestId('reporting-definition-create')).toBeDisabled();
  release(); await expect(page.getByTestId('reporting-definition-create')).toBeEnabled();
  await page.getByTestId('reporting-definition-create').click();
  await expect(page.getByRole('dialog')).toBeVisible(); await fits(page);
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
});
test('编辑器内刷新保留自定义输入，端口变化经显式参数重置后才允许保存', async ({ page }, info) => {
  await boot(page, undefined, ['reporting.definitions.update']); let required = false, unavailable = false, saved;
  const parameter = () => ({ parameterKey: 'year', displayName: '年度', dataTypeKey: 'string', isRequired: required, defaultValue: '2026' });
  const draft = { id: lastId, groupId: id, dataSourceId: lastId, definitionKey: 'port-recovery', name: '参数恢复定义', description: null, queryPortKey: 'metadata.schemas',
    parameterSchema: [{ ...parameter(), displayName: '草稿自定义', defaultValue: '2027' }], layoutConfigJson: '{}', isEnabled: true, latestPublishedVersionNumber: 0, createdAtUtc, updatedAtUtc: null, version: 4 };
  const { parameterSchema: _, layoutConfigJson: __, ...summary } = draft;
  await page.route('**/api/v1/reporting/definitions?*', route => json(route, { items: [summary], page: 1, pageSize: 20, total: 1 }));
  await page.route('**/api/v1/reporting/definitions/' + lastId, route => {
    if (route.request().method() === 'GET') return json(route, draft);
    saved = route.request().postDataJSON(); return json(route, { ...draft, ...saved, version: 5 });
  });
  await page.route('**/api/v1/reporting/query-ports', route => unavailable
    ? route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, code: 'fixture.catalog', title: '端口目录暂不可用' }) })
    : json(route, [{ queryPortKey: 'metadata.schemas', displayName: '架构目录', description: '', supportedProviderKeys: ['sql_server'], parameters: [{ ...parameter(), minimum: null, maximum: null }] }]));
  await page.getByTestId('reporting-config-refresh').click(); await page.getByTestId('reporting-config-edit').click();
  const dialog = page.getByRole('dialog'); await page.getByTestId('reporting-config-name').fill('保留名称');
  await expect(dialog.locator('.el-table input').nth(1)).toHaveValue('2027'); unavailable = true;
  await page.getByTestId('reporting-editor-catalog-refresh').click(); await expect(page.getByTestId('reporting-config-draft-save')).toHaveCount(0);
  await expect(page.getByTestId('reporting-config-name')).toHaveValue('保留名称'); await expect(dialog.locator('.el-table input').nth(1)).toHaveValue('2027');
  unavailable = false; required = true; await page.getByTestId('reporting-editor-catalog-refresh').click();
  await expect(dialog).toContainText('当前分组、数据源或参数与查询端口不匹配'); await expect(page.getByTestId('reporting-config-draft-save')).toHaveCount(0);
  await page.getByTestId('reporting-parameters-reset').click(); const prompt = page.getByRole('dialog').filter({ has: page.getByTestId('reporting-management-confirm') });
  await prompt.getByRole('button', { name: '取消', exact: true }).click(); await expect(dialog.locator('.el-table input').nth(1)).toHaveValue('2027');
  await page.getByTestId('reporting-parameters-reset').click(); await page.getByTestId('reporting-management-confirm').click();
  await expect(dialog.locator('.el-table input').nth(1)).toHaveValue('2026'); await expect(page.getByTestId('reporting-config-name')).toHaveValue('保留名称');
  await expect(page.getByTestId('reporting-config-draft-save')).toBeVisible(); expect(saved).toBeUndefined();
  await page.screenshot({ path: info.outputPath('catalog-recovery.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('reporting-config-draft-save').click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(saved).toMatchObject({ name: '保留名称', version: 4, groupId: id, parameterSchema: [{ parameterKey: 'year', displayName: '年度', dataTypeKey: 'string', isRequired: true, defaultValue: '2026' }] });
  await expect(page.getByTestId('reporting-draft-receipt')).toContainText('草稿保存已确认');
});
