import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const base = '/api/v1/enterprise_request/enterprise-requests';
const permissions = ['identity.navigation.read', 'enterprise_request.enterprise_requests.read',
  'enterprise_request.enterprise_requests.create', 'enterprise_request.enterprise_requests.update', 'enterprise_request.enterprise_request_lines.read'];
const master = { id: id(1), tenantId: id(2), organizationUnitId: id(3), requestNumber: 'REQ-LINES', title: '主从申请', status: 'Draft',
  totalAmount: '9007199254740993.12', applicantUserId: id(9), version: '9007199254740993', createdAtUtc: '2026-11-02T00:00:00Z',
  createdById: id(9), updatedAtUtc: null, updatedById: null, deletedAtUtc: null, deletedById: null, isDeleted: false };
const line = n => ({ id: id(n), requestId: id(1), tenantId: id(2), organizationUnitId: id(3), lineNumber: n, itemDescription: `事项 ${n}`, quantity: '1.0001',
  unitPrice: '1.00', lineAmount: '1.00', version: '9007199254740995', createdAtUtc: master.createdAtUtc, createdById: id(9), updatedAtUtc: null,
  updatedById: null, deletedAtUtc: null, deletedById: null, isDeleted: false });
const json = (route, value, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(value) });
async function setup(page, allowed = permissions, status = 'Draft') {
  const snapshot = { master: { ...master, status }, details: [line(4), line(5)] };
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', r => json(r, { accessToken: 'aggregate-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', r => json(r, { id: id(9), username: 'request-editor', displayName: 'Request editor', actorScope: 'tenant', scope: `tenant:${id(2).replaceAll('-', '')}`,
    tenantId: id(2), isSuperAdministrator: false, permissions: allowed, sessionId: id(8), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', r => json(r, [{ id: 'enterprise-requests', parentId: null, routeName: 'enterprise-requests', path: '/enterprise-requests', componentKey: 'enterprise-requests',
    title: '企业申请', caption: 'Requests', icon: 'collection', order: 1, requiredPermission: permissions[1], children: [] }]));
  for (const [path, value] of [['tenancy/available', []], ['notifications/my-inbox-messages/unread-count', { unreadCount: 0 }],
    ['identity/session-policy', { loginPolicy: 0 }], ['me/tenant-invitations', []]]) await page.route(`**/api/v1/${path}`, r => json(r, value));
  await page.route(`**${base}?*`, r => json(r, { items: [snapshot.master], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**${base}/${id(1)}/aggregate`, r => json(r, snapshot));
  await page.goto('/#/enterprise-requests'); await page.getByRole('link', { name: '企业申请', exact: true }).click();
  return snapshot;
}

for (const width of [1440, 390]) {
  test(`主从编辑：精确金额、版本冲突与增改删 ${width}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); const snapshot = await setup(page); let writes = 0;
    await page.route(`**${base}/${id(1)}/aggregate`, async r => {
      if (r.request().method() === 'GET') return json(r, snapshot);
      const body = r.request().postDataJSON(); writes++;
      expect(body.master.totalAmount).toBe(master.totalAmount);
      expect(body.master.version).toBe(writes === 1 ? '9007199254740993' : '9007199254740994');
      expect(body.updated).toHaveLength(1); expect(body.removed).toEqual([{ id: id(5), version: '9007199254740995' }]);
      expect(body.added).toHaveLength(1); expect(body.updated[0].input.quantity).toBe('1.2345');
      expect(JSON.stringify(body)).not.toContain('requestId');
      if (writes === 1) {
        snapshot.master.version = '9007199254740994'; snapshot.details[0].version = '9007199254740996';
        return json(r, { status: 409, code: 'version.conflict', title: '单据版本已更新' }, 409);
      }
      expect(body.updated[0].version).toBe('9007199254740996');
      snapshot.master = { ...snapshot.master, ...body.master, version: '9007199254740995' };
      snapshot.details = [{ ...snapshot.details[0], ...body.updated[0].input, version: '9007199254740997' }, { ...line(6), ...body.added[0] }];
      return json(r, snapshot);
    });
    const entry = page.getByRole('button', { name: '主从明细', exact: true }); await entry.click();
    await page.setViewportSize({ width, height: 1000 });
    const dialog = page.getByRole('dialog', { name: '申请单与明细', exact: true });
    async function edit() {
      await expect(dialog.getByTestId('aggregate-line')).toHaveCount(2);
      await dialog.getByTestId('aggregate-line-quantity').first().fill('1.2345');
      await dialog.getByTestId('aggregate-remove-line').last().click(); await dialog.getByTestId('aggregate-add-line').click();
      await dialog.getByTestId('aggregate-line-description').last().fill('新增事项');
    }
    await edit(); await dialog.getByTestId('aggregate-save').click();
    await expect(page.getByRole('alert').filter({ hasText: '单据版本已更新' })).toBeVisible();
    await expect(dialog.getByTestId('aggregate-line-quantity').first()).toHaveValue('1.2345');
    await dialog.getByRole('button', { name: '关闭', exact: true }).click();
    await page.setViewportSize({ width: 1440, height: 1000 }); await entry.click(); await page.setViewportSize({ width, height: 1000 }); await edit();
    const box = await dialog.locator('.el-dialog').boundingBox(); expect(box.x).toBeGreaterThanOrEqual(0); expect(box.x + box.width).toBeLessThanOrEqual(width);
    const axe = await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa']).analyze(); expect(axe.violations).toEqual([]);
    await dialog.locator('.el-dialog').screenshot({ path: testInfo.outputPath('aggregate-editor.png') });
    await dialog.getByTestId('aggregate-save').focus(); await page.keyboard.press('Enter'); await expect(dialog).not.toBeVisible(); expect(writes).toBe(2);
  });
}
test('已提交单据只读，不渲染明细写按钮', async ({ page }) => {
  await setup(page, permissions, 'Submitted'); await page.getByRole('button', { name: '主从明细', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: '申请单与明细', exact: true }); await expect(dialog).toContainText('当前状态或权限仅允许查看');
  await expect(dialog.getByTestId('aggregate-save')).toHaveCount(0); await expect(dialog.getByTestId('aggregate-add-line')).toHaveCount(0);
});
test('缺明细读取权限不渲染既有明细入口', async ({ page }) => {
  await setup(page, permissions.filter(key => key !== 'enterprise_request.enterprise_request_lines.read'));
  await expect(page.getByRole('button', { name: '主从明细', exact: true })).toHaveCount(0);
});
