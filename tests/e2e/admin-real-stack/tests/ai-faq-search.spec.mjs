import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import path from 'node:path';
const fixture = JSON.parse(readFileSync(process.env.FULLNET_K04_SEARCH_BROWSER_READY, 'utf8'));
const bridge = path.dirname(process.env.FULLNET_K04_SEARCH_BROWSER_READY);
process.env.FULLNET_E2E_USERNAME = fixture.username; process.env.FULLNET_E2E_PASSWORD = fixture.password;
const { loginAsHostAdmin } = await import('./support/real-stack-auth.mjs');
async function searchFaq(page) {
  await page.goto('/#/ai/knowledge-search'); const view = page.getByTestId('knowledge-search');
  await page.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: fixture.knowledgeName, exact: true }).click();
  await view.getByTestId('search-query').fill('标准'); await expect(view.getByTestId('search-submit')).toBeEnabled();
  await view.getByTestId('search-submit').focus(); await expect(view.getByTestId('search-submit')).toBeFocused();
  const searched = page.waitForResponse(value => value.url().endsWith('/knowledge-search') && value.request().method() === 'POST', { timeout: 60_000 });
  await page.keyboard.press('Enter'); expect((await searched).status()).toBe(200);
  let hit = view.getByTestId('search-hit').filter({ hasText: '真实 FAQ 标准问题' }); await expect(hit).toHaveCount(1);
  const response = page.waitForResponse(value => value.url().endsWith('/knowledge-search') && value.request().method() === 'POST');
  await hit.getByTestId('search-only-faq').click(); const result = await (await response).json();
  expect(result.sources).toHaveLength(1); expect(result.sources[0].documentId).toBe(fixture.documentId); expect(result.sources[0].faqId).toBe(fixture.faqId);
  expect(result.sources[0].faqRevisionId).toBe(fixture.revisionId); expect(result.sources[0].sourceKind).toBe('faq-revision-v1');
  hit = view.getByTestId('search-hit').filter({ hasText: '真实 FAQ 标准问题' }); await expect(hit).toHaveCount(1); return { view, hit };
}
async function preview(page, action) {
  await action.click(); const dialog = page.getByTestId('knowledge-source-dialog');
  await expect(dialog.getByTestId('knowledge-source-download')).toHaveCount(0); await expect(dialog.getByTestId('knowledge-source-read')).toHaveCount(0);
  const loaded = page.waitForResponse(value => value.url().endsWith(`/faqs/${fixture.faqId}`) && value.request().method() === 'GET', { timeout: 60_000 });
  await expect(dialog.getByTestId('knowledge-faq-source-read')).toBeEnabled(); await dialog.getByTestId('knowledge-faq-source-read').focus();
  await page.keyboard.press('Enter'); expect((await loaded).status()).toBe(200);
  await expect(dialog.getByTestId('knowledge-faq-source-body')).toContainText('数据库修订的正式答案。');
  await expect(dialog.getByTestId('knowledge-faq-source-body')).toContainText('文档派生'); return dialog;
}
async function ask(page, label) {
  const listed = page.waitForResponse(value => /\/chat\/sessions\/?(?:\?.*)?$/.test(value.url()) && value.request().method() === 'GET', { timeout: 60_000 });
  await page.getByTestId('search-answer').click(); const dialog = page.getByTestId('search-answer-dialog'); expect((await listed).status()).toBe(200);
  await dialog.getByTestId('answer-session').click();
  // 页面展示批准配置的名称，技术模型 ID 不等于配置名称；实际请求仍须命中夹具创建的准确会话。
  const option = page.getByRole('option', { name: new RegExp(`^FAQ 来源${label} · `) }); await expect(option).toHaveCount(1); await option.click();
  const request = page.waitForRequest(value => value.url().endsWith('/knowledge/ask') && value.method() === 'POST');
  const responded = page.waitForResponse(value => value.url().endsWith('/knowledge/ask') && value.request().method() === 'POST', { timeout: 120_000 });
  await expect(dialog.getByTestId('answer-confirm')).toBeEnabled(); await dialog.getByTestId('answer-confirm').focus(); await page.keyboard.press('Enter');
  const sent = await request; const body = sent.postDataJSON();
  expect(new URL(sent.url()).pathname).toBe(`/api/v1/ai/chat/sessions/${fixture.sessions[['桌面', '移动', '移动历史'].indexOf(label)]}/knowledge/ask`);
  expect(body.searchScope.sources).toHaveLength(1); expect(body.searchScope.sources[0].sourceKind).toBe('faq-revision-v1');
  expect(body.searchScope.documentIds).toEqual([fixture.documentId]); expect((await responded).status()).toBe(200);
  await expect(dialog.getByTestId('search-answer-text')).toContainText('原生完整生成验证');
  return dialog;
}
for (const [index, viewport] of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }].entries()) {
  test(`FAQ 冻结搜索、策略、专属预览与历史 ${viewport.width}`, async ({ page }, testInfo) => {
    test.setTimeout(300_000); const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.setViewportSize({ width: 1440, height: 1000 }); await loginAsHostAdmin(page); await page.setViewportSize(viewport);
    const { view, hit } = await searchFaq(page); let source = await preview(page, hit.getByTestId('search-preview'));
    expect((await new AxeBuilder({ page }).include('[data-testid="knowledge-source-dialog"]').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('faq-source.png') }); await source.getByRole('button', { name: '取消', exact: true }).click();
    if (index === 0) {
      await view.getByTestId('search-compare').click(); const compare = page.getByTestId('knowledge-comparison-dialog');
      const compared = page.waitForResponse(value => value.url().endsWith('/knowledge-search/compare') && value.request().method() === 'POST', { timeout: 120_000 });
      await compare.getByTestId('compare-confirm').click(); expect((await compared).status()).toBe(200);
      await expect(compare.getByTestId('comparison-vector').getByTestId('comparison-hit')).toHaveCount(1);
      source = await preview(page, compare.getByTestId('comparison-preview').first()); await source.getByRole('button', { name: '取消', exact: true }).click();
      await compare.getByRole('button', { name: '取消', exact: true }).click();
    } else {
      await view.getByTestId('search-refine').click(); const refine = page.getByTestId('knowledge-refinement-dialog');
      const refined = page.waitForResponse(value => value.url().endsWith('/knowledge-search/refine') && value.request().method() === 'POST', { timeout: 120_000 });
      await refine.getByTestId('refine-confirm').click(); expect((await refined).status()).toBe(200); await expect(refine.getByTestId('refine-operation')).toHaveCount(4);
      source = await preview(page, refine.getByTestId('refine-preview').first()); await source.getByRole('button', { name: '取消', exact: true }).click();
      await refine.getByRole('button', { name: '取消', exact: true }).click();
    }
    const answer = await ask(page, index === 0 ? '桌面' : '移动'); source = await preview(page, answer.getByTestId('answer-preview').first());
    await source.getByRole('button', { name: '取消', exact: true }).click();
    const historyRead = page.waitForResponse(value => /\/chat\/sessions\/[0-9a-f-]+$/.test(value.url()) && value.request().method() === 'GET', { timeout: 60_000 });
    await answer.getByTestId('answer-history').click(); expect((await historyRead).status()).toBe(200);
    await expect(answer.getByTestId('search-answer-text')).toContainText('原生完整生成验证');
    expect((await new AxeBuilder({ page }).include('[data-testid="search-answer-dialog"]').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: testInfo.outputPath('faq-answer.png') });
    if (index === 1) {
      // 答案与其来源叠层同时持有旧正文；来源失效必须连同答案、引用和搜索缓冲一并清空。
      source = await preview(page, answer.getByTestId('answer-preview').first());
      await expect(answer.getByTestId('search-answer-text')).toContainText('原生完整生成验证');
      writeFileSync(path.join(bridge, 'advance.json'), JSON.stringify({ advance: true }));
      await expect.poll(() => existsSync(path.join(bridge, 'replaced.json')), { timeout: 90_000 }).toBe(true);
      const denied = page.waitForResponse(value => value.url().endsWith(`/faqs/${fixture.faqId}`) && value.request().method() === 'GET', { timeout: 60_000 });
      await source.getByTestId('knowledge-faq-source-read').click(); expect((await denied).status()).toBe(409);
      await expect(page.getByTestId('knowledge-faq-source-body')).toHaveCount(0); await expect(page.getByTestId('search-answer-text')).toHaveCount(0);
      await expect(view.getByTestId('search-hit')).toHaveCount(0);
      // 同一交互会话进入正式聊天页，再由服务器重新读取持久历史，不能只靠本地清空通过。
      const listed = page.waitForResponse(value => /\/chat\/sessions\/?(?:\?.*)?$/.test(value.url()) && value.request().method() === 'GET', { timeout: 60_000 });
      await page.goto('/#/ai/chat'); expect((await listed).status()).toBe(200);
      const sessionButton = page.locator('.ai-chat-session-item').filter({ has: page.getByText('FAQ 来源移动', { exact: true }) });
      await expect(sessionButton).toBeEnabled();
      const redacted = page.waitForResponse(value => value.url().endsWith(`/chat/sessions/${fixture.sessions[1]}`) && value.request().method() === 'GET', { timeout: 60_000 });
      await sessionButton.click(); const loaded = await redacted; expect(loaded.status()).toBe(200);
      const message = (await loaded.json()).messages.find(value => value.roleKey === 'assistant');
      expect(message.content).toBe(''); expect(message.knowledgeUnavailableCode).toBeTruthy(); expect(message.citations).toHaveLength(0);
      await expect(page.getByTestId('knowledge-answer-unavailable')).toBeVisible(); await expect(page.getByTestId('knowledge-citation')).toHaveCount(0);
      await expect(page.getByText('原生完整生成验证', { exact: true })).toHaveCount(0);
    } else await answer.getByRole('button', { name: '取消', exact: true }).click();
    expect(errors).toEqual([]);
  });
}
