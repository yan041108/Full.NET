import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = '01912345-6789-7abc-8def-0123456789ab';
const user = '01912345-6789-7abc-8def-0123456789ac';
// 受控 HTTP 验证浏览器交互；SQL、租约和 Files 状态机由本批双库用例独立验收。
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`第三批能力关闭、取消及人工重试 ${viewport.width}`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    let enabled = false; let row = { id, documentItemId: id, documentTitle: '预览闭环验收', versionId: id, sourceFileId: id,
      outputFileId: null, statusKey: 'pending', providerKey: 'external_http', errorCode: null, requestedByUserId: user,
      createdAtUtc: '2026-10-24T00:00:00Z', startedAtUtc: null, completedAtUtc: null, version: 4, attemptCount: 2, nextAttemptAtUtc: null };
    await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
    await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'document-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
    await page.route('**/api/v1/me', route => json(route, { id: user, username: 'preview-test', displayName: '预览测试', tenantId: null,
      actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false, sessionId: user, preferredLocale: 'zh-CN', profileVersion: 1,
      permissions: ['identity.navigation.read', 'document.host_preview_tasks.read', 'document.host_preview_tasks.create', 'document.host_preview_tasks.cancel', 'document.host_preview_tasks.retry'] }));
    await page.route('**/api/v1/navigation', route => json(route, [{ id: 'preview', parentId: null, routeName: 'document-preview-tasks',
      path: '/document/preview-tasks', componentKey: 'document-preview-tasks', title: '预览任务', caption: '文档', icon: 'document', order: 1,
      requiredPermission: 'document.host_preview_tasks.read', children: [] }]));
    await page.route('**/api/v1/tenancy/available', route => json(route, []));
    await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
    await page.route('**/api/v1/document/public/preview-capabilities', route => json(route, { enabled, providerKey: enabled ? 'external_http' : 'disabled',
      allowedMimeTypes: ['application/msword'], maxInputBytes: 52428800, maxAttempts: 3 }));
    await page.route('**/api/v1/document/host/preview-tasks?*', route => json(route, { items: [row], page: 1, pageSize: 20, total: 1 }));
    let cancelled = false; let retried = false;
    await page.route(`**/api/v1/document/host/preview-tasks/${id}/cancel`, route => {
      expect(route.request().postDataJSON()).toEqual({ version: 4 }); cancelled = true;
      row = { ...row, statusKey: 'cancelled', version: 5, errorCode: 'document.preview.cancelled' }; return json(route, row);
    });
    await page.route(`**/api/v1/document/host/preview-tasks/${id}/retry`, route => { retried = true; return json(route, { ...row, id: user, statusKey: 'pending', attemptCount: 0 }); });
    await page.goto('/#/document/preview-tasks');
    await expect(page.getByTestId('document-preview-disabled')).toBeVisible();
    await expect(page.getByTestId('document-preview-task-create')).toHaveCount(0);
    const cancel = page.getByTestId('document-preview-task-cancel'); await expect(cancel).toBeVisible();
    await cancel.focus(); await page.keyboard.press('Enter');
    const confirmation = page.getByRole('dialog', { name: '确认取消预览转换', exact: true }); await expect(confirmation).toBeVisible(); expect(cancelled).toBe(false);
    await confirmation.getByTestId('document-preview-confirm-cancel').click();
    await expect.poll(() => cancelled).toBe(true);
    await expect(page.getByTestId('document-preview-task-retry')).toHaveCount(0);
    enabled = true;
    await page.getByRole('button', { name: '刷新', exact: true }).first().click();
    await expect(page.getByTestId('document-preview-task-create')).toBeVisible();
    const retry = page.getByTestId('document-preview-task-retry'); await expect(retry).toBeVisible(); await retry.click();
    await expect.poll(() => retried).toBe(true);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    if (viewport.width < 768) {
      await expect(page.locator('.document-preview-tasks-view__mobile-state')).toContainText('已取消');
      await expect(page.locator('.document-preview-tasks-view__mobile-state')).toContainText('预览转换已取消');
    }
    await page.screenshot({ path: testInfo.outputPath(`preview-${viewport.width}.png`), fullPage: true });
    await expect(page.locator('.el-message')).toHaveCount(0);
    const accessibility = await new AxeBuilder({ page }).include('.document-preview-tasks-view').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
    expect(accessibility.violations.filter(item => ['critical', 'serious'].includes(item.impact))).toEqual([]);
    expect(errors).toEqual([]);
  });
}
