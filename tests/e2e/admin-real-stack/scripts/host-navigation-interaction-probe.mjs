/**
 * Host 菜单交互探针：查询 + 可选新增提交。
 * 用法：
 *   node scripts/host-navigation-interaction-probe.mjs
 *   node scripts/host-navigation-interaction-probe.mjs --only-failed
 *   node scripts/host-navigation-interaction-probe.mjs --paths /identity/users,/tenants
 */
import { chromium } from '@playwright/test';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { flattenNavigation, uniqueByPath } from './support/navigation-manifest.mjs';
import {
  attachProbeListeners,
  createProbeContext,
  dismissBlockingOverlays,
  hasErrorMessage,
  runCreateProbe,
  runListProbe
} from './support/navigation-probe-default.mjs';
import { resolvePlugin } from './support/navigation-probe-plugins.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const adminBaseUrl = process.env.FULLNET_E2E_ADMIN_URL ?? 'http://localhost:5173';
const origin = process.env.FULLNET_E2E_ADMIN_ORIGIN ?? 'http://localhost:5173';
const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const outDir = join(__dirname, '../.artifacts');
const screenshotDir = join(outDir, 'probe-screenshots');
const resultsPath = join(outDir, 'host-navigation-interaction-results.json');

function parseArgs(argv) {
  const onlyFailed = argv.includes('--only-failed');
  const pathsArg = argv.find(arg => arg.startsWith('--paths='));
  const paths = pathsArg
    ? pathsArg.slice('--paths='.length).split(',').map(p => p.trim()).filter(Boolean)
    : null;
  return { onlyFailed, paths };
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
    headers: { Authorization: `Bearer ${accessToken}`, Origin: origin }
  });
  if (!response.ok()) {
    throw new Error(`navigation failed: ${response.status()}`);
  }
  return response.json();
}

async function loginUi(page) {
  await page.context().clearCookies();
  await page.addInitScript(() => {
    localStorage.clear();
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
  await page.goto(`${adminBaseUrl}/#/login`);
  await page.getByLabel('账号', { exact: true }).fill(username);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '进入控制台' }).click();
  await page.getByRole('navigation', { name: '主导航' }).waitFor({
    state: 'visible',
    timeout: 30_000
  });
}

