import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const at = '2026-10-08T00:00:00Z';
const permission = 'enterprise_request.enterprise_requests.read';
const request = { id, tenantId: id, organizationUnitId: id, requestNumber: 'REQ-PROGRESS', title: '审批进度夹具',
  status: 'Approved', totalAmount: '12.50', applicantUserId: id, version: '3', createdAtUtc: at,
  createdById: id, updatedAtUtc: at, updatedById: id, isDeleted: false, deletedAtUtc: null, deletedById: null };
const progress = { requestId: id, requestStatus: 'Approved', requestVersion: '3', deliveryState: 'finalized',
  workflowDefinitionVersionId: id, workflowInstanceId: id, submittedVersion: '2', submittedAtUtc: at,
  startedAtUtc: at, completedAtUtc: at, finalNotification: { intentId: id, acceptedAtUtc: at,
    totalDeliveryCount: 1, pendingDeliveryCount: 0, sentDeliveryCount: 0, failedDeliveryCount: 0,
    deadLetteredDeliveryCount: 0, unknownDeliveryCount: 0, otherDeliveryCount: 0,
    deliveredDeliveryCount: 0, readDeliveryCount: 0, suppressedDeliveryCount: 0, persistedDeliveryCount: 1,
    nextAttemptAtUtc: null } };
const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

for (const { locale, width, height } of [
  { locale: 'zh-CN', width: 1280, height: 720 },
  { locale: 'zh-CN', width: 375, height: 568 },
  { locale: 'en-US', width: 1280, height: 720 },
]) {
  test(`审批进度可滚动、键盘关闭且状态本地化：${locale} ${width}x${height}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height });
    await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
    await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
    await page.route('**/api/v1/auth/refresh', route => json(route,
      { accessToken: 'fixture-access', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
    await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: '夹具',
      tenantId: id, actorScope: `tenant:${id.replaceAll('-', '')}`, scope: `tenant:${id.replaceAll('-', '')}`,
      isSuperAdministrator: false, passwordChangeRequired: false, permissions: [permission, 'notifications.inbox.read'],
      sessionId: id, preferredLocale: locale, profileVersion: 1 }));
    await page.route('**/api/v1/navigation', route => json(route, [{ id: 'enterprise-requests', parentId: null,
      routeName: 'enterprise-requests', path: '/enterprise-requests', componentKey: 'enterprise-requests',
      title: '申请', caption: '', icon: 'document', order: 10, requiredPermission: permission, children: [] }]));
    await page.route('**/api/v1/enterprise_request/enterprise-requests?*', route => json(route,
      { items: [request], total: 1, page: 1, pageSize: 20 }));
    await page.route(`**/api/v1/enterprise_request/enterprise-requests/${id}/approval-progress`, route => json(route, progress));
    await page.goto('/#/enterprise-requests');
    const label = locale === 'zh-CN' ? '审批进度' : 'Approval progress';
    const trigger = page.getByRole('button', { name: label, exact: true });
    await trigger.click();
    const dialog = page.getByRole('dialog', { name: label, exact: true });
    await expect(dialog.locator('.approval-progress').first().locator('dd').first()).toHaveText(locale === 'zh-CN' ? '已通过' : 'Approved');
    await expect(dialog.getByTestId('notification-persisted')).toHaveText('1');
    await expect(dialog).toBeVisible();
    // 内容超过视口时通过真实滚轮到达页脚，不能以 Playwright 自动滚入掩盖不可用的容器。
    await page.mouse.move(width / 2, height / 2);
    await page.mouse.wheel(0, 2500);
    const cancel = dialog.getByRole('button', { name: locale === 'zh-CN' ? '取消' : 'Cancel', exact: true });
    await expect.poll(async () => {
      const bounds = await cancel.boundingBox();
      return !!bounds && bounds.y >= 0 && bounds.y + bounds.height <= height;
    }).toBe(true);
    await cancel.focus();
    await expect(cancel).toBeFocused();
    for (let index = 0; index < 6; index++) {
      await page.keyboard.press('Tab');
      await expect.poll(() => dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
    }
    for (let index = 0; index < 6; index++) {
      await page.keyboard.press('Shift+Tab');
      await expect.poll(() => dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
    }
    const accessibility = await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
    expect(accessibility.violations).toEqual([]);
    await page.screenshot({ path: testInfo.outputPath('progress.png'), fullPage: true });
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
  });
}
