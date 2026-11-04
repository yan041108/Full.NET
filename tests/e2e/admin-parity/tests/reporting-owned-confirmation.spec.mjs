import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
test.afterEach(async ({ page }, info) => { await page.screenshot({ path: info.outputPath('page.png'), fullPage: true, animations: 'disabled' }); });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', groupId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const draft = { id, groupId, dataSourceId: groupId, definitionKey: 'report-review', name: '删除确认验收', description: null, queryPortKey: 'metadata.schemas',
  parameterSchema: [], layoutConfigJson: '{}', latestPublishedVersionNumber: 1, isEnabled: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 7 };
const { layoutConfigJson: _, parameterSchema: __, ...summary } = draft;
const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
async function boot(page) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions: ['reporting.definitions.read', 'reporting.definitions.update', 'reporting.definitions.delete'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'reporting', parentId: null, routeName: 'reporting-definitions', path: '/reporting/definitions', componentKey: 'reporting-definitions',
    title: '报表配置', caption: '', icon: 'document', order: 1, requiredPermission: 'reporting.definitions.read', children: [] }]));
  await page.route('**/api/v1/reporting/definitions?*', route => json(route, { items: [summary], page: 1, pageSize: 20, total: 1 }));
  await page.route('**/api/v1/reporting/definitions/' + id, route => json(route, draft));
  await page.goto('/#/reporting/definitions'); await expect(page.getByTestId('reporting-config-delete')).toBeEnabled();
}
async function fits(dialog, page) {
  await expect.poll(async () => { const box = await dialog.boundingBox(); return !!box && box.x >= 0 && box.x + box.width <= page.viewportSize().width; }).toBe(true);
  await expect.poll(() => dialog.evaluate(element => {
    for (let node = element.querySelector('.el-dialog') ?? element; node; node = node.parentElement)
      if (getComputedStyle(node).opacity !== '1' || node.getAnimations().some(animation => animation.playState === 'running')) return false;
    return true;
  })).toBe(true);
}
async function openDelete(page) {
  await page.getByRole('row').filter({ hasText: '删除确认验收' }).getByTestId('art-table-action-more').click();
  await page.getByTestId('reporting-config-delete').click();
}
test('删除未应用保留资源，人工刷新后确认成功，陈旧目录不能复活资源', async ({ page }, info) => {
  await boot(page); let writes = 0;
  await page.route('**/api/v1/reporting/definitions/' + id, route => {
    if (route.request().method() !== 'DELETE') return json(route, draft);
    return json(route, ++writes > 1);
  });
  await openDelete(page); const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('删除确认验收'); await fits(dialog, page);
  await expect.poll(async () => (await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations.map(item => item.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath('deletion-confirm.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('reporting-management-confirm').click(); await expect(dialog).toHaveCount(0);
  await expect(page.getByTestId('reporting-config-delete')).toBeDisabled(); await expect(page.getByTestId('reporting-config-unconfirmed')).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: '删除确认验收' })).toHaveCount(1);
  await page.getByTestId('reporting-config-refresh').click(); await expect(page.getByTestId('reporting-config-delete')).toBeEnabled();
  await openDelete(page); await page.getByTestId('reporting-management-confirm').click();
  await expect(page.getByRole('row').filter({ hasText: '删除确认验收' })).toHaveCount(0);
  await page.getByTestId('reporting-config-refresh').click(); await expect(page.getByRole('row').filter({ hasText: '删除确认验收' })).toHaveCount(0); expect(writes).toBe(2);
});
test('取消和离开页面都会关闭资源确认，重新进入不自动删除', async ({ page }) => {
  await boot(page); let writes = 0;
  await page.route('**/api/v1/reporting/definitions/' + id, route => { if (route.request().method() === 'DELETE') writes++; return json(route, draft); });
  await openDelete(page); await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0); await openDelete(page);
  await page.goto('/#/'); await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.goto('/#/reporting/definitions'); await expect(page.getByTestId('reporting-config-delete')).toBeEnabled(); expect(writes).toBe(0);
});
test('嵌套丢弃确认可取消保留输入，键盘确认后只读取新草稿', async ({ page }, info) => {
  await boot(page); let reads = 0, writes = 0;
  await page.route('**/api/v1/reporting/definitions/' + id, route => {
    if (route.request().method() !== 'GET') writes++;
    return json(route, { ...draft, name: ++reads === 1 ? draft.name : '服务端新草稿', version: reads === 1 ? 7 : 8 });
  });
  await page.getByTestId('reporting-config-edit').click(); await page.getByTestId('reporting-config-name').fill('保留本地输入');
  await page.getByTestId('reporting-config-draft-reload').click(); const confirmDialog = page.getByRole('dialog').filter({ has: page.getByTestId('reporting-management-confirm') });
  await expect(confirmDialog).toContainText('放弃本地输入'); await fits(confirmDialog, page);
  await page.screenshot({ path: info.outputPath('discard-confirm.png'), fullPage: true, animations: 'disabled' });
  await confirmDialog.getByRole('button', { name: '取消', exact: true }).click(); await expect(page.getByTestId('reporting-config-name')).toHaveValue('保留本地输入'); expect(reads).toBe(1);
  await page.getByTestId('reporting-config-draft-reload').click(); await page.getByTestId('reporting-management-confirm').focus(); await page.keyboard.press('Enter');
  await expect(page.getByTestId('reporting-config-name')).toHaveValue('服务端新草稿'); expect(reads).toBe(2); expect(writes).toBe(0);
});
