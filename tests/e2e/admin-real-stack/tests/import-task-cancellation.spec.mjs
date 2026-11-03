import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';
import { loginAsHostAdmin, enterDevelopmentTenant, clickMainNavLink } from './support/real-stack-auth.mjs';

test('确认取消本人待执行导入，刷新详情与本人任务均保持终态', async ({ page }) => {
  test.setTimeout(150_000); const evidence = process.env.FULLNET_IMPORT_CANCELLATION_UI_EVIDENCE;
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.setViewportSize({ width: 1440, height: 1000 }); await loginAsHostAdmin(page); await enterDevelopmentTenant(page, 'Acme Corporation');
  await clickMainNavLink(page, /导入任务/); await page.getByTestId('import-export-task-create').click();
  const create = page.getByRole('dialog', { name: '提交导入', exact: true });
  await create.getByTestId('import-task-file').setInputFiles(path.join(evidence, 'cancel-positions.xlsx'));
  await create.getByTestId('import-task-submit').click(); const detail = page.getByRole('dialog', { name: '任务详情', exact: true }); await expect(detail).toBeVisible();
  await detail.getByTestId('import-export-task-cancel').click(); await expect(page.getByText('取消后不能继续执行或恢复，已写入的业务数据会保留。确定取消？', { exact: true })).toBeVisible();
  const response = page.waitForResponse(result => result.request().method() === 'POST' && /\/tasks\/[^/]+\/cancel$/.test(new URL(result.url()).pathname));
  await page.getByRole('button', { name: '确定取消', exact: true }).click(); const canceled = await response;
  expect(canceled.status(), await canceled.text()).toBe(200); expect((await canceled.json()).statusKey).toBe('canceled');
  await expect(detail).toContainText('任务已取消'); await expect(detail.getByTestId('import-export-task-cancel')).toHaveCount(0);
  await expect(detail.getByTestId('import-export-task-execute')).toHaveCount(0);
  await detail.getByTestId('import-export-task-refresh').click(); await expect(detail).toContainText('任务已取消');
  await page.setViewportSize({ width: 390, height: 844 }); await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await expect(page.locator('.el-message')).toHaveCount(0);
  const axe = await new AxeBuilder({ page }).include('.el-drawer').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(axe.violations.filter(item => ['critical', 'serious'].includes(item.impact))).toEqual([]);
  await page.screenshot({ path: path.join(evidence, 'import-canceled-mobile.png'), animations: 'disabled' }); await page.keyboard.press('Escape');
  await page.setViewportSize({ width: 1440, height: 1000 }); await clickMainNavLink(page, /我的任务/);
  const imports = page.getByTestId('my-tasks-imports');
  // 断言真实数据行，避免把筛选下拉中的隐藏“已取消”选项误当成任务终态。
  const status = imports.locator('.el-table__body-wrapper .el-tag'); await expect(status).toBeVisible(); await expect(status).toHaveText('已取消');
  await expect(imports.getByTestId('my-tasks-cancel')).toHaveCount(0); await page.screenshot({ path: path.join(evidence, 'my-tasks-canceled.png'), animations: 'disabled' });
});