function slugPath(path) {
  return path.replace(/^\//, '').replace(/\//g, '_') || 'root';
}

async function probePage(page, item, ctx) {
  const detach = attachProbeListeners(page, ctx);
  const screenshotName = `${slugPath(item.path)}.png`;
  try {
    await dismissBlockingOverlays(page);
    await page.goto(`${adminBaseUrl}/#${item.path}`, {
      waitUntil: 'domcontentloaded',
      timeout: 30_000
    });
    await page.waitForTimeout(1_200);
    const hash = await page.evaluate(() => window.location.hash);
    if (hash.includes('/403') || hash.includes('/404') || hash.includes('/500')) {
      return { status: 'fail', step: 'status_page', hash, screenshot: screenshotName };
    }

    const plugin = resolvePlugin(item.componentKey);
    if (plugin) {
      const pluginResult = await plugin(page, ctx);
      if (ctx.apiFailures.length > 0) {
        return { status: 'fail', step: 'api_5xx', apiFailures: [...ctx.apiFailures], screenshot: screenshotName };
      }
      return { ...pluginResult, hash, screenshot: null };
    }

    await runListProbe(page);
    if (ctx.apiFailures.length > 0) {
      return { status: 'fail', step: 'api_5xx_after_list', apiFailures: [...ctx.apiFailures], screenshot: screenshotName };
    }

    const createResult = await runCreateProbe(page, ctx.stamp);
    if (createResult.mode === 'readOnly') {
      return { status: 'readOnly', step: createResult.step, hash };
    }
    if (createResult.mode === 'fail') {
      return { status: 'fail', step: createResult.step, detail: createResult.detail, screenshot: screenshotName };
    }
    if (createResult.mode === 'needs_scenario') {
      if (ctx.apiFailures.length > 0) {
        return { status: 'fail', step: 'api_5xx_after_create', apiFailures: [...ctx.apiFailures], screenshot: screenshotName };
      }
      return { status: 'needs_scenario', step: createResult.step, hash };
    }
    if (await hasErrorMessage(page)) {
      return { status: 'fail', step: 'error_toast_after_create', screenshot: screenshotName };
    }
    if (ctx.apiFailures.length > 0) {
      return { status: 'fail', step: 'api_5xx_after_create', apiFailures: [...ctx.apiFailures], screenshot: screenshotName };
    }
    return { status: 'pass', step: createResult.step, hash };
  } catch (error) {
    return {
      status: 'fail',
      step: 'exception',
      error: error instanceof Error ? error.message : String(error),
      screenshot: screenshotName
    };
  } finally {
    detach();
  }
}

async function main() {
  const { onlyFailed, paths: pathFilter } = parseArgs(process.argv.slice(2));
  mkdirSync(outDir, { recursive: true });
  mkdirSync(screenshotDir, { recursive: true });

  let manifest;
  if (onlyFailed && pathFilter === null) {
    const prior = JSON.parse(readFileSync(resultsPath, 'utf8'));
    const failedPaths = new Set(
      prior.results.filter(r => r.status === 'fail').map(r => r.path)
    );
    manifest = prior.results.filter(r => failedPaths.has(r.path));
  } else {
    const browserForNav = await chromium.launch({
      channel: process.env.PLAYWRIGHT_CHROMIUM_CHANNEL ?? 'msedge',
      headless: true
    });
    const navContext = await browserForNav.newContext();
    const token = await loginAccessToken(navContext.request);
    const tree = await fetchNavigation(navContext.request, token);
    manifest = uniqueByPath(flattenNavigation(tree));
    await browserForNav.close();
    if (pathFilter?.length) {
      const allowed = new Set(pathFilter);
      manifest = manifest.filter(item => allowed.has(item.path));
    }
  }

  const browser = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHROMIUM_CHANNEL ?? 'msedge',
    headless: true
  });
  const context = await browser.newContext();
  const page = await context.newPage();
  await loginUi(page);

  const results = [];
  for (let index = 0; index < manifest.length; index += 1) {
    const item = manifest[index];
    const ctx = createProbeContext();
    process.stdout.write(`[${index + 1}/${manifest.length}] ${item.path} ... `);
    const probe = await probePage(page, item, ctx);
    const record = { ...item, ...probe };
    if (probe.screenshot && probe.status === 'fail') {
      await page.screenshot({
        path: join(screenshotDir, probe.screenshot),
        fullPage: true
      }).catch(() => {});
    }
    results.push(record);
    console.log(record.status);
  }
  await browser.close();

  const summary = {
    probedAt: new Date().toISOString(),
    totals: {
      pass: results.filter(r => r.status === 'pass').length,
      readOnly: results.filter(r => r.status === 'readOnly').length,
      needs_scenario: results.filter(r => r.status === 'needs_scenario').length,
      fail: results.filter(r => r.status === 'fail').length,
      skip: results.filter(r => r.status === 'skip').length
    },
    results
  };
  writeFileSync(resultsPath, JSON.stringify(summary, null, 2), 'utf8');
  writeFileSync(
    join(outDir, 'host-navigation-interaction-report.md'),
    buildReport(summary),
    'utf8'
  );

  console.log('\nSummary:', JSON.stringify(summary.totals));
  if (summary.totals.fail > 0) {
    for (const f of results.filter(r => r.status === 'fail')) {
      console.log(`- ${f.path} (${f.title}): ${f.step} ${f.detail ?? f.error ?? ''}`);
    }
    process.exitCode = 1;
  }
}

function buildReport(summary) {
  const lines = [
    '# Host 菜单交互探针报告',
    '',
    `时间: ${summary.probedAt}`,
    '',
    '| 状态 | 数量 |',
    '|------|------|',
    `| pass | ${summary.totals.pass} |`,
    `| readOnly | ${summary.totals.readOnly} |`,
    `| needs_scenario | ${summary.totals.needs_scenario} |`,
    `| fail | ${summary.totals.fail} |`,
    '',
    '## 失败项',
    ''
  ];
  for (const row of summary.results.filter(r => r.status === 'fail')) {
    lines.push(`- \`${row.path}\` (${row.title}) — ${row.step}`);
  }
  lines.push('', '## needs_scenario', '');
  for (const row of summary.results.filter(r => r.status === 'needs_scenario')) {
    lines.push(`- \`${row.path}\` (${row.componentKey}) — ${row.step}`);
  }
  return lines.join('\n');
}

await main();
