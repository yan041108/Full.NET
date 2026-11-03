import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
const readyPath = process.env.FULLNET_K03_BROWSER_READY;
const fixture = JSON.parse(readFileSync(readyPath, 'utf8'));
process.env.FULLNET_E2E_USERNAME = fixture.username; process.env.FULLNET_E2E_PASSWORD = fixture.password;
const { loginAsHostAdmin } = await import('./support/real-stack-auth.mjs');
const bridge = path.dirname(readyPath);
const container = process.env.FULLNET_K03_BROWSER_CONTAINER;
if (container && !/^[a-f0-9]{12,64}$/.test(container)) throw new Error('The browser bridge requires the owned container ID.');
function advance() {
  const file = path.join(bridge, 'advance.json'); writeFileSync(file, JSON.stringify({ advance: true }));
  // Docker Desktop 的 C 盘绑定故障时，仅传递小型协调文件；不复制仓库或在 Windows 执行后端。
  if (container) execFileSync('docker', ['cp', file, `${container}:/out/temp/k03-browser-1/advance.json`]);
}
function replaced() {
  return container ? spawnSync('docker', ['exec', container, 'test', '-f', '/out/temp/k03-browser-1/replaced.json'], { stdio: 'ignore' }).status === 0
    : existsSync(path.join(bridge, 'replaced.json'));
}

for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`真实已发布知识搜索与来源 ${viewport.width}`, async ({ page }, testInfo) => {
    test.setTimeout(240_000); const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.setViewportSize({ width: 1440, height: 1000 }); await loginAsHostAdmin(page); await page.setViewportSize(viewport);
    await page.goto('/#/ai/knowledge-search'); const view = page.getByTestId('knowledge-search');
    await page.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: fixture.knowledgeName, exact: true }).click();
    await view.getByTestId('search-query').fill('明确 补写');
    await view.getByTestId('search-submit').focus(); await page.keyboard.press('Enter');
    await expect(view.getByTestId('search-hit')).toHaveCount(17, { timeout: 45_000 });
    await expect(view.getByTestId('search-diagnostics')).toHaveCount(0);
    const first = view.getByTestId('search-hit').first(); await expect(first).toContainText('人工原生原文');
    await first.getByTestId('search-preview').click(); const source = page.getByTestId('knowledge-source-dialog');
    await source.getByTestId('knowledge-source-read').click(); await expect(source.getByTestId('knowledge-source-text')).toHaveText(fixture.original);
    await source.getByRole('button', { name: '取消', exact: true }).click();
    if (viewport.width === 1440) {
      // 生产完整生成和独立审核由 Linux 夹具执行；页面持有旧集合，实际发布后必须得到 409。
      advance(); await expect.poll(replaced, { timeout: 150_000 }).toBe(true);
      await view.getByTestId('search-submit').click(); await expect(view.getByTestId('search-changed')).toBeVisible();
      await expect(view.getByTestId('search-hit')).toHaveCount(0);
      await view.getByTestId('search-adopt').click(); await expect(view.getByTestId('search-hit')).toHaveCount(17);
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
    expect((await new AxeBuilder({ page }).include('.knowledge-search').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await view.getByTestId('search-hit').first().scrollIntoViewIfNeeded(); await page.screenshot({ path: testInfo.outputPath('published-search.png') });
    expect(errors).toEqual([]);
  });
}
