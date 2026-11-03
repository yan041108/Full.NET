import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';

test('Host 新建数据源，编辑留空密码保留凭据并显式禁用', async ({ page }) => {
  test.setTimeout(120_000); const evidence = process.env.FULLNET_REPORTING_UI_EVIDENCE, name = 'Source UI ' + Date.now();
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN')); await page.setViewportSize({ width: 1440, height: 1000 });
  await loginAsHostAdmin(page); await clickMainNavLink(page, /报表数据源/); await page.getByTestId('reporting-data-source-create').click();
  const create = page.getByRole('dialog', { name: '新建报表数据源', exact: true });
  for (const [key, value] of Object.entries({ name, host: 'reports.example.com', port: '1433', database: 'reports', username: 'readonly', password: 'Controlled!Fixture2026' })) await create.getByTestId(`reporting-source-${key}`).fill(value);
  const creating = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/reporting/data-sources' && response.request().method() === 'POST');
  await create.getByTestId('reporting-data-source-editor-submit').click(); const creation = await creating; expect(creation.status(), await creation.text()).toBe(201);
  const source = await creation.json(); expect(source.hasPassword).toBe(true); expect(source).not.toHaveProperty('password'); expect(source.trustServerCertificate).toBe(false); await expect(create).not.toBeVisible();
  const row = page.locator('.reporting-data-sources-view .el-table__row').filter({ hasText: name }); await expect(row).toBeVisible();
  await row.getByTestId('reporting-data-source-edit').click(); const editor = page.getByRole('dialog', { name: '编辑报表数据源', exact: true });
  await expect(editor.getByTestId('reporting-source-password')).toHaveValue(''); await editor.getByTestId('reporting-source-name').fill(name + ' Updated');
  await expect.poll(() => page.evaluate(() => document.getAnimations().filter(animation => animation.playState === 'running' && animation.effect?.getComputedTiming().iterations !== Infinity).length)).toBe(0);
  const accessibility = await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(accessibility.violations.filter(item => ['critical', 'serious'].includes(item.impact))).toEqual([]);
  await page.screenshot({ path: path.join(evidence, 'report-source-desktop.png'), animations: 'disabled' }); await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await editor.getByTestId('reporting-source-password').scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(evidence, 'report-source-mobile.png'), animations: 'disabled' }); await page.setViewportSize({ width: 1440, height: 1000 });
  const updating = page.waitForResponse(response => new URL(response.url()).pathname === `/api/v1/reporting/data-sources/${source.id}` && response.request().method() === 'PUT');
  await editor.getByTestId('reporting-data-source-editor-submit').click(); const updated = await updating; expect(updated.status(), await updated.text()).toBe(200);
  expect(updated.request().postDataJSON().password).toBeNull(); expect(updated.request().postDataJSON().version).toBe(source.version); expect((await updated.json()).hasPassword).toBe(true); await expect(editor).not.toBeVisible();
  await row.getByTestId('reporting-data-source-disable').click(); await page.locator('.el-message-box').getByRole('button', { name: '确定', exact: true }).click();
  await expect(row.getByTestId('reporting-data-source-disable')).toBeDisabled();
});
