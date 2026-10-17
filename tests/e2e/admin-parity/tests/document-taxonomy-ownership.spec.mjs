import { expect, test } from '@playwright/test';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const other = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const common = { id, name: '验收目录', code: 'KEEP', icon: 'book', color: '#123456', description: '保留描述', createdAtUtc: '2026-10-10T00:00:00Z', updatedAtUtc: null, version: 1 };
const configurations = [
  { kind: 'category', plural: 'categories', entity: { ...common, parentId: null, sortOrder: 0 } },
  { kind: 'tag', plural: 'tags', entity: { ...common, useCount: 0, isHot: true, isRecommended: false } }
];
const json = (route, value) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });

async function boot(page, config) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  const permission = 'document.' + config.plural;
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false, permissions: ['read', 'create', 'update', 'delete'].map(action => permission + '.' + action), sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  const routeName = 'document-' + config.plural, path = '/document/' + config.plural;
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'fixture', parentId: null, routeName, path, componentKey: routeName, title: '文档目录', caption: '', icon: 'document', order: 1, requiredPermission: permission + '.read', children: [] }]));
  let lists = 0;
  const api = '**/api/v1/document/host/' + config.plural;
  await page.route(api + '*', route => {
    if (route.request().method() !== 'GET') return route.fallback();
    lists++; return json(route, [config.entity]);
  });
  await page.goto('/#' + path);
  await expect(page.getByTestId('document-' + config.kind + '-edit')).toBeEnabled();
  await expect.poll(() => lists).toBe(1);
  return { api, lists: () => lists };
}

for (const config of configurations) {
  const testId = action => 'document-' + config.kind + '-' + action;

  test(config.kind + '关闭在途创建取消请求，重开清空名称', async ({ page }) => {
    const { api, lists } = await boot(page, config);
    let release, started = false, finished = false, cancelled = false;
    const pending = new Promise(resolve => { release = resolve; });
    page.on('requestfailed', request => { if (request.url().endsWith('/host/' + config.plural) && request.method() === 'POST') cancelled = true; });
    await page.route(api, async route => {
      if (route.request().method() !== 'POST') return route.fallback();
      started = true; await pending; await json(route, config.entity).catch(() => {}); finished = true;
    });
    await page.getByTestId(testId('create')).click(); await page.getByTestId(testId('name')).fill('待取消目录');
    await page.getByTestId(testId('editor-submit')).click(); await expect.poll(() => started).toBe(true);
    await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
    release(); await expect.poll(() => finished).toBe(true); await expect.poll(() => cancelled).toBe(true);
    expect(lists()).toBe(1); await expect(page.locator('.el-message--success,.el-message--error')).toHaveCount(0);
    await page.getByTestId(testId('create')).click(); await expect(page.getByTestId(testId('name'))).toHaveValue('');
  });

  test(config.kind + '更新拒绝错配身份，保留隐藏元数据并允许重试', async ({ page }) => {
    const { api, lists } = await boot(page, config); let updates = 0;
    await page.route(api + '/' + id, route => {
      expect(route.request().method()).toBe('PUT');
      expect(route.request().postDataJSON()).toMatchObject({ name: common.name, code: 'KEEP', icon: 'book', color: '#123456', description: '保留描述', version: 1 });
      return json(route, ++updates === 1 ? { ...config.entity, id: other } : { ...config.entity, id: id.toUpperCase(), version: 2 });
    });
    await page.getByTestId(testId('edit')).click(); await page.getByTestId(testId('editor-submit')).click();
    await expect(page.locator('.el-message--error')).toBeVisible(); expect(lists()).toBe(1);
    await expect(page.getByTestId(testId('editor-submit'))).toBeEnabled(); await page.getByTestId(testId('editor-submit')).click();
    await expect(page.getByRole('dialog')).toHaveCount(0); await expect.poll(lists).toBe(2); expect(updates).toBe(2);
  });

  test(config.kind + '删除false不报成功，真实确认重试后刷新', async ({ page }) => {
    const { api, lists } = await boot(page, config); let removes = 0;
    await page.route(api + '/' + id + '/delete', route => {
      expect(route.request().postDataJSON()).toEqual({ version: 1 }); return json(route, ++removes !== 1);
    });
    await page.getByTestId(testId('delete')).click(); await page.getByTestId(testId('delete-confirm')).click();
    await expect(page.locator('.el-message--error')).toBeVisible(); expect(lists()).toBe(1); await expect(page.locator('.el-message--success')).toHaveCount(0);
    await page.getByTestId(testId('delete')).click(); await page.getByTestId(testId('delete-confirm')).click();
    await expect.poll(lists).toBe(2); await expect(page.locator('.el-message--success')).toBeVisible(); expect(removes).toBe(2);
  });

  test(config.kind + '离页回收真实删除确认，不执行旧目录删除', async ({ page }) => {
    const { api } = await boot(page, config); let removes = 0;
    await page.route(api + '/' + id + '/delete', route => { removes++; return json(route, true); });
    await page.getByTestId(testId('delete')).click(); await expect(page.getByRole('dialog')).toContainText(common.name);
    await page.goto('/#/document/share/PUBLIC'); await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByTestId(testId('delete-confirm'))).toHaveCount(0); expect(removes).toBe(0);
  });
}

test('分类排序拒绝小数及溢出，合法Int32下界完整提交', async ({ page }) => {
  const config = configurations[0], { api, lists } = await boot(page, config); let creates = 0;
  await page.route(api, route => {
    if (route.request().method() !== 'POST') return route.fallback();
    creates++; expect(route.request().postDataJSON().sortOrder).toBe(-2147483648); return json(route, config.entity);
  });
  await page.getByTestId('document-category-create').click(); await page.getByTestId('document-category-name').fill('排序目录');
  const input = page.getByTestId('document-category-sort-order');
  for (const value of ['1.5', '2147483648']) {
    await input.fill(value); await page.getByTestId('document-category-editor-submit').click();
    await expect(page.locator('.el-message--warning').last()).toBeVisible(); expect(creates).toBe(0);
  }
  await input.fill('-2147483648'); await page.getByTestId('document-category-editor-submit').click();
  await expect(page.getByRole('dialog')).toHaveCount(0); await expect.poll(lists).toBe(2); expect(creates).toBe(1);
});
