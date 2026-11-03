import { expect, test } from '@playwright/test';

const id = '01912345-6789-7abc-8def-0123456789ab';
const versionId = '01912345-6789-7abc-8def-0123456789ac';
const user = '01912345-6789-7abc-8def-0123456789ae';

// 浏览器使用受控 HTTP；双库筛选、统计与分类事务由 DocumentBatchTwoTests 验证。
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`第二批服务端分页与清空编辑 ${viewport.width}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    let item = { id, documentNo: 'DOC-BATCH-TWO', title: '第二批验收', description: '保留描述', categoryId: null,
      categoryName: null, categoryColor: null, documentType: 1, sizeKb: 1, thumbnail: '/cover.png', status: 0,
      accessCount: 0, sort: 7, lastAccessTime: null, visibilityMode: 'host',
      currentVersion: { id: versionId, versionNumber: 1, fileId: versionId, contentHash: null, sizeBytes: 1537,
        changeDescription: '首次上传', createdAtUtc: '2026-10-24T00:00:00Z', uploadedByUserId: user },
      tags: [], createdAtUtc: '2026-10-24T00:00:00Z', createdByUserId: user, updatedAtUtc: null,
      updatedByUserId: null, deletedAtUtc: null, deletedByUserId: null, version: 1 };
    await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
    await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'document-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
    await page.route('**/api/v1/me', route => json(route, { id: user, username: 'document-test', displayName: '文档测试', tenantId: null,
      actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false, sessionId: user, preferredLocale: 'zh-CN', profileVersion: 1,
      permissions: ['identity.navigation.read', 'document.host_documents.read', 'document.host_documents.update', 'document.host_documents.add_version',
        'document.host_documents.publish', 'document.host_documents.archive', 'document.host_documents.unarchive', 'document.host_shares.create'] }));
    await page.route('**/api/v1/navigation', route => json(route, [{ id: 'documents', parentId: null, routeName: 'host-document-items',
      path: '/document/host-items', componentKey: 'host-document-items', title: '文档库', caption: '文档测试', icon: 'document', order: 1,
      requiredPermission: 'document.host_documents.read', children: [] }]));
    await page.route('**/api/v1/tenancy/available', route => json(route, []));
    await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
    await page.route('**/api/v1/document/host/categories**', route => json(route, []));
    await page.route('**/api/v1/document/host/tags**', route => json(route, { items: [], page: 1, pageSize: 20, total: 0 }));
    const requests = [];
    await page.route('**/api/v1/document/host/items?*', route => {
      const query = new URL(route.request().url()).searchParams;
      requests.push(Object.fromEntries(query));
      return json(route, { items: [{ ...item, title: query.get('keyword') || item.title }], page: Number(query.get('page') || 1),
        pageSize: Number(query.get('pageSize') || 20), total: query.get('keyword') ? 1 : 123 });
    });
    await page.route(`**/api/v1/document/host/items/${id}`, route => {
      const body = route.request().postDataJSON();
      expect(body).toEqual({ title: '修改标题', description: null, categoryId: null, thumbnail: null, sort: 7, tagIds: [], version: item.version });
      item = { ...item, ...body, version: item.version + 1 };
      return json(route, item);
    });
    await page.goto('/#/document/host-items');
    await expect(page.getByTestId('host-document-item-edit')).toBeVisible();
    await page.locator('.el-pagination .btn-next').first().click();
    await expect.poll(() => requests.at(-1)?.page).toBe('2');
    const keyword = page.getByPlaceholder('按标题筛选');
    await keyword.fill('第 101 条');
    await page.getByRole('button', { name: '查询', exact: true }).click();
    await expect.poll(() => requests.at(-1)?.keyword).toBe('第 101 条');
    expect(requests.at(-1).page).toBe('1');
    await page.getByTestId('host-document-item-edit').click();
    await page.getByTestId('host-document-item-edit-title').fill('修改标题');
    await page.getByTestId('host-document-item-edit-description').fill('');
    await page.getByTestId('host-document-item-edit-thumbnail').fill('');
    await page.getByTestId('host-document-item-save').focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('host-document-item-edit')).toBeVisible();
    await expect(page.getByTestId('host-document-item-edit-title')).toHaveCount(0);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
    await page.screenshot({ path: `C:/Users/dai27/AppData/Local/Temp/fullnet-document-batch2-browser-20261024/document-${viewport.width}.png`, fullPage: true });
    expect(errors).toEqual([]);
  });
}
