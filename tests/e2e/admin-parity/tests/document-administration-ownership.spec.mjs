import { expect, test } from '@playwright/test';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', other = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const third = '01912345-6789-7abc-8def-0123456789af';
const doc = { id, documentNo: 'DOC-1', title: '管理验收文档', description: null, categoryId: null, categoryName: null, categoryColor: null, documentType: 1, sizeKb: 0, thumbnail: null, status: 1, accessCount: 0, sort: 0, lastAccessTime: null, currentVersion: null, tags: [], createdAtUtc: '2026-10-10T00:00:00Z', createdByUserId: id, updatedAtUtc: null, updatedByUserId: null, deletedAtUtc: null, deletedByUserId: null, version: 2 };
const second = { ...doc, id: other, documentNo: 'DOC-2', title: '第二文档' };
const permission = { id, documentId: id, userId: id, permissionLevel: 'read', createdAtUtc: '2026-10-10T00:00:00Z' };
const added = { ...permission, id: other, userId: other, permissionLevel: 'write' };
const json = (route, value) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });
async function boot(page, recycle = false) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  const permissions = recycle ? ['read', 'restore', 'purge'].map(action => 'document.host_recycle_bin.' + action) : ['document.host_documents.read', 'document.host_permissions.read', 'document.host_permissions.set'];
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false, permissions, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  const routeName = recycle ? 'document-recycle-bin' : 'document-permissions', path = recycle ? '/document/recycle-bin' : '/document/permissions';
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'fixture', parentId: null, routeName, path, componentKey: routeName, title: '文档管理', caption: '', icon: 'document', order: 1, requiredPermission: recycle ? 'document.host_recycle_bin.read' : 'document.host_permissions.read', children: [] }]));
  let lists = 0;
  await page.route('**/api/v1/document/host/' + (recycle ? 'recycle-bin' : 'items') + '?*', route => {
    lists++; return json(route, { items: recycle ? [doc, second].map(item => ({ ...item, status: 4, deletedAtUtc: '2026-10-10T00:00:00Z', deletedByUserId: id })) : [doc, second], page: 1, pageSize: 20, total: 2 });
  });
  await page.route('**/api/v1/document/host/permissions/by-document/*', route => json(route, [permission]));
  await page.goto('/#' + path);
  await expect(page.getByTestId(recycle ? 'document-recycle-restore' : 'document-permissions-set').first()).toBeEnabled();
  await expect.poll(() => lists).toBe(1); return () => lists;
}
async function openAcl(page) { await page.getByTestId('document-permissions-set').first().click(); await expect(page.getByTestId('document-permissions-save')).toBeEnabled(); }
async function inputAcl(page, user = other) { await page.getByTestId('document-permissions-user-id').fill(user); await page.getByTestId('document-permissions-level').fill('write'); }

test('权限读取错配不开放保存，重读成功后保留其他授权', async ({ page }) => {
  await boot(page); let reads = 0, writes = 0;
  await page.route('**/api/v1/document/host/permissions/by-document/' + id, route => json(route, ++reads === 1 ? [{ ...permission, documentId: other }] : [permission]));
  await page.route('**/api/v1/document/host/permissions', route => {
    writes++; expect(route.request().postDataJSON()).toEqual({ documentId: id, permissions: [{ userId: id, permissionLevel: 'read' }, { userId: other, permissionLevel: 'write' }] });
    return json(route, [added, permission]);
  });
  await page.getByTestId('document-permissions-set').first().click(); await expect(page.locator('.el-alert--error')).toBeVisible();
  await expect(page.getByTestId('document-permissions-save')).toHaveCount(0); expect(writes).toBe(0);
  await page.getByTestId('document-permissions-load').click(); await expect(page.getByTestId('document-permissions-save')).toBeEnabled();
  await inputAcl(page); await page.getByTestId('document-permissions-save').click(); await expect(page.locator('.el-message--success')).toBeVisible();
  await expect(page.getByTestId('document-permissions-user-id')).toHaveValue(''); expect(writes).toBe(1);
});

