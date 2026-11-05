import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';

test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const task = { id, tenantId: id, schemaKey: 'organization.tenant_positions', schemaDisplayName: 'Retention fixture', worksheetKey: 'positions',
  sourceFileId: id, sourceFileName: 'positions.xlsx', statusKey: 'preview_succeeded', totalRows: 1, validRowCount: 1, invalidRowCount: 0,
  errorCode: null, requestedByUserId: id, createdAtUtc: '2026-10-10T00:00:00Z', previewCompletedAtUtc: null, processedRowCount: 0,
  succeededRowCount: 0, executionFailedRowCount: 0, nextLineNumber: 0, executionStartedAtUtc: null, executionCompletedAtUtc: null,
  hasErrorReceipt: false, version: 1, previewRows: [], sourceRetention: { expiresAtUtc: '2026-11-01T00:00:00Z', stateKey: 'expired', releasedAtUtc: null } };
const json = (route, value) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });
async function boot(page, value, locale = 'zh-CN', permissions = ['read', 'execute', 'cancel']) {
  await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Tenant', tenantId: id,
    actorScope: `tenant:${id.replaceAll('-', '')}`, scope: `tenant:${id.replaceAll('-', '')}`, isSuperAdministrator: false,
    passwordChangeRequired: false, permissions: permissions.map(p => `import_export.import_tasks.${p}`), sessionId: id, preferredLocale: locale, profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'fixture', parentId: null, routeName: 'import-export-tasks', path: '/import-export/tasks',
    componentKey: 'import-export-tasks', title: 'Import tasks', caption: '', icon: 'document', order: 1, requiredPermission: 'import_export.import_tasks.read', children: [] }]));
  await page.route('**/api/v1/import-export/tasks?*', route => json(route, { items: [value], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/import-export/tasks/${id}`, route => json(route, value));
  await page.goto('/#/import-export/tasks');
  await page.getByTestId('import-export-task-detail').click();
  await expect(page.getByTestId('import-source-state')).toBeVisible();
  await expect(page.locator('.el-loading-mask')).toHaveCount(0);
}

test('到期来源保留历史并阻止排队，桌面手机均可读', async ({ page }, info) => {
  await boot(page, task);
  await expect(page.getByTestId('import-source-state')).toHaveText('已到期，等待清理');
  await expect(page.getByTestId('import-export-task-execute')).toBeDisabled();
  await expect(page.getByTestId('import-source-unavailable')).toContainText('任务历史、已处理记录和错误回执继续保留');
  expect((await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()).violations).toEqual([]);
  await page.screenshot({ path: path.join(info.outputDir, 'retention-expired.png'), fullPage: true, animations: 'disabled' });
});

test('执行期来源继续保留且允许本人停止排队', async ({ page }) => {
  await boot(page, { ...task, statusKey: 'queued', sourceRetention: { ...task.sourceRetention, stateKey: 'retained_for_execution' } });
  await expect(page.getByTestId('import-source-state')).toHaveText('执行期间继续保留，退出队列后清理');
  await expect(page.getByTestId('import-export-task-cancel')).toBeEnabled();
});

test('已清理来源不妨碍只读用户下载错误回执及英文日期', async ({ page }, info) => {
  await boot(page, { ...task, statusKey: 'execution_failed', hasErrorReceipt: true,
    sourceRetention: { ...task.sourceRetention, stateKey: 'released', releasedAtUtc: '2026-11-02T00:00:00Z' } }, 'en-US', ['read']);
  await expect(page.getByTestId('import-source-state')).toHaveText('Source workbook cleaned');
  await expect(page.getByTestId('import-export-task-error-receipt')).toBeEnabled();
  await expect(page.getByTestId('import-export-task-execute')).toHaveCount(0);
  await expect(page.getByTestId('import-source-expiry')).toHaveAttribute('datetime', task.sourceRetention.expiresAtUtc);
  await page.screenshot({ path: path.join(info.outputDir, 'retention-readonly-en.png'), fullPage: true, animations: 'disabled' });
});

test('服务端到期冲突不会自动重发或使用旧可用状态', async ({ page }) => {
  await boot(page, { ...task, sourceRetention: { ...task.sourceRetention, stateKey: 'available', expiresAtUtc: '2027-11-01T00:00:00Z' } });
  let writes = 0;
  await page.route(`**/api/v1/import-export/tasks/${id}/execute`, route => {
    writes++;
    return route.fulfill({ status: 409, contentType: 'application/problem+json', body: JSON.stringify({ status: 409, code: 'import_export.source.expired', title: 'Expired' }) });
  });
  await page.getByTestId('import-export-task-execute').click();
  await expect(page.getByTestId('import-source-state')).toHaveText('已到期，等待清理');
  await expect(page.getByTestId('import-export-task-execute')).toBeDisabled();
  expect(writes).toBe(1);
});
