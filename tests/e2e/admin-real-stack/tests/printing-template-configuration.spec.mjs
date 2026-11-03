import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';

test('Host 草稿保存与显式发布分离，历史版式不可变并在隔离框内查看', async ({ page }) => {
  test.setTimeout(180_000); const evidence = process.env.FULLNET_PRINTING_CONFIGURATION_UI_EVIDENCE;
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN')); await page.setViewportSize({ width: 1440, height: 1000 });
  await loginAsHostAdmin(page); await clickMainNavLink(page, /打印预览/);
  let publications = 0; page.on('request', request => { if (request.method() === 'POST' && /\/printing\/templates\/[^/]+\/publish$/.test(new URL(request.url()).pathname)) publications++; });
  await page.getByTestId('printing-preview-create').click(); const editor = page.getByRole('dialog', { name: '新建模板草稿', exact: true });
  await editor.getByTestId('printing-template-key').fill('browser-layout-' + Date.now().toString(36)); await editor.getByTestId('printing-template-name').fill('版式配置验收');
  const layout = editor.getByTestId('printing-template-layout'); await layout.fill('<h1>Layout one INSERT</h1>');
  await layout.evaluate(input => { input.focus(); const start = input.value.indexOf('INSERT'); input.setSelectionRange(start, start + 6); });
  await editor.locator('[data-field-key="tenantName"]').click(); await expect(layout).toHaveValue('<h1>Layout one {{tenantName}}</h1>'); expect(publications).toBe(0);
  const editorAccessibility = await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(editorAccessibility.violations.filter(item => ['critical', 'serious'].includes(item.impact))).toEqual([]);
  await page.screenshot({ path: path.join(evidence, 'printing-fields-desktop.png'), animations: 'disabled' });
  await page.setViewportSize({ width: 390, height: 844 }); await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await editor.locator('[data-testid="printing-template-fields"]').scrollIntoViewIfNeeded(); await expect(editor.locator('[data-field-key="tenantName"]')).toBeVisible();
  await page.screenshot({ path: path.join(evidence, 'printing-fields-mobile.png'), animations: 'disabled' }); await page.setViewportSize({ width: 1440, height: 1000 });
  const created = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/printing/templates' && response.request().method() === 'POST');
  await editor.getByTestId('printing-preview-submit').click(); const creation = await created; expect(creation.status(), await creation.text()).toBe(201);
  const draft = await creation.json(); expect(draft.latestPublishedVersionNumber).toBe(0); await expect(editor).not.toBeVisible(); expect(publications).toBe(0);
  async function edit(html) {
    await page.getByTestId('printing-template-edit').click(); const dialog = page.getByRole('dialog', { name: '编辑模板草稿', exact: true });
    await dialog.getByTestId('printing-template-layout').fill(html);
    const update = page.waitForResponse(response => new URL(response.url()).pathname === `/api/v1/printing/templates/${draft.id}` && response.request().method() === 'PUT');
    await dialog.getByTestId('printing-preview-submit').click(); const saved = await update; expect(saved.status(), await saved.text()).toBe(200); await expect(dialog).not.toBeVisible();
  }
  async function publish(note, number) {
    await page.getByTestId('printing-template-publish').click(); const dialog = page.getByRole('dialog', { name: '发布版本', exact: true });
    await expect(dialog).toContainText('不可变版本'); await dialog.getByTestId('printing-template-change-note').fill(note);
    const publish = page.waitForResponse(response => new URL(response.url()).pathname === `/api/v1/printing/templates/${draft.id}/publish` && response.request().method() === 'POST');
    await dialog.getByTestId('printing-template-confirm-publish').click(); const result = await publish; expect(result.status(), await result.text()).toBe(200);
    expect((await result.json()).versionNumber).toBe(number); await expect(dialog).not.toBeVisible(); await expect(page.getByTestId('printing-template-versions')).toContainText(`最新发布版本 ${number}`);
  }
  await edit('<h1>Layout two {{tenantName}}</h1>'); expect(publications).toBe(0); await publish('First layout', 1);
  await edit('<h1>Layout three {{tenantName}}</h1><script>window.printTemplateUnsafe=true</script>'); expect(publications).toBe(1); await publish('Second layout', 2); expect(publications).toBe(2);
  await expect(page.locator('.el-message')).toHaveCount(0);
  // 新配置控件的扫描在版式 iframe 建立前完成，隔离框本身保持禁脚本并单独检查标题和内容。
  const accessibility = await new AxeBuilder({ page }).include('.printing-preview-view').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(accessibility.violations.filter(item => ['critical', 'serious'].includes(item.impact))).toEqual([]);
  await page.getByTestId('printing-template-version').click(); await page.getByRole('option', { name: '1 · First layout', exact: true }).click();
  await page.getByTestId('printing-preview-run').click(); const frame = page.frameLocator('iframe[title="打印版式示例"]');
  await expect(frame.getByRole('heading', { name: 'Layout two {{tenantName}}', exact: true })).toBeVisible();
  await expect(page.locator('.printing-preview-surface')).toContainText('占位字段尚未绑定');
  await expect(page.locator('iframe.printing-preview-html')).toHaveAttribute('sandbox', 'allow-same-origin allow-modals');
  await page.screenshot({ path: path.join(evidence, 'printing-configuration-desktop.png'), animations: 'disabled' });
  await page.getByTestId('printing-template-version').click(); await page.getByRole('option', { name: '2 · Second layout', exact: true }).click();
  await page.getByTestId('printing-preview-run').click(); await expect(frame.getByRole('heading', { name: 'Layout three {{tenantName}}', exact: true })).toBeVisible();
  await expect(frame.locator('script')).toHaveCount(0); expect(await page.evaluate(() => window.printTemplateUnsafe)).toBeUndefined();
  await page.setViewportSize({ width: 390, height: 844 }); await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.locator('.printing-preview-surface').scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(evidence, 'printing-configuration-mobile.png'), animations: 'disabled' });
});
