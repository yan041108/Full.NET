import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';
import { loginAsHostAdmin } from './support/real-stack-auth.mjs';

test('Host 使用恢复的后台操作入口并完成大屏草稿发布预览', async ({ page }) => {
  test.setTimeout(180_000);
  const evidence = process.env.FULLNET_ADMIN_TOOLBAR_UI_EVIDENCE;
  const pageErrors = []; page.on('pageerror', error => pageErrors.push(error.message));
  await page.setViewportSize({ width: 1440, height: 1000 });
  await loginAsHostAdmin(page);
  const origin = new URL(page.url()).origin;
  async function visit(route) { await page.goto(`${origin}/#${route}`); }
  // 仅打开配置表单，避免把真实厂商调用混入后台入口验收。
  for (const [route, id] of [
    ['/identity/registration-ways', 'registration-ways-action-create'],
    ['/identity/oauth-providers', 'oauth-provider-create'],
    ['/k3cloud/connection-configs', 'k3cloud-connection-create'],
    ['/k3cloud/document-syncs', 'k3cloud-document-sync-create']
  ]) {
    await visit(route); await page.getByTestId(id).click();
    const dialog = page.getByRole('dialog'); await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: '取消', exact: true }).click(); await expect(dialog).toBeHidden();
  }
  await visit('/ai/model-configs'); await expect(page.getByTestId('ai-model-config-create')).toBeVisible();
  await expect(page.locator('.art-search-bar')).toBeVisible();
  await page.locator('.art-search-bar input').fill('toolbar-missing-model');
  const searching = page.waitForResponse(r => r.request().method() === 'GET' && new URL(r.url()).searchParams.get('nameContains') === 'toolbar-missing-model');
  await page.locator('.art-search-bar').getByRole('button', { name: '查询', exact: true }).click();
  expect((await searching).ok()).toBe(true);
  await visit('/ocr/id-card-tasks');
  const chooser = page.waitForEvent('filechooser'); await page.getByTestId('ocr-id-card-upload').getByRole('button', { name: '上传识别', exact: true }).click(); await chooser;
  await visit('/goview/projects'); await page.getByTestId('goview-project-create').click();
  const dialog = page.getByRole('dialog'); const name = `Toolbar dashboard ${Date.now()}`;
  await dialog.getByTestId('goview-project-key-input').fill(`toolbar-${Date.now()}`);
  await dialog.getByTestId('goview-project-name-input').fill(name);
  const creating = page.waitForResponse(r => new URL(r.url()).pathname === '/api/v1/goview/projects' && r.request().method() === 'POST');
  await dialog.getByRole('button', { name: '确定', exact: true }).click();
  const created = await creating; expect(created.status(), await created.text()).toBe(201); const project = await created.json();
  await expect(page.getByTestId('goview-editor-name')).toHaveValue(name);
  await expect(page.getByTestId('goview-editor-open-preview')).toBeDisabled();
  const canvas = { width: 600, height: 400, backgroundColor: '#0a1628', components: [{ id: 'title', type: 'text', x: 0, y: 0, width: 500, height: 80, props: { text: 'Toolbar published dashboard', color: '#ffffff', fontSize: 24 } }] };
  await page.getByTestId('goview-editor-canvas-json').fill(JSON.stringify(canvas));
  const saving = page.waitForResponse(r => new URL(r.url()).pathname === `/api/v1/goview/projects/${project.id}` && r.request().method() === 'PUT');
  await page.getByTestId('goview-editor-save').click(); const saved = await saving; expect(saved.status(), await saved.text()).toBe(200);
  const publishing = page.waitForResponse(r => new URL(r.url()).pathname === `/api/v1/goview/projects/${project.id}/publish` && r.request().method() === 'POST');
  await page.getByTestId('goview-editor-publish').click(); const published = await publishing; expect(published.ok(), await published.text()).toBe(true);
  await expect(page.getByTestId('goview-editor-open-preview')).toBeEnabled();
  await expect(page.locator('.el-message')).toHaveCount(0);
  const accessibility = await new AxeBuilder({ page }).include('.art-table-header').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(accessibility.violations.filter(v => ['critical', 'serious'].includes(v.impact))).toEqual([]);
  await page.screenshot({ path: path.join(evidence, 'toolbar-desktop.png'), animations: 'disabled' });
  await page.setViewportSize({ width: 390, height: 844 });
  for (const id of ['goview-editor-save', 'goview-editor-publish', 'goview-editor-open-preview']) await expect(page.getByTestId(id)).toBeVisible();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: path.join(evidence, 'toolbar-mobile.png'), animations: 'disabled' });
  await page.getByTestId('goview-editor-open-preview').click(); await expect(page.getByTestId('goview-preview-stage')).toContainText('Toolbar published dashboard');
  const reload = page.waitForResponse(r => new URL(r.url()).pathname === `/api/v1/goview/projects/${project.id}/preview` && r.request().method() === 'POST');
  await page.getByTestId('goview-preview-reload').click(); const reloaded = await reload; expect(reloaded.ok(), await reloaded.text()).toBe(true);
  expect(JSON.parse((await reloaded.json()).canvasJson)).toEqual(canvas);
  expect(pageErrors).toEqual([]);
});
