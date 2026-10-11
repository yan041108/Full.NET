import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { after, before, test } from 'node:test';
import { waitForApplicationTenantLanding } from './support/application-crud-browser.mjs';

const requireFromE2e = createRequire(new URL('../e2e/admin-real-stack/package.json', import.meta.url));
const { chromium } = requireFromE2e('@playwright/test');
const origin = 'http://localhost:25183';
let browser;
before(async () => { browser = await chromium.launch(); });
after(async () => { await browser?.close(); });

async function fixture(hash, contextName, action) {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    // 拦截应用地址，仅模拟已确认的两阶段状态发布；夹具不登录、不读取数据库。
    await page.route(origin + '/**', route => route.fulfill({ contentType: 'text/html; charset=utf-8',
      body: '<div data-testid="shell-current-context"></div>' }));
    await page.goto(origin + '/#' + hash);
    await page.getByTestId('shell-current-context').evaluate((element, value) => { element.textContent = value; }, contextName);
    await action(page);
  } finally { await context.close(); }
}

async function remainsPending(promise) {
  // 给真实浏览器完成一次 Locator 往返的时间；旧实现立即完成，不能被后续跳转掩盖。
  return Promise.race([promise.then(() => false), new Promise(resolve => setTimeout(() => resolve(true), 500))]);
}

test('tenant browser landing waits for the delayed home redirect after context publication', () =>
  fixture('/tenant-context', 'Local', async page => {
    const landing = waitForApplicationTenantLanding(page, 'Local');
    assert.equal(await remainsPending(landing), true, 'context label alone released product navigation');
    await page.evaluate(() => { location.hash = '/'; });
    await landing;
    assert.equal(page.url(), origin + '/#/');
  }));

test('tenant browser landing requires the requested context even on the home route', () =>
  fixture('/', 'Host', async page => {
    const landing = waitForApplicationTenantLanding(page, 'Local');
    assert.equal(await remainsPending(landing), true, 'home route bypassed context verification');
    await page.getByTestId('shell-current-context').evaluate(element => { element.textContent = 'Local'; });
    await landing;
  }));

test('tenant browser landing rejects an unexpected route without navigating around it', () =>
  fixture('/403', 'Local', async page => {
    await assert.rejects(waitForApplicationTenantLanding(page, 'Local', 300), /toHaveURL/u);
    assert.equal(page.url(), origin + '/#/403');
  }));
