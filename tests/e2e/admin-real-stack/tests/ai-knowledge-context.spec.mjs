import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import path from 'node:path';
const readyPath = process.env.FULLNET_K03_CONTEXT_BROWSER_READY;
const fixture = JSON.parse(readFileSync(readyPath, 'utf8'));
process.env.FULLNET_E2E_USERNAME = fixture.username; process.env.FULLNET_E2E_PASSWORD = fixture.password;
const { loginAsHostAdmin } = await import('./support/real-stack-auth.mjs');
const bridge = path.dirname(readyPath);
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`真实发布上下文与版本边界 ${viewport.width}`, async ({ page }, testInfo) => {
    test.setTimeout(240_000); const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.setViewportSize({ width: 1440, height: 1000 }); await loginAsHostAdmin(page); await page.setViewportSize(viewport);
    await page.goto('/#/ai/knowledge-search'); const view = page.getByTestId('knowledge-search');
    await page.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: fixture.knowledgeName, exact: true }).click();
    await view.getByTestId('search-query').fill('明确 补写'); await view.getByTestId('search-submit').click();
    await expect(view.getByTestId('search-hit')).toHaveCount(17);
    await view.getByTestId('search-context').first().focus(); await page.keyboard.press('Enter'); let dialog = page.getByTestId('knowledge-context-dialog');
    await expect(dialog.getByTestId('context-block')).toHaveCount(2); await expect(dialog).toContainText('明确补写条目');
    await dialog.getByTestId('context-preview').first().click(); const source = page.getByTestId('knowledge-source-dialog');
    await source.getByTestId('knowledge-source-read').click(); await expect(source.getByTestId('knowledge-source-text')).toHaveText(fixture.original);
    await source.getByRole('button', { name: '取消', exact: true }).click();
    if (viewport.width === 1440) {
      writeFileSync(path.join(bridge, 'advance.json'), JSON.stringify({ advance: true }));
      await expect.poll(() => existsSync(path.join(bridge, 'replaced.json')), { timeout: 150_000 }).toBe(true);
      await dialog.getByTestId('context-refresh').focus(); await page.keyboard.press('Enter'); await expect(view.getByTestId('search-changed')).toBeVisible();
      await expect(view.getByTestId('search-hit')).toHaveCount(0); await expect(page.getByTestId('knowledge-context-dialog')).toHaveCount(0);
      await view.getByTestId('search-adopt').click(); await expect(view.getByTestId('search-hit')).toHaveCount(17);
      await view.getByTestId('search-context').first().click(); dialog = page.getByTestId('knowledge-context-dialog');
      await expect(dialog.getByTestId('context-block')).toHaveCount(2); await expect(dialog).toContainText('下一份人工草稿');
    }
    await dialog.getByTestId('context-radius').click(); await page.getByRole('option', { name: '前后各两块', exact: true }).click();
    await expect(dialog.getByTestId('context-block')).toHaveCount(3);
    expect((await new AxeBuilder({ page }).include('.knowledge-context-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('published-context.png') }); expect(errors).toEqual([]);
  });
}
