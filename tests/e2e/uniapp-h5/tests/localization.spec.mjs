import { expect, test } from '@playwright/test';
import { readdir, readFile, stat } from 'node:fs/promises';
import { resolve } from 'node:path';

const bridgeName = '__FULLNET_UNIAPP_E2E__';
const fixtureMarker = 'fullnet-uniapp-e2e-fixture';

async function waitForBridge(page) {
  await page.waitForFunction(name => typeof globalThis[name]?.hydrateAuthenticated === 'function', bridgeName);
}

async function chooseLocale(page, label) {
  await page.getByText(label, { exact: true }).click();
}

async function submitLocale(page, label) {
  await page.getByRole('button', { name: label }).click();
}

async function hydrateAuthenticated(page, snapshot) {
  await page.evaluate(({ name, value }) => globalThis[name].hydrateAuthenticated(value), {
    name: bridgeName,
    value: snapshot
  });
}

async function requestThroughApplication(page, path) {
  return page.evaluate(({ name, value }) => globalThis[name].request({ path: value }), {
    name: bridgeName,
    value: path
  });
}

async function collectFiles(directory) {
  const entries = await readdir(directory);
  const files = [];
  for (const entry of entries) {
    const path = resolve(directory, entry);
    if ((await stat(path)).isDirectory()) {
      files.push(...await collectFiles(path));
    } else {
      files.push(path);
    }
  }
  return files;
}

test('starts in Chinese and keeps an anonymous English selection across refresh', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByText('选择界面语言', { exact: true })).toBeVisible();
  await expect(page.getByText('当前为匿名模式。语言选择只保存在此设备，不会创建账号或会话。', { exact: true })).toBeVisible();

  await chooseLocale(page, 'English (US)');
  const applyButton = page.getByRole('button', { name: '应用到此设备' });
  await applyButton.focus();
  await applyButton.press('Enter');

  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US');
  await expect(page).toHaveTitle('Language settings');
  await expect(page.getByText('Choose your interface language', { exact: true })).toBeVisible();
  await expect(page.getByText('You are browsing anonymously. This choice stays on this device and creates no account or session.', { exact: true })).toBeVisible();

  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US');
  await expect(page).toHaveTitle('Language settings');
  await expect(page.getByText('Choose your interface language', { exact: true })).toBeVisible();

  await waitForBridge(page);
  const requestPromise = page.waitForRequest(request => request.url().endsWith('/api/e2e/locale-probe'));
  await page.route('**/api/e2e/locale-probe', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ ok: true })
  }));
  await requestThroughApplication(page, '/api/e2e/locale-probe');
  const request = await requestPromise;
  expect(await request.headerValue('accept-language')).toBe('en-US');
});

test('commits an authenticated locale only after the server confirms the PUT', async ({ page }) => {
  await page.goto('/');
  await waitForBridge(page);
  await hydrateAuthenticated(page, { preferredLocale: 'zh-CN', profileVersion: 5 });
  await expect(page.getByText('账号偏好已连接。保存成功后才会更新当前语言和资料版本。', { exact: true })).toBeVisible();
  await expect(page.getByText('资料版本 5', { exact: true })).toBeVisible();

  let acceptLanguage;
  await page.route('**/api/v1/me/locale', async route => {
    acceptLanguage = route.request().headerValue('accept-language');
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ preferredLocale: 'en-US', profileVersion: 6 })
    });
  });
  await chooseLocale(page, 'English (US)');
  await submitLocale(page, '保存账号偏好');

  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US');
  await expect(page.getByText('Profile version 6', { exact: true })).toBeVisible();
  await expect(page.getByText('Saved successfully', { exact: true })).toBeVisible();
  expect(await acceptLanguage).toBe('zh-CN');
});

test('keeps authenticated state and shows the localized conflict after a 409', async ({ page }) => {
  await page.goto('/');
  await waitForBridge(page);
  await hydrateAuthenticated(page, { preferredLocale: 'zh-CN', profileVersion: 5 });
  await page.route('**/api/v1/me/locale', route => route.fulfill({
    status: 409,
    contentType: 'application/problem+json',
    body: JSON.stringify({
      code: 'identity.profile_version_conflict',
      title: 'Profile changed.',
      traceId: 'trace-conflict-e2e'
    })
  }));

  await chooseLocale(page, 'English (US)');
  await submitLocale(page, '保存账号偏好');

  await expect(page.locator('html')).toHaveAttribute('lang', 'zh-CN');
  await expect(page.getByText('账号偏好已连接。保存成功后才会更新当前语言和资料版本。', { exact: true })).toBeVisible();
  await expect(page.getByText('资料版本 5', { exact: true })).toBeVisible();
  await expect(page.getByText('资料已更新，请刷新后重试', { exact: true })).toBeVisible();
  await expect(page.getByText('跟踪 ID: trace-conflict-e2e', { exact: true })).toBeVisible();
});

test('uses a safe server title and trace id for an unknown 409 code', async ({ page }) => {
  await page.goto('/');
  await waitForBridge(page);
  await hydrateAuthenticated(page, { preferredLocale: 'zh-CN', profileVersion: 9 });
  await page.route('**/api/v1/me/locale', route => route.fulfill({
    status: 409,
    contentType: 'application/problem+json',
    body: JSON.stringify({
      code: 'identity.future_conflict',
      title: 'Preference could not be saved.',
      traceId: 'trace-unknown-e2e'
    })
  }));

  await chooseLocale(page, 'English (US)');
  await submitLocale(page, '保存账号偏好');

  await expect(page.locator('html')).toHaveAttribute('lang', 'zh-CN');
  await expect(page.getByText('资料版本 9', { exact: true })).toBeVisible();
  await expect(page.getByText('Preference could not be saved.', { exact: true })).toBeVisible();
  await expect(page.getByText('跟踪 ID: trace-unknown-e2e', { exact: true })).toBeVisible();
});

test('does not ship the development bridge in the production H5 artifact', async () => {
  const outputDirectory = resolve(import.meta.dirname, '../../../../clients/uniapp/dist/build/h5');
  const files = await collectFiles(outputDirectory);
  expect(files.length).toBeGreaterThan(0);

  for (const path of files.filter(file => /\.(?:html|js|css|json)$/u.test(file))) {
    const content = await readFile(path, 'utf8');
    expect(content).not.toContain(bridgeName);
    expect(content).not.toContain(fixtureMarker);
  }
});
