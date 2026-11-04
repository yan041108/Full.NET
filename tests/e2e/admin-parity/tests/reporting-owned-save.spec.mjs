import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
test.afterEach(async ({ page }, info) => { await page.screenshot({ path: info.outputPath('page.png'), fullPage: true, animations: 'disabled' }); });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', groupId = '019bc2b1-2a40-7cc3-8992-a80de51bf298', newId = '019bc2b1-2a40-7cc3-8992-a80de51bf297';
const createdAtUtc = '2026-11-03T00:00:00Z';
const group = { id: groupId, parentId: null, name: '保存验收分组', sortOrder: 3, isEnabled: true, createdAtUtc, updatedAtUtc: null, version: 4 };
const draft = { id, groupId, dataSourceId: groupId, definitionKey: 'report-review', name: '保存验收定义', description: null, queryPortKey: 'metadata.schemas',
  parameterSchema: [], layoutConfigJson: '{}', latestPublishedVersionNumber: 1, isEnabled: true, createdAtUtc, updatedAtUtc: null, version: 7 };
const summary = value => { const { parameterSchema: _, layoutConfigJson: __, ...row } = value; return row; };
const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
async function boot(page, rows = () => [summary(draft)]) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions: ['reporting.definitions.read', 'reporting.definitions.create', 'reporting.definitions.update', 'reporting.groups.read', 'reporting.groups.create', 'reporting.groups.update', 'reporting.query_ports.read', 'reporting.data_sources.read'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'reporting', parentId: null, routeName: 'reporting-definitions', path: '/reporting/definitions', componentKey: 'reporting-definitions',
    title: '报表配置', caption: '', icon: 'document', order: 1, requiredPermission: 'reporting.definitions.read', children: [] }]));
  await page.route('**/api/v1/reporting/groups', route => json(route, [group]));
  await page.route('**/api/v1/reporting/query-ports', route => json(route, [{ queryPortKey: 'metadata.schemas', displayName: '目录', description: '', supportedProviderKeys: ['sql_server'], parameters: [] }]));
  await page.route('**/api/v1/reporting/data-sources?*', route => json(route, { items: [{ id: groupId, tenantId: null, name: '验收数据源', providerKey: 'sql_server', maskedServerEndpoint: 'fixture-***',
    maskedDatabaseName: 'fixture-***', maskedUsername: 'fixture-***', hasPassword: true, trustServerCertificate: false, isEnabled: true, lastTestedAtUtc: null, lastTestStatusKey: null,
    lastTestMessage: null, createdAtUtc, updatedAtUtc: null, version: 1 }], page: 1, pageSize: 100, total: 1 }));
  await page.route('**/api/v1/reporting/definitions?*', route => {
    const selected = new URL(route.request().url()).searchParams.get('groupId'); const items = rows().filter(row => !selected || row.groupId === selected);
    return json(route, { items, page: 1, pageSize: 20, total: items.length });
  });
  await page.route('**/api/v1/reporting/definitions/' + id, route => json(route, draft));
  await page.goto('/#/reporting/definitions'); await expect(page.getByTestId('reporting-definition-create')).toBeEnabled();
}
async function fits(dialog, page) {
  await expect.poll(async () => { const box = await dialog.boundingBox(); return !!box && box.x >= 0 && box.x + box.width <= page.viewportSize().width; }).toBe(true);
  await expect.poll(() => dialog.evaluate(element => {
    for (let node = element.querySelector('.el-dialog') ?? element; node; node = node.parentElement)
      if (getComputedStyle(node).opacity !== '1' || node.getAnimations().some(animation => animation.playState === 'running')) return false;
    return true;
  })).toBe(true);
}
for (const resource of ['group', 'definition']) {
  test(resource + ' 未知新建跨刷新重开防重，人工核对仅解锁下一次手动保存', async ({ page }, info) => {
    let created, writes = 0, publishes = 0;
    await boot(page, () => created && resource === 'definition' ? [summary(draft), summary(created)] : [summary(draft)]);
    await page.route('**/api/v1/reporting/' + (resource === 'group' ? 'groups' : 'definitions'), route => {
      if (route.request().method() === 'GET') return json(route, created ? [group, created] : [group]);
      writes++; if (writes === 1) return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, code: 'fixture.unknown', title: '结果待核对' }) });
      const body = route.request().postDataJSON(); created = resource === 'group' ? { ...group, ...body, id: newId, parentId: null, version: 1 }
        : { ...draft, ...body, id: newId, latestPublishedVersionNumber: 0, version: 1 };
      return json(route, created);
    });
    await page.route('**/api/v1/reporting/definitions/*/publish', route => { publishes++; return route.fulfill({ status: 500 }); });
    const create = page.getByTestId(resource === 'group' ? 'reporting-group-create' : 'reporting-definition-create');
    const save = page.getByTestId(resource === 'group' ? 'reporting-config-group-save' : 'reporting-config-draft-save');
    await create.click(); let dialog = page.getByRole('dialog');
    if (resource === 'group') await dialog.locator('input').first().fill('未知分组原输入');
    else { await page.getByTestId('reporting-config-key').fill('new-report'); await page.getByTestId('reporting-config-name').fill('未知定义原输入'); }
    await save.click(); await expect(page.getByTestId('reporting-draft-receipt')).toContainText('结果尚未确认'); await expect(save).toHaveCount(0);
    await dialog.getByRole('button', { name: '取消', exact: true }).click(); await page.getByTestId('reporting-config-refresh').click(); await create.click();
    await expect(dialog).toContainText('刷新或重开不会自动再次新建');
    await expect(resource === 'group' ? dialog.locator('input').first() : page.getByTestId('reporting-config-name')).toHaveValue(resource === 'group' ? '未知分组原输入' : '未知定义原输入');
    await fits(dialog, page); const accessibility = await new AxeBuilder({ page }).include('[role="dialog"]').analyze();
    expect(accessibility.violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => ({ target: node.target, reason: node.failureSummary })) }))).toEqual([]);
    await page.screenshot({ path: info.outputPath('creation-unknown.png'), fullPage: true, animations: 'disabled' }); expect(writes).toBe(1);
    await page.getByTestId('reporting-' + resource + '-recovery').click(); await page.getByTestId('reporting-management-confirm').click();
    await expect(save).toBeVisible(); expect(writes).toBe(1); await save.click(); await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByTestId('reporting-draft-receipt')).toContainText('草稿保存已确认'); expect(writes).toBe(2); expect(publishes).toBe(0);
  });
  test(resource + ' 在途更新关闭取消接入，人工刷新核对不会自动重发', async ({ page }) => {
    let committed = false, writes = 0, cancelled = false, release; const pending = new Promise(resolve => { release = resolve; });
    await boot(page, () => [summary(committed && resource === 'definition' ? { ...draft, version: 8, name: '更新原输入' } : draft)]);
    const target = resource === 'group' ? groupId : id, endpoint = '/api/v1/reporting/' + (resource === 'group' ? 'groups/' : 'definitions/') + target;
    page.on('requestfailed', request => { if (request.url().endsWith(endpoint) && request.method() !== 'GET') cancelled = true; });
    await page.route('**' + endpoint, async route => {
      if (route.request().method() === 'GET') return json(route, draft);
      writes++; committed = true; const body = route.request().postDataJSON(); expect(body.version).toBe(resource === 'group' ? 4 : 7); await pending;
      await json(route, { ...(resource === 'group' ? group : draft), ...body, version: body.version + 1 }).catch(() => {});
    });
    if (resource === 'group') await page.locator('.groups-card').getByRole('button', { name: '编辑', exact: true }).click(); else await page.getByTestId('reporting-config-edit').click();
    const dialog = page.getByRole('dialog'); await (resource === 'group' ? dialog.locator('input').first() : page.getByTestId('reporting-config-name')).fill('更新原输入');
    await page.getByTestId(resource === 'group' ? 'reporting-config-group-save' : 'reporting-config-draft-save').click(); await expect.poll(() => writes).toBe(1);
    await expect(dialog).toContainText('关闭只停止本窗口等待'); await dialog.getByRole('button', { name: '取消', exact: true }).click();
    await expect.poll(() => cancelled).toBe(true); release(); await expect(page.getByTestId('reporting-draft-receipt')).toContainText('结果尚未确认');
    await page.getByTestId('reporting-config-refresh').click(); await expect(page.getByRole('dialog')).toHaveCount(0); expect(writes).toBe(1);
  });
}
