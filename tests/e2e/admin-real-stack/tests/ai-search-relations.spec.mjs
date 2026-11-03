import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
const readyPath = process.env.FULLNET_K03_RELATIONS_BROWSER_READY;
const fixture = JSON.parse(readFileSync(readyPath, 'utf8'));
process.env.FULLNET_E2E_USERNAME = fixture.username; process.env.FULLNET_E2E_PASSWORD = fixture.password;
const { loginAsHostAdmin } = await import('./support/real-stack-auth.mjs');
const bridge = path.dirname(readyPath);
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`真实嵌入父子补全与来源复核 ${viewport.width}`, async ({ page }, testInfo) => {
    test.setTimeout(240_000); const errors = []; page.on('pageerror', error => errors.push(error.message)); let reads = 0; let paid = 0;
    page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/knowledge-search/relations')) ++reads;
      if (request.method() === 'POST' && /\/(ask|compare)$/.test(request.url())) ++paid; });
    await page.setViewportSize({ width: 1440, height: 1000 }); await loginAsHostAdmin(page); await page.setViewportSize(viewport);
    await page.goto('/#/ai/knowledge-search'); const view = page.getByTestId('knowledge-search');
    await page.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: fixture.knowledgeName, exact: true }).click();
    await view.getByTestId('search-query').fill(fixture.query); await view.getByTestId('search-submit').click();
    await expect(view.getByTestId('search-hit')).toHaveCount(1); await expect(view.getByTestId('search-answer')).toHaveCount(0);
    await view.getByTestId('search-relations').focus(); await page.keyboard.press('Enter'); let dialog = page.getByTestId('knowledge-relations-dialog');
    expect(reads).toBe(0); await dialog.getByTestId('relations-load').focus(); await page.keyboard.press('Enter');
    await expect(dialog.getByTestId('relation-block')).toHaveCount(fixture.blocks); await expect(dialog.getByTestId('relation-edge')).toHaveCount(fixture.blocks - 1);
    await expect(dialog.getByTestId('relations-state')).toContainText('父子双方正文'); expect(reads).toBe(1); expect(paid).toBe(0);
    await expect(dialog.getByTestId('relation-block').first()).toContainText(viewport.width === 1440 ? '人工核对后的事实' : '下一份真实关系父正文');
    await dialog.getByTestId('relations-preview').first().click(); const source = page.getByTestId('knowledge-source-dialog');
    // DOCX 沿既有二进制来源下载验收，不把原文件冒充浏览器可直接显示的纯文本。
    await expect(source.getByTestId('knowledge-source-context')).toHaveCount(1);
    const downloaded = page.waitForEvent('download'); await source.getByTestId('knowledge-source-download').click(); const download = await downloaded;
    expect(await download.failure()).toBeNull(); const downloadedPath = await download.path(); expect(downloadedPath).not.toBeNull();
    expect(createHash('sha256').update(readFileSync(downloadedPath)).digest('hex')).toBe(process.env.FULLNET_K03_RELATIONS_SOURCE_SHA256);
    await source.getByRole('button', { name: '取消', exact: true }).click();
    expect((await new AxeBuilder({ page }).include('.knowledge-relations-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('verified-relations.png') });
    if (viewport.width === 1440) {
      writeFileSync(path.join(bridge, 'advance.json'), JSON.stringify({ advance: true }));
      await expect.poll(() => existsSync(path.join(bridge, 'replaced.json')), { timeout: 150_000 }).toBe(true);
      await dialog.getByRole('button', { name: '取消', exact: true }).click(); await view.getByTestId('search-relations').click(); dialog = page.getByTestId('knowledge-relations-dialog');
      await dialog.getByTestId('relations-load').focus(); await page.keyboard.press('Enter');
      await expect(view.getByTestId('search-changed')).toBeVisible(); await expect(view.getByTestId('search-hit')).toHaveCount(0);
      await expect(page.getByTestId('knowledge-relations-dialog')).toHaveCount(0); await view.getByTestId('search-adopt').click(); await expect(view.getByTestId('search-hit')).toHaveCount(1);
      expect(reads).toBe(2);
    }
    expect(paid).toBe(0); expect(errors).toEqual([]);
  });
}
