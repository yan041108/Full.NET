/**
 * Host 导航全量冒烟：登录后按 /api/v1/navigation 扁平化访问各 hash 路径，采集 UI/控制台/API 失败。
 * 用法：node scripts/host-navigation-crawl.mjs
 */
import { chromium } from '@playwright/test';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { adminOrigin, loginAsHostAdmin } from '../tests/support/real-stack-auth.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const adminBaseUrl =
  process.env.FULLNET_E2E_ADMIN_URL ?? process.env.FULLNET_E2E_ADMIN_ORIGIN ?? adminOrigin('vue');
const origin =
  process.env.FULLNET_E2E_ADMIN_ORIGIN ?? process.env.FULLNET_E2E_ADMIN_URL ?? adminOrigin('vue');
const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';

function flattenNavigation(nodes, output = []) {
  for (const node of nodes ?? []) {
    const isLayout =
      node.componentKey === 'layout'
      || (node.path?.startsWith('/domains/') ?? false)
      || (node.path?.startsWith('/modules/') ?? false);
    const isMenu =
      node.menuType === 'Menu'
      || (!isLayout && node.menuType !== 'Button' && node.menuType !== 'Directory');
    if (isMenu && node.path && !node.path.startsWith('__group__')) {
      output.push({
        path: node.path,
        title: node.title,
        routeName: node.routeName,
        componentKey: node.componentKey,
        menuType: node.menuType
      });
    }
    if (node.children?.length) {
      flattenNavigation(node.children, output);
    }
  }
  return output;
}

async function loginAccessToken(request) {
  const response = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    headers: { Origin: origin },
    data: { username, password }
  });
  if (!response.ok()) {
    throw new Error(`login failed: ${response.status()} ${await response.text()}`);
  }
  const body = await response.json();
  return body.accessToken;
}

async function fetchNavigation(request, accessToken) {
  const response = await request.get(`${apiBaseUrl}/api/v1/navigation`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin
    }
  });
  if (!response.ok()) {
    throw new Error(`navigation failed: ${response.status()}`);
  }
  return response.json();
}

async function loginUi(page) {
  await loginAsHostAdmin(page, `${adminBaseUrl}/`);
}

function uniqueByPath(items) {
  const seen = new Set();
  return items.filter(item => {
    if (seen.has(item.path)) {
      return false;
    }
    seen.add(item.path);
    return true;
  });
}

async function crawlPath(page, item) {
  const consoleErrors = [];
  const apiFailures = [];
  const onConsole = msg => {
    if (msg.type() === 'error') {
      consoleErrors.push(msg.text().slice(0, 500));
    }
  };
  const onResponse = response => {
    const url = response.url();
    if (!url.includes('/api/')) {
      return;
    }
    const status = response.status();
    if (status >= 500) {
      apiFailures.push({ url, status });
    }
  };
  page.on('console', onConsole);
  page.on('response', onResponse);
  try {
    await page.goto(`${adminBaseUrl}/#${item.path}`, {
      waitUntil: 'domcontentloaded',
      timeout: 30_000
    });
    await page.waitForTimeout(1200);
    const hash = await page.evaluate(() => window.location.hash);
    const statusPage =
      hash.includes('/403')
      || hash.includes('/404')
      || hash.includes('/500');
    const apiFailuresFiltered = apiFailures.filter(
      entry => !entry.url.includes('/auth/refresh')
    );
    return {
      ...item,
      hash,
      statusPage,
      consoleErrors: [...new Set(consoleErrors)],
      apiFailures: apiFailuresFiltered
    };
  } finally {
    page.off('console', onConsole);
    page.off('response', onResponse);
  }
}

async function main() {
  const browser = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHROMIUM_CHANNEL ?? 'msedge',
    headless: true
  });
  const context = await browser.newContext();
  const request = context.request;
  const accessToken = await loginAccessToken(request);
  const navigationTree = await fetchNavigation(request, accessToken);
  const manifest = uniqueByPath(flattenNavigation(navigationTree));
  const outDir = join(__dirname, '../.artifacts');
  mkdirSync(outDir, { recursive: true });
  writeFileSync(
    join(outDir, 'host-navigation-manifest.json'),
    JSON.stringify(manifest, null, 2),
    'utf8'
  );
  console.log(`Manifest: ${manifest.length} menu paths`);

  const page = await context.newPage();
  await loginUi(page);
  const results = [];
  for (let index = 0; index < manifest.length; index += 1) {
    const item = manifest[index];
    process.stdout.write(`[${index + 1}/${manifest.length}] ${item.path} ... `);
    try {
      const result = await crawlPath(page, item);
      const failed =
        result.statusPage
        || result.apiFailures.length > 0;
      results.push({ ...result, failed });
      console.log(failed ? 'FAIL' : 'ok');
    } catch (error) {
      results.push({
        ...item,
        failed: true,
        error: error instanceof Error ? error.message : String(error)
      });
      console.log('ERROR');
    }
  }
  await browser.close();

  const failures = results.filter(r => r.failed);
  writeFileSync(
    join(outDir, 'host-navigation-crawl-results.json'),
    JSON.stringify({ crawledAt: new Date().toISOString(), results, failures }, null, 2),
    'utf8'
  );
  console.log(`\nDone: ${results.length - failures.length} passed, ${failures.length} failed`);
  if (failures.length > 0) {
    for (const f of failures) {
      console.log(`- ${f.path} (${f.title})`);
      if (f.statusPage) console.log('    status page');
      if (f.apiFailures?.length) console.log(`    api: ${JSON.stringify(f.apiFailures)}`);
      if (f.consoleErrors?.length) console.log(`    console: ${f.consoleErrors[0]}`);
      if (f.error) console.log(`    error: ${f.error}`);
    }
    process.exitCode = 1;
  }
}

await main();
