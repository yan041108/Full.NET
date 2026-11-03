import { expect, test } from '@playwright/test';

const id = '01912345-6789-7abc-8def-0123456789ab';
const versionId = '01912345-6789-7abc-8def-0123456789ac';
const user = '01912345-6789-7abc-8def-0123456789ae';

// 浏览器使用受控 HTTP；双库事务、会话与可靠释放由 DocumentBatchOneTests 验证。
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`第一批文档状态与完整编辑 ${viewport.width}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    let item = { id, documentNo: 'DOC-BATCH-ONE', title: '第一批验收', description: '保留描述', categoryId: null,
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
    await page.route('**/api/v1/document/host/items?*', route => json(route, { items: [item], page: 1, pageSize: 100, total: 1 }));
    await page.route(`**/api/v1/document/host/items/${id}`, route => {
      const body = route.request().postDataJSON();
      expect(body).toEqual({ title: '修改标题', description: item.description, categoryId: null, thumbnail: '/cover.png', sort: 7, tagIds: [], version: item.version });
      item = { ...item, ...body, version: item.version + 1 };
      return json(route, item);
    });
    for (const [action, status] of [['publish', 1], ['archive', 2], ['unarchive', 0]]) {
      await page.route(`**/api/v1/document/host/items/${id}/${action}`, route => {
        expect(route.request().postDataJSON()).toEqual({ version: item.version });
        item = { ...item, status, version: item.version + 1 };
        return json(route, item);
      });
    }
    async function showAction(action) {
      const button = page.getByTestId(`host-document-item-${action}`);
      if (viewport.width < 768 && action !== 'edit') {
        await page.keyboard.press('Escape');
        await expect(page.locator('.art-table-action-group__dropdown:visible')).toHaveCount(0);
        await page.getByTestId('art-table-action-more').click();
      } else if (!await button.isVisible()) await page.getByTestId('art-table-action-more').click();
      await expect(button).toBeVisible(); return button;
    }
    await page.goto('/#/document/host-items');
    await showAction('publish');
    await (await showAction('edit')).click();
    await page.getByTestId('host-document-item-edit-title').fill('修改标题');
    await page.getByTestId('host-document-item-save').click();
    await showAction('publish');
    await page.getByTestId('host-document-item-publish').click(); await page.mouse.click(10, 100);
    await showAction('archive');
    await page.getByTestId('host-document-item-archive').click(); await page.mouse.click(10, 100);
    await showAction('unarchive');
    for (const action of ['edit', 'upload-version', 'share']) await expect(page.getByTestId(`host-document-item-${action}`)).toHaveCount(0);
    await page.getByTestId('host-document-item-unarchive').focus();
    await page.keyboard.press('Enter');
    await showAction('publish');
    expect(errors).toEqual([]);
  });
}