test('保存结果不确定时禁止旧快照重试，重读保留已提交用户', async ({ page }) => {
  await boot(page); let writes = 0, committed = false;
  await page.route('**/api/v1/document/host/permissions/by-document/' + id, route => json(route, committed ? [permission, added] : [permission]));
  await page.route('**/api/v1/document/host/permissions', route => {
    writes++; const payload = route.request().postDataJSON();
    if (writes === 1) { committed = true; return json(route, [{ ...permission, documentId: other }]); }
    expect(payload.permissions).toEqual([{ userId: id, permissionLevel: 'read' }, { userId: other, permissionLevel: 'write' }, { userId: third, permissionLevel: 'write' }]);
    return json(route, [permission, added, { ...added, id: third, userId: third }]);
  });
  await openAcl(page); await inputAcl(page); await page.getByTestId('document-permissions-save').click();
  await expect(page.locator('.el-message--error')).toBeVisible(); await expect(page.getByTestId('document-permissions-save')).toHaveCount(0); expect(writes).toBe(1);
  await page.getByTestId('document-permissions-load').click(); await expect(page.getByTestId('document-permissions-save')).toBeEnabled();
  await inputAcl(page, third); await page.getByTestId('document-permissions-save').click(); await expect(page.locator('.el-message--success')).toBeVisible(); expect(writes).toBe(2);
});

test('关闭在途权限保存中止请求，重开清空输入', async ({ page }) => {
  await boot(page); let release, started = false, finished = false, cancelled = false;
  const pending = new Promise(resolve => { release = resolve; });
  page.on('requestfailed', request => { if (request.url().endsWith('/host/permissions') && request.method() === 'POST') cancelled = true; });
  await page.route('**/api/v1/document/host/permissions', async route => { started = true; await pending; await json(route, [permission, added]).catch(() => {}); finished = true; });
  await openAcl(page); await inputAcl(page); await page.getByTestId('document-permissions-save').click(); await expect.poll(() => started).toBe(true);
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  release(); await expect.poll(() => finished).toBe(true); await expect.poll(() => cancelled).toBe(true); await expect(page.locator('.el-message--success,.el-message--error')).toHaveCount(0);
  await openAcl(page); await expect(page.getByTestId('document-permissions-user-id')).toHaveValue('');
});

test('恢复拒绝其他条目响应，重试合法身份才刷新', async ({ page }) => {
  const lists = await boot(page, true); let restores = 0;
  await page.route('**/api/v1/document/host/recycle-bin/' + id + '/restore', route => { expect(route.request().postDataJSON()).toEqual({ version: 2 }); return json(route, ++restores === 1 ? { ...doc, id: other } : doc); });
  await page.getByTestId('document-recycle-restore').first().click(); await expect(page.locator('.el-message--error')).toBeVisible(); expect(lists()).toBe(1);
  await page.getByTestId('document-recycle-restore').first().click(); await expect.poll(lists).toBe(2); expect(restores).toBe(2);
});

test('永久清除false不报成功，确认重试后刷新', async ({ page }) => {
  const lists = await boot(page, true); let purges = 0;
  await page.route('**/api/v1/document/host/recycle-bin/' + id + '/purge', route => json(route, ++purges !== 1));
  await page.getByTestId('document-recycle-purge').first().click(); await page.getByTestId('document-recycle-confirm').click();
  await expect(page.locator('.el-message--error')).toBeVisible(); expect(lists()).toBe(1); await expect(page.locator('.el-message--success')).toHaveCount(0);
  await page.getByTestId('document-recycle-purge').first().click(); await page.getByTestId('document-recycle-confirm').click();
  await expect.poll(lists).toBe(2); expect(purges).toBe(2);
});

test('批量恢复冻结选择，在途取消勾选不改变剩余目标', async ({ page }) => {
  const lists = await boot(page, true); const restored = []; let release;
  const pending = new Promise(resolve => { release = resolve; });
  await page.route('**/api/v1/document/host/recycle-bin/*/restore', async route => {
    const target = route.request().url().split('/').at(-2); restored.push(target); expect(route.request().postDataJSON()).toEqual({ version: 2 });
    if (restored.length === 1) await pending; await json(route, target === id ? doc : second);
  });
  const selectAll = page.locator('.el-table__header-wrapper .el-checkbox').first();
  await selectAll.click(); await page.getByTestId('document-recycle-batch-restore').click(); await expect.poll(() => restored.length).toBe(1);
  await selectAll.click(); release(); await expect.poll(lists).toBe(2); expect(restored).toEqual([id, other]);
});

test('离开回收站回收清除确认，不发送永久删除', async ({ page }) => {
  await boot(page, true); let purges = 0;
  await page.route('**/api/v1/document/host/recycle-bin/*/purge', route => { purges++; return json(route, true); });
  await page.getByTestId('document-recycle-purge').first().click(); await expect(page.getByRole('dialog')).toContainText(doc.title);
  await page.goto('/#/document/share/PUBLIC'); await expect(page.getByRole('dialog')).toHaveCount(0); expect(purges).toBe(0);
});
