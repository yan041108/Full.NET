import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync, writeFileSync, renameSync, existsSync } from 'node:fs';
import path from 'node:path';
import { loginAsHostUser } from './support/real-stack-auth.mjs';
const ready = process.env.FULLNET_K06_SOURCE_BROWSER_READY;
if (!ready) throw Error('Missing exact new Calendar source bridge');
const fixture = JSON.parse(readFileSync(ready, 'utf8')); const bridge = path.dirname(ready);
if (fixture.users?.length !== 2) throw Error('Two independent source owners are required');
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus) {
    const file = path.join(bridge, 'complete-failed.writing'); writeFileSync(file, JSON.stringify({ passed: false }));
    renameSync(file, path.join(bridge, 'complete.json'));
  }
});
for (const [index, viewport] of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }].entries())
  test(`本人日程来源、准确版本与撤回清空 ${viewport.width}`, async ({ page }, info) => {
    test.setTimeout(180_000); const user = fixture.users[index]; const errors = []; let modelPosts = 0;
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => { if (request.method() === 'POST' && /\/(ask|execute|responses|embeddings)(\?|$)/.test(request.url())) modelPosts++; });
    const marker = (name, value) => { const file = path.join(bridge, name + '.writing'); writeFileSync(file, JSON.stringify(value)); renameSync(file, path.join(bridge, name)); };
    const both = name => expect.poll(() => [1440, 390].every(width => existsSync(path.join(bridge, name + '-' + width + '.json'))), { timeout: 90_000 }).toBe(true);
    await page.setViewportSize({ width: 1440, height: 1000 }); await loginAsHostUser(page, user.username, fixture.password); await page.setViewportSize(viewport);
    await page.goto('/#/calendar/personal-schedules');
    await expect(page.getByRole('heading', { name: '个人日程', exact: true })).toBeVisible();
    const response = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/personal-schedule-assistant/sources/preview'));
    const open = page.getByTestId('personal-schedules-assistant-source'); await open.focus(); await page.keyboard.press('Enter'); expect((await response).status()).toBe(200);
    const dialog = page.getByRole('dialog', { name: '本人日程来源', exact: true }); await expect(dialog).toBeVisible();
    await expect(dialog.getByTestId('personal-schedule-source')).toContainText(user.content);
    await expect(dialog.locator('script')).toHaveCount(0); await expect(dialog).not.toContainText(fixture.users[1 - index].username);
    await page.evaluate(async () => { await Promise.all(document.getAnimations().filter(value => Number.isFinite(value.effect?.getComputedTiming().endTime)).map(value => value.finished.catch(() => {}))); });
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
    expect(await dialog.evaluate(node => node.scrollWidth <= node.clientWidth + 1)).toBe(true);
    // 两位日期必须在同一行；仅无横向溢出不能识别把日期挤成两行的视觉缺陷。
    expect(await page.locator('.personal-schedules-calendar__day').evaluateAll(nodes => nodes.every(node => {
      const range = document.createRange(); range.selectNodeContents(node); return range.getClientRects().length === 1;
    }))).toBe(true);
    await page.screenshot({ path: info.outputPath('personal-source-visible.png'), fullPage: true });
    marker('shown-' + viewport.width + '.json', { visible: true }); await both('shown');
    if (index === 0) marker('advance.json', { advance: true });
    // 复用页面真实的HTTP客户端和当前会话，不重登同一账号撤销正在验收的会话。
    await page.evaluate(async ({ id, version }) => {
      const api = await import('/src/api/personal-schedules.ts');
      await api.deletePersonalSchedule(id, { version });
    }, { id: user.scheduleId, version: user.version });
    marker('deleted-' + viewport.width + '.json', { deleted: true });
    await expect.poll(() => existsSync(path.join(bridge, 'replaced.json')), { timeout: 90_000 }).toBe(true);
    const denied = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/personal-schedule-assistant/sources/preview'));
    await dialog.getByRole('button', { name: '重新读取', exact: true }).focus(); await page.keyboard.press('Space'); expect((await denied).status()).toBe(409);
    await expect(dialog.getByTestId('personal-schedule-source')).toHaveCount(0); await expect(dialog).toContainText('日程已变化');
    await expect(page.locator('.personal-schedules-day-list__item')).toHaveCount(0);
    await page.evaluate(async () => { await Promise.all(document.getAnimations().filter(value => Number.isFinite(value.effect?.getComputedTiming().endTime)).map(value => value.finished.catch(() => {}))); });
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
    await page.screenshot({ path: info.outputPath('personal-source-cleared.png'), fullPage: true });
    expect(modelPosts).toBe(0); expect(errors).toEqual([]);
    marker('done-' + viewport.width + '.json', { passed: true }); await both('done'); if (index === 0) marker('complete.json', { passed: true });
    await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0);
  });
