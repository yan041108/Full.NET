import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const permissions = ['identity.navigation.read', 'import_export.import_tasks.read', 'import_export.import_tasks.create',
  'import_export.static_schemas.read', 'enterprise_request.enterprise_requests.create'];
const schema = { schemaKey: 'enterprise.requests', displayName: '企业申请', scopeKey: 'tenant', requiredPermission: permissions[4],
  worksheets: [{ worksheetKey: 'requests', displayName: '申请', headerColumns: ['title'] }] };
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });
async function setup(page, allowed = permissions) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', r => json(r, { accessToken: 'import-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', r => json(r, { id: id(1), username: 'import', displayName: 'Import operator', actorScope: 'tenant', scope: `tenant:${id(2).replaceAll('-', '')}`,
    isSuperAdministrator: false, tenantId: id(2), permissions: allowed, sessionId: id(3), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', r => json(r, [{ id: 'import-export-tasks', parentId: null, routeName: 'import-export-tasks', path: '/import-export/tasks',
    componentKey: 'import-export-tasks', title: '导入任务', caption: 'Import tasks', icon: 'collection', order: 1, requiredPermission: permissions[1], children: [] }]));
  for (const [path, value] of [['tenancy/available', []], ['notifications/my-inbox-messages/unread-count', { unreadCount: 0 }],
    ['identity/session-policy', { loginPolicy: 0 }], ['me/tenant-invitations', []]]) await page.route(`**/api/v1/${path}`, r => json(r, value));
  await page.route('**/api/v1/import-export/tasks?*', r => json(r, { items: [], total: 0, page: 1, pageSize: 20 }));
  await page.route('**/api/v1/import-export/schemas', r => json(r, [schema]));
  await page.route('**/api/v1/import-export/schemas/*/worksheets/*/template', r => r.fulfill({ contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', body: Buffer.from('PK-template') }));
  await page.goto('/#/import-export/tasks'); await page.getByRole('link', { name: '导入任务', exact: true }).click();
}

for (const width of [1440, 390]) {
  test(`导入创建：模板下载、拒绝恢复与预校验结果 ${width}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); await setup(page);
    let attempts = 0;
    await page.route('**/api/v1/import-export/tasks', r => {
      attempts++; expect(r.request().postDataBuffer().toString()).toContain('filename="requests.xlsx"');
      if (attempts === 1) return json(r, { status: 422, code: 'import.invalid', title: '工作簿校验失败' }, 422);
      return json(r, { id: id(4), tenantId: id(2), schemaKey: schema.schemaKey, schemaDisplayName: '企业申请', worksheetKey: 'requests',
        sourceFileId: id(5), sourceFileName: 'requests.xlsx', statusKey: 'preview_failed', totalRows: 1, validRowCount: 0, invalidRowCount: 1,
        errorCode: null, requestedByUserId: id(1), createdAtUtc: '2026-11-02T00:00:00Z', previewCompletedAtUtc: null,
        processedRowCount: 0, succeededRowCount: 0, executionFailedRowCount: 0, nextLineNumber: 0, executionStartedAtUtc: null,
        executionCompletedAtUtc: null, hasErrorReceipt: false, version: 1,
        previewRows: [{ lineNumber: 2, isValid: false, errorCode: 'row.invalid', message: '金额精度超过两位' }] }, 201);
    });
    await page.getByTestId('import-export-task-create').click();
    await page.setViewportSize({ width, height: 1000 });
    const dialog = page.getByRole('dialog', { name: '提交导入', exact: true });
    await expect(dialog.getByTestId('import-task-template')).toBeEnabled();
    const downloaded = page.waitForEvent('download'); await dialog.getByTestId('import-task-template').click();
    expect((await downloaded).suggestedFilename()).toBe('enterprise.requests-requests.xlsx');
    await dialog.getByTestId('import-task-file').setInputFiles({ name: 'requests.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: Buffer.from('PK-upload') });
    await dialog.getByTestId('import-task-submit').click(); await expect(page.getByRole('alert').filter({ hasText: '工作簿校验失败' })).toBeVisible();
    const box = await dialog.locator('.el-dialog').boundingBox(); expect(box.x).toBeGreaterThanOrEqual(0); expect(box.x + box.width).toBeLessThanOrEqual(width);
    await dialog.locator('.el-dialog').screenshot({ path: testInfo.outputPath('import-dialog.png') });
    const axe = await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa']).analyze(); expect(axe.violations).toEqual([]);
    await dialog.getByTestId('import-task-submit').focus(); await page.keyboard.press('Enter');
    await expect(dialog).not.toBeVisible();
    const detail = page.getByRole('dialog', { name: '任务详情', exact: true }); await expect(detail).toContainText('金额精度超过两位');
    expect(attempts).toBe(2); await expect(page.getByTestId('import-export-task-execute')).toHaveCount(0);
  });
}
test('缺少模板权限不渲染提交入口', async ({ page }) => {
  await setup(page, permissions.filter(p => p !== 'import_export.static_schemas.read'));
  await expect(page.getByRole('heading', { name: '导入任务', exact: true })).toBeVisible();
  await expect(page.getByTestId('import-export-task-create')).toHaveCount(0);
});
