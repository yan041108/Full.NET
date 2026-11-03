import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import path from 'node:path';
const readyPath = process.env.FULLNET_K03_COMPARE_BROWSER_READY;
const fixture = JSON.parse(readFileSync(readyPath, 'utf8'));
process.env.FULLNET_E2E_USERNAME = fixture.username; process.env.FULLNET_E2E_PASSWORD = fixture.password;
const { loginAsHostAdmin } = await import('./support/real-stack-auth.mjs');
const bridge = path.dirname(readyPath);
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`同输入关键词向量比较与收费边界 ${viewport.width}`, async ({ page }, testInfo) => {
    test.setTimeout(240_000); page.setDefaultTimeout(30_000); const errors = []; page.on('pageerror', error => errors.push(error.message));
    let calls = 0; page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/knowledge-search/compare')) ++calls; });
    await page.setViewportSize({ width: 1440, height: 1000 }); await loginAsHostAdmin(page); await page.setViewportSize(viewport);
    await page.goto('/#/ai/knowledge-search'); const view = page.getByTestId('knowledge-search');
    await page.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: fixture.knowledgeName, exact: true }).click();
    // 合并块保留独立片段；替换原文片段后，补写片段仍匹配，两个发布均为十七个命中。
    await view.getByTestId('search-query').fill('明确 补写'); await view.getByTestId('search-submit').click(); await expect(view.getByTestId('search-hit')).toHaveCount(17);
    await expect(view.getByTestId('search-answer')).toHaveCount(0);
    await view.getByTestId('search-compare').focus(); await page.keyboard.press('Enter'); let dialog = page.getByTestId('knowledge-comparison-dialog');
    expect(calls).toBe(0); await dialog.getByTestId('compare-confirm').focus(); await page.keyboard.press('Enter');
    await expect(dialog.getByTestId('comparison-vector').getByTestId('comparison-hit')).toHaveCount(5);
    await expect(dialog.getByTestId('comparison-keyword').getByTestId('comparison-hit')).toHaveCount(5);
    await expect(dialog.getByTestId('comparison-usage')).toContainText('模型已报告用量'); await expect(dialog.getByTestId('comparison-diagnostics')).toHaveCount(0);
    expect(calls).toBe(1); await expect(dialog.getByTestId('compare-confirm')).toBeDisabled();
    await dialog.getByTestId('comparison-preview').first().click(); const source = page.getByTestId('knowledge-source-dialog');
    await source.getByTestId('knowledge-source-read').click(); await expect(source.getByTestId('knowledge-source-text')).toHaveText(fixture.original);
    await source.getByRole('button', { name: '取消', exact: true }).click();
    expect((await new AxeBuilder({ page }).include('.knowledge-comparison-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('comparison-results.png') });
    if (viewport.width === 1440) {
      writeFileSync(path.join(bridge, 'advance.json'), JSON.stringify({ advance: true }));
      await expect.poll(() => existsSync(path.join(bridge, 'replaced.json')), { timeout: 150_000 }).toBe(true);
      await dialog.getByRole('button', { name: '取消', exact: true }).click();
      await view.getByTestId('search-compare').click(); dialog = page.getByTestId('knowledge-comparison-dialog');
      await dialog.getByTestId('compare-confirm').focus(); await page.keyboard.press('Enter');
      await expect(view.getByTestId('search-changed')).toBeVisible(); await expect(view.getByTestId('search-hit')).toHaveCount(0);
      await expect(page.getByTestId('knowledge-comparison-dialog')).toHaveCount(0); expect(calls).toBe(2);
      await view.getByTestId('search-adopt').click(); await expect(view.getByTestId('search-hit')).toHaveCount(17);
    }
    expect(errors).toEqual([]);
  });
}
