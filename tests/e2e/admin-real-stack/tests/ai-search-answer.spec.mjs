import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import path from 'node:path';
const readyPath = process.env.FULLNET_K03_ANSWER_BROWSER_READY;
const fixture = JSON.parse(readFileSync(readyPath, 'utf8'));
process.env.FULLNET_E2E_USERNAME = fixture.username; process.env.FULLNET_E2E_PASSWORD = fixture.password;
const { loginAsHostAdmin } = await import('./support/real-stack-auth.mjs');
const bridge = path.dirname(readyPath);
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`冻结搜索收费问答与历史 ${viewport.width}`, async ({ page }, testInfo) => {
    test.setTimeout(240_000); page.setDefaultTimeout(30_000); const errors = []; page.on('pageerror', error => errors.push(error.message));
    let calls = 0; page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/knowledge/ask')) ++calls; });
    await page.setViewportSize({ width: 1440, height: 1000 }); await loginAsHostAdmin(page); await page.setViewportSize(viewport);
    await page.goto('/#/ai/knowledge-search'); const view = page.getByTestId('knowledge-search');
    await page.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: fixture.knowledgeName, exact: true }).click();
    await view.getByTestId('search-query').fill('明确 补写'); await view.getByTestId('search-submit').click();
    await expect(view.getByTestId('search-hit')).toHaveCount(17);
    await view.getByTestId('search-answer').focus(); await page.keyboard.press('Enter'); const dialog = page.getByTestId('search-answer-dialog');
    await dialog.getByTestId('answer-session').click(); await page.getByRole('option', { name: '冻结搜索问答页面 · gpt-4.1-mini', exact: true }).click();
    expect(calls).toBe(0); await dialog.getByTestId('answer-confirm').focus(); await page.keyboard.press('Enter');
    await expect(dialog.getByTestId('search-answer-text')).toHaveText('原生完整生成验证'); expect(calls).toBe(1);
    await expect(dialog.getByTestId('answer-confirm')).toBeDisabled();
    await dialog.getByTestId('answer-preview').click(); const source = page.getByTestId('knowledge-source-dialog');
    await source.getByTestId('knowledge-source-read').click(); await expect(source.getByTestId('knowledge-source-text')).toHaveText(fixture.original);
    await source.getByRole('button', { name: '取消', exact: true }).click();
    await dialog.getByTestId('answer-history').click(); await expect(dialog.getByTestId('search-answer-text')).toHaveText('原生完整生成验证'); expect(calls).toBe(1);
    expect((await new AxeBuilder({ page }).include('.knowledge-search-answer-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    if (viewport.width === 1440) {
      writeFileSync(path.join(bridge, 'advance.json'), JSON.stringify({ advance: true }));
      await expect.poll(() => existsSync(path.join(bridge, 'replaced.json')), { timeout: 150_000 }).toBe(true);
      await dialog.getByTestId('answer-history').focus(); await page.keyboard.press('Enter');
      await expect(view.getByTestId('search-error')).toBeVisible(); await expect(view.getByTestId('search-hit')).toHaveCount(0);
      await expect(page.getByTestId('search-answer-dialog')).toHaveCount(0); expect(calls).toBe(1);
    }
    expect((await new AxeBuilder({ page }).include(viewport.width === 1440 ? '.knowledge-search' : '.knowledge-search-answer-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('search-answer.png') }); expect(errors).toEqual([]);
  });
}
