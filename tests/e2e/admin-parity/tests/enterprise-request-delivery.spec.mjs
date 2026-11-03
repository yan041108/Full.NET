import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const requestId = id(1), attachmentId = id(2), reportId = id(3), tenantId = id(4);
const base = `/api/v1/enterprise_request/enterprise-requests/${requestId}`;
const now = '2026-10-27T00:00:00Z';
const permissions = ['identity.navigation.read', 'enterprise_request.enterprise_requests.read',
  'enterprise_request.enterprise_request_lines.read', 'enterprise_request.attachments.read', 'enterprise_request.attachments.manage',
  'enterprise_request.reports.read', 'enterprise_request.reports.create', 'enterprise_request.reports.cancel', 'enterprise_request.documents.print'];
const row = { id: requestId, tenantId, organizationUnitId: id(5), requestNumber: 'REQ-DOC', title: '交付验收', status: 'Draft',
  totalAmount: '12.50', applicantUserId: id(6), createdAtUtc: now, createdById: id(6), updatedAtUtc: null,
  updatedById: null, deletedAtUtc: null, deletedById: null, isDeleted: false, version: '1' };
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });

async function setup(page, allowed = permissions) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', r => json(r, { accessToken: 'delivery-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', r => json(r, { id: id(6), username: 'delivery', displayName: 'Delivery operator', actorScope: 'tenant', scope: `tenant:${tenantId.replaceAll('-', '')}`,
    isSuperAdministrator: false, tenantId, permissions: allowed, sessionId: id(7), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', r => json(r, [{ id: 'enterprise-requests', parentId: null, routeName: 'enterprise-requests', path: '/enterprise-requests',
    componentKey: 'enterprise-requests', title: '企业申请', caption: 'Enterprise requests', icon: 'collection', order: 1,
    requiredPermission: 'enterprise_request.enterprise_requests.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', r => json(r, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', r => json(r, { unreadCount: 0 }));
  await page.route('**/api/v1/identity/session-policy', r => json(r, { loginPolicy: 0 }));
  await page.route('**/api/v1/me/tenant-invitations', r => json(r, []));
  await page.route('**/api/v1/enterprise_request/enterprise-requests?*', r => json(r, { items: [row], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**${base}`, r => json(r, row));
}

for (const width of [1440, 390]) {
  test(`真实交付页面：上传重试、文档状态与隔离打印 ${width}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); await setup(page);
    const files = [], reports = [], uploadKeys = [];
    await page.route(`**${base}/attachments?*`, r => {
      if (r.request().method() === 'GET') return json(r, { items: files, page: 1, pageSize: 20, total: files.length });
      const url = new URL(r.request().url()); uploadKeys.push(url.searchParams.get('idempotencyKey'));
      expect(url.searchParams.get('requestVersion')).toBe('1');
      expect(r.request().postDataBuffer().toString()).toContain('filename="evidence.txt"');
      if (uploadKeys.length === 1) return json(r, { status: 503, code: 'test.response_lost', title: 'lost' }, 503);
      const file = { id: attachmentId, requestId, fileName: 'evidence.txt', contentType: 'text/plain', sizeBytes: '14', phase: 'Ready', version: '4', createdAtUtc: now };
      files.push(file); return json(r, file);
    });
    await page.route(`**${base}/reports?*`, r => json(r, { items: reports, total: reports.length, page: 1, pageSize: 20 }));
    await page.route(`**${base}/reports`, r => {
      expect(r.request().postDataJSON()).toMatchObject({ requestVersion: '1' });
      const report = { id: reportId, requestId, phase: 'Queued', errorCode: null, attempt: 0, includesAmounts: false, createdAtUtc: now, expiresAtUtc: '2026-10-28T00:00:00Z', version: '1' };
      reports.push(report); return json(r, report);
    });
    await page.route(`**${base}/reports/${reportId}/cancel`, r => {
      expect(r.request().postDataJSON()).toEqual({ version: '1' }); reports[0].phase = 'CancelledCleanup'; reports[0].version = '2'; return json(r, reports[0]);
    });
    await page.route(`**${base}/attachments/${attachmentId}/download`, r => r.fulfill({ contentType: 'application/octet-stream', headers: { 'Content-Disposition': 'attachment; filename=evidence.txt' }, body: 'safe attachment' }));
    await page.route(`**${base}/attachments/${attachmentId}/delete`, r => { expect(r.request().postDataJSON()).toEqual({ requestVersion: '1', attachmentVersion: '4' }); files[0].phase = 'Deleting'; return json(r, true); });
    await page.route(`**${base}/print`, r => json(r, { requestId, requestVersion: '1', html: '<h1>安全预览</h1><img src=x onerror="window.evil=true"><script>window.evil=true</script>', includesAmounts: false, generatedAtUtc: now }));
    await page.goto('/#/enterprise-requests');
    await page.getByRole('link', { name: '企业申请', exact: true }).click();
    await page.getByRole('button', { name: '附件与文档', exact: true }).click();
    await page.setViewportSize({ width, height: 1000 });
    const dialog = page.getByRole('dialog', { name: '企业申请交付', exact: true });
    await dialog.locator('input[type=file]').setInputFiles({ name: 'evidence.txt', mimeType: 'text/plain', buffer: Buffer.from('safe attachment') });
    await dialog.getByRole('button', { name: '上传附件', exact: true }).click();
    await expect(dialog.getByRole('alert')).toContainText('操作未完成');
    await dialog.getByRole('button', { name: '上传附件', exact: true }).click();
    await expect(dialog).toContainText('evidence.txt'); expect(uploadKeys).toHaveLength(2); expect(uploadKeys[0]).toBe(uploadKeys[1]);
    const downloaded = page.waitForEvent('download'); await dialog.getByRole('button', { name: '下载', exact: true }).click();
    expect((await downloaded).suggestedFilename()).toBe('evidence.txt');
    await dialog.getByRole('button', { name: '生成报表', exact: true }).click();
    await expect(dialog).toContainText(reportId);
    await expect(dialog.getByTestId('enterprise-report-expiry')).toHaveAttribute('datetime', '2026-10-28T00:00:00Z');
    await dialog.getByRole('button', { name: '取消任务', exact: true }).click();
    const confirmation = page.getByRole('dialog', { name: '确认取消报表', exact: true }); await expect(confirmation).toBeVisible();
    await confirmation.getByTestId('enterprise-report-confirm-cancel').click();
    await expect(dialog.getByRole('button', { name: '取消任务', exact: true })).toHaveCount(0);
    await expect(dialog).toContainText('已取消'); await expect(dialog.getByTestId('enterprise-report-cleanup')).toBeVisible();
    await dialog.getByRole('button', { name: '打印预览', exact: true }).click();
    const iframe = dialog.locator('iframe'); await expect(iframe).toBeVisible();
    const html = await iframe.getAttribute('srcdoc'); expect(html).not.toMatch(/<script|onerror/);
    await expect(iframe).toHaveAttribute('sandbox', 'allow-same-origin allow-modals');
    await expect(page.frameLocator('iframe[title="打印预览"]').getByRole('heading', { name: '安全预览' })).toBeVisible();
    await dialog.getByRole('button', { name: '删除', exact: true }).click();
    await expect(dialog.getByRole('button', { name: '下载', exact: true })).toHaveCount(0);
    const box = await dialog.boundingBox(); expect(box.x).toBeGreaterThanOrEqual(0); expect(box.x + box.width).toBeLessThanOrEqual(width);
    const axe = await new AxeBuilder({ page }).include('.enterprise-delivery').withTags(['wcag2a', 'wcag2aa']).analyze(); expect(axe.violations).toEqual([]);
    await dialog.screenshot({ path: testInfo.outputPath(`delivery-${width}.png`) });
  });
}

test('缺少明细权限不显示报表创建与打印，保留已授权附件读取', async ({ page }) => {
  await setup(page, ['identity.navigation.read', 'enterprise_request.enterprise_requests.read', 'enterprise_request.attachments.read']);
  await page.route(`**${base}/attachments?*`, r => json(r, { items: [], total: 0, page: 1, pageSize: 20 }));
  await page.goto('/#/enterprise-requests'); await page.getByRole('link', { name: '企业申请', exact: true }).click();
  await page.getByRole('button', { name: '附件与文档', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: '企业申请交付' });
  await expect(dialog.getByRole('heading', { name: '附件', exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: '生成报表' })).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: '打印预览' })).toHaveCount(0);
  await expect(dialog.locator('input[type=file]')).toHaveCount(0);
});
