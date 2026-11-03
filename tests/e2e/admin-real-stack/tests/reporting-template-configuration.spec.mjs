import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';

test('报表草稿发生真实版本冲突后重新载入，独立确认发布并读取历史摘要', async ({ page }) => {
  test.setTimeout(150_000); const evidence = process.env.FULLNET_REPORTING_UI_EVIDENCE;
  const name = process.env.FULLNET_E2E_REPORTING_DEFINITION_NAME;
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN')); await page.setViewportSize({ width: 1440, height: 1000 });
  await loginAsHostAdmin(page); await clickMainNavLink(page, /报表定义/);
  let publishes = 0; page.on('request', request => { if (request.method() === 'POST' && /\/reporting\/definitions\/[^/]+\/publish$/.test(new URL(request.url()).pathname)) publishes++; });
  await page.getByTestId('reporting-definition-create').click(); const create = page.getByRole('dialog', { name: '新建报表定义', exact: true });
  await create.getByTestId('reporting-config-key').fill('browser-draft-' + Date.now().toString(36)); await create.getByTestId('reporting-config-name').fill('未发布的独立新草稿');
  const creating = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/reporting/definitions' && response.request().method() === 'POST');
  await create.getByTestId('reporting-config-draft-save').click(); const creation = await creating; expect(creation.status(), await creation.text()).toBe(201);
  expect((await creation.json()).latestPublishedVersionNumber).toBe(0); await expect(create).not.toBeVisible(); expect(publishes).toBe(0);
  const row = page.locator('.definitions-card .el-table__row').filter({ hasText: name }); await expect(row).toBeVisible();
  await row.getByTestId('reporting-config-edit').click(); const editor = page.getByRole('dialog', { name: '编辑报表定义', exact: true });
  await editor.getByTestId('reporting-config-name').fill(name + ' Local edit');
  // 先以同一真实 HTTP 版本提交另一份草稿，再让原请求触发服务端 CAS 冲突。
  await page.route('**/api/v1/reporting/definitions/*', async route => {
    if (route.request().method() !== 'PUT') { await route.continue(); return; }
    const body = route.request().postDataJSON(); const competing = await route.fetch({ postData: { ...body, name: name + ' Concurrent' } });
    expect(competing.status(), await competing.text()).toBe(200); await route.continue();
  }, { times: 1 });
  const failed = page.waitForResponse(response => /\/reporting\/definitions\/[^/]+$/.test(new URL(response.url()).pathname) && response.request().method() === 'PUT');
  await editor.getByTestId('reporting-config-draft-save').click(); expect((await failed).status()).toBe(409);
  await expect(editor.getByTestId('reporting-config-name')).toHaveValue(name + ' Local edit'); await expect(editor.getByTestId('reporting-config-draft-save')).toHaveCount(0);
  await editor.getByTestId('reporting-config-draft-reload').click(); await page.locator('.el-message-box').getByRole('button', { name: '确定', exact: true }).click();
  await expect(editor.getByTestId('reporting-config-name')).toHaveValue(name + ' Concurrent'); await editor.getByTestId('reporting-config-name').fill(name + ' Updated');
  const saved = page.waitForResponse(response => /\/reporting\/definitions\/[^/]+$/.test(new URL(response.url()).pathname) && response.request().method() === 'PUT');
  await editor.getByTestId('reporting-config-draft-save').click(); expect((await saved).status()).toBe(200); await expect(editor).not.toBeVisible(); expect(publishes).toBe(0);
  await row.getByTestId('reporting-config-publish').click(); const confirmation = page.getByRole('dialog', { name: '发布报表版本', exact: true });
  await expect(confirmation).toContainText('不可变版本'); expect(publishes).toBe(0); await confirmation.getByTestId('reporting-config-publish-note').fill('真实页面确认发布');
  // 等待旧编辑器退出与新确认框进入动画结束，再检查最终文字和背景对比度。
  await expect.poll(() => page.evaluate(() => document.getAnimations().filter(animation => animation.playState === 'running'
    && animation.effect?.getComputedTiming().iterations !== Infinity).length)).toBe(0);
  const accessibility = await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(accessibility.violations.filter(item => ['critical', 'serious'].includes(item.impact))).toEqual([]);
  await page.screenshot({ path: path.join(evidence, 'report-configuration-desktop.png'), animations: 'disabled' });
  await page.setViewportSize({ width: 390, height: 844 }); await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: path.join(evidence, 'report-configuration-mobile.png'), animations: 'disabled' }); await page.setViewportSize({ width: 1440, height: 1000 });
  const published = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/publish') && response.request().method() === 'POST');
  await confirmation.getByTestId('reporting-config-publish-confirm').click(); const publication = await published; expect(publication.status(), await publication.text()).toBe(200);
  expect((await publication.json()).versionNumber).toBe(2); await expect(confirmation).not.toBeVisible(); expect(publishes).toBe(1);
  await row.getByTestId('reporting-config-history').click();
  const history = page.getByRole('dialog', { name: '发布版本', exact: true }); await expect(history).toContainText('真实页面确认发布'); await expect(history).toContainText('Browser proof');
});
