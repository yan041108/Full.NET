import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';

test('草稿参数改变后仍读取明确发布版并执行相同版本', async ({ page }) => {
  test.setTimeout(120_000); const evidence = process.env.FULLNET_REPORTING_UI_EVIDENCE, name = process.env.FULLNET_E2E_REPORTING_DEFINITION_NAME;
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN')); await page.setViewportSize({ width: 1440, height: 1000 });
  await loginAsHostAdmin(page); let draftReads = 0;
  page.on('request', request => { if (request.method() === 'GET' && /\/api\/v1\/reporting\/definitions\/[^/]+$/.test(new URL(request.url()).pathname)) draftReads++; });
  await clickMainNavLink(page, /报表执行/); await page.getByTestId('reporting-execute-definition').click();
  await page.getByRole('option', { name: `${name} (browser_version)`, exact: true }).click();
  await expect(page.getByTestId('reporting-execute-version')).toContainText('已载入发布版本 1'); await expect(page.getByText('未发布草稿参数', { exact: true })).toHaveCount(0);
  const executing = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/execute') && response.request().method() === 'POST');
  await page.getByTestId('reporting-execute-run').click(); const response = await executing; expect(response.status(), await response.text()).toBe(200);
  expect(response.request().postDataJSON()).toEqual({ versionNumber: 1, parameters: [] });
  const result = await response.json(); expect(result.versionNumber).toBe(1); expect(result.queryPortKey).toBe('reporting.database_engine_version'); expect(draftReads).toBe(0);
  expect(result.columns[0].columnKey).toBe('EngineVersion'); expect(result.rows[0].values.EngineVersion).toBe('2026.1'); expect(result.rows[0].values).not.toHaveProperty('engineVersion');
  await expect(page.getByText('2026.1', { exact: true })).toBeVisible(); await expect(page.getByTestId('reporting-execute-result-version')).toContainText('结果发布版本 1');
  await expect.poll(() => page.evaluate(() => document.getAnimations().filter(animation => animation.playState === 'running' && animation.effect?.getComputedTiming().iterations !== Infinity).length)).toBe(0);
  const accessibility = await new AxeBuilder({ page }).include('.reporting-execute-view').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(accessibility.violations.filter(item => ['critical', 'serious'].includes(item.impact))).toEqual([]);
  await page.screenshot({ path: path.join(evidence, 'report-execute-desktop.png'), animations: 'disabled' }); await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.getByTestId('reporting-execute-result-version').scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(evidence, 'report-execute-mobile.png'), animations: 'disabled' });
});
