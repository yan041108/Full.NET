/**
 * Host 管理端专业功能审计：P0/P1 检查点 + JSON/Markdown 报告。
 * 用法：
 *   node scripts/host-admin-qa-audit.mjs
 *   node scripts/host-admin-qa-audit.mjs --only-failed
 *   node scripts/host-admin-qa-audit.mjs --paths /identity/users
 *   node scripts/host-admin-qa-audit.mjs --skip-l3
 */
import { chromium } from '@playwright/test';
import { execSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  flattenNavigationWithDomain,
  uniqueByPath
} from './support/navigation-manifest.mjs';
import {
  attachProbeListeners,
  createProbeContext,
  dismissBlockingOverlays,
  hasErrorMessage,
  runCreateProbe,
  runListProbe
} from './support/navigation-probe-default.mjs';
import { resolvePlugin } from './support/navigation-probe-plugins.mjs';
import { runP1Export, runP1RowActions } from './support/qa-audit-p1.mjs';
import { runL3FullSuite, writeL3Artifact } from './support/qa-l3-runner.mjs';
import { buildQaReportMarkdown } from './support/qa-report-builder.mjs';
import { adminOrigin, loginAsHostAdmin } from '../tests/support/real-stack-auth.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const rootDir = join(__dirname, '..');
const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const adminBaseUrl =
  process.env.FULLNET_E2E_ADMIN_URL ?? process.env.FULLNET_E2E_ADMIN_ORIGIN ?? adminOrigin('vue');
const origin =
  process.env.FULLNET_E2E_ADMIN_ORIGIN ?? process.env.FULLNET_E2E_ADMIN_URL ?? adminOrigin('vue');
const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const outDir = join(rootDir, '.artifacts');
const screenshotDir = join(outDir, 'qa-screenshots');
const auditJsonPath = join(outDir, 'host-admin-qa-audit.json');
const reportMdPath = join(outDir, 'host-admin-qa-report.md');
const priorAuditPath = auditJsonPath;

function parseArgs(argv) {
  return {
    onlyFailed: argv.includes('--only-failed'),
    skipL3: argv.includes('--skip-l3'),
    skipCrawl: argv.includes('--skip-crawl'),
    paths: (() => {
      const arg = argv.find(a => a.startsWith('--paths='));
      if (!arg) {
        return null;
      }
      return arg.slice('--paths='.length).split(',').map(p => p.trim()).filter(Boolean);
    })()
  };
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

async function fetchAdminUserId(request, accessToken) {
  const response = await request.get(`${apiBaseUrl}/api/v1/identity/users?page=1&pageSize=50`, {
    headers: { Authorization: `Bearer ${accessToken}`, Origin: origin }
  });
  if (!response.ok()) {
    return null;
  }
  const body = await response.json();
  const user = body.items?.find(entry => entry.username === username);
  return user?.id ?? null;
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
  await loginAsHostAdmin(page, `${adminBaseUrl}/`);
}

function slugPath(path) {
  return path.replace(/^\//, '').replace(/\//g, '_') || 'root';
}

function deriveP0Status(p0) {
  if (p0.reachability === 'fail' || p0.api5xx === 'fail' || p0.query === 'fail' || p0.create === 'fail') {
    return 'fail';
  }
  if (p0.create === 'needs_scenario') {
    return 'needs_scenario';
  }
  if (p0.create === 'readOnly' || p0.create === 'na') {
    return 'readOnly';
  }
  return 'pass';
}

async function auditPage(page, item, ctx) {
  const detach = attachProbeListeners(page, ctx);
  const screenshotName = `${slugPath(item.path)}.png`;
  const p0 = {
    reachability: 'pass',
    api5xx: 'pass',
    query: 'na',
    create: 'na',
    overall: 'pass',
    step: null,
    detail: null
  };
  const p1 = { row: null, export: null };

  try {
    await dismissBlockingOverlays(page);
    await page.goto(`${adminBaseUrl}/#${item.path}`, {
      waitUntil: 'domcontentloaded',
      timeout: 30_000
    });
    await page.waitForTimeout(1_200);

    const hash = await page.evaluate(() => window.location.hash);
    if (hash.includes('/403') || hash.includes('/404') || hash.includes('/500')) {
      p0.reachability = 'fail';
      p0.overall = 'fail';
      p0.step = 'status_page';
      return {
        status: 'fail',
        severity: 'Critical',
        step: 'status_page',
        hash,
        p0,
        p1,
        evidence: { screenshot: screenshotName }
      };
    }

    const shell = page.locator(
      '.art-page-stack, .art-crud-page, .overview, [data-route-heading], [class$="-view"]'
    ).first();
    if (!(await shell.isVisible().catch(() => false))) {
      p0.reachability = 'fail';
      p0.overall = 'fail';
      p0.step = 'shell_missing';
      return {
        status: 'fail',
        severity: 'Critical',
        step: 'shell_missing',
        hash,
        p0,
        p1,
        evidence: { screenshot: screenshotName }
      };
    }

    if (ctx.apiFailures.length > 0) {
      p0.api5xx = 'fail';
      p0.overall = 'fail';
      p0.step = 'api_5xx_on_load';
      return {
        status: 'fail',
        severity: 'Critical',
        step: 'api_5xx_on_load',
        apiFailures: [...ctx.apiFailures],
        hash,
        p0,
        p1,
        evidence: { screenshot: screenshotName }
      };
    }

    const plugin = resolvePlugin(item.componentKey);
    if (plugin) {
      const pluginResult = await plugin(page, ctx);
      p0.query = 'pass';
      if (ctx.apiFailures.length > 0) {
        p0.api5xx = 'fail';
        p0.overall = 'fail';
        return {
          ...pluginResult,
          status: 'fail',
          severity: 'Critical',
          hash,
          p0: { ...p0, overall: 'fail', step: 'api_5xx_after_plugin' },
          p1,
          apiFailures: [...ctx.apiFailures],
          evidence: { screenshot: screenshotName }
        };
      }
      const st = pluginResult.status;
      if (st === 'readOnly') {
        p0.create = 'readOnly';
        p0.overall = 'pass';
      } else if (st === 'needs_scenario') {
        p0.create = 'needs_scenario';
        p0.overall = 'needs_scenario';
        p0.step = pluginResult.step;
        p0.detail = pluginResult.detail;
      } else if (st === 'fail') {
        p0.create = 'fail';
        p0.overall = 'fail';
      } else {
        p0.create = 'pass';
        p0.overall = 'pass';
      }
      p1.row = await runP1RowActions(page, ctx);
      p1.export = await runP1Export(page, ctx);
      return {
        status: deriveP0Status(p0),
        step: pluginResult.step,
        hash,
        p0,
        p1,
        detail: pluginResult.detail
      };
    }

    await runListProbe(page);
    p0.query = 'pass';
    if (ctx.apiFailures.length > 0) {
      p0.api5xx = 'fail';
      p0.overall = 'fail';
      return {
        status: 'fail',
        severity: 'Critical',
        step: 'api_5xx_after_list',
        hash,
        p0,
        p1,
        apiFailures: [...ctx.apiFailures],
        evidence: { screenshot: screenshotName }
      };
    }

    const createResult = await runCreateProbe(page, ctx.stamp);
    if (createResult.mode === 'readOnly') {
      p0.create = 'readOnly';
    } else if (createResult.mode === 'fail') {
      p0.create = 'fail';
      p0.detail = createResult.detail;
    } else if (createResult.mode === 'needs_scenario') {
      p0.create = 'needs_scenario';
      p0.detail = createResult.detail;
    } else {
      p0.create = 'pass';
    }

    if (await hasErrorMessage(page)) {
      p0.create = 'fail';
      p0.overall = 'fail';
      return {
        status: 'fail',
        severity: 'Major',
        step: 'error_toast_after_create',
        hash,
        p0,
        p1,
        evidence: { screenshot: screenshotName }
      };
    }
    if (ctx.apiFailures.length > 0) {
      p0.api5xx = 'fail';
      p0.overall = 'fail';
      return {
        status: 'fail',
        severity: 'Critical',
        step: 'api_5xx_after_create',
        hash,
        p0,
        p1,
        apiFailures: [...ctx.apiFailures],
        evidence: { screenshot: screenshotName }
      };
    }

    p0.overall = deriveP0Status(p0);
    p0.step = createResult.step;
    p1.row = await runP1RowActions(page, ctx);
    p1.export = await runP1Export(page, ctx);

    const p1Fail = p1.row?.status === 'fail' || p1.export?.status === 'fail';
    return {
      status: p0.overall,
      step: createResult.step,
      hash,
      p0,
      p1,
      severity: p1Fail ? 'Major' : undefined
    };
  } catch (error) {
    p0.overall = 'fail';
    return {
      status: 'fail',
      severity: 'Critical',
      step: 'exception',
      error: error instanceof Error ? error.message : String(error),
      p0,
      p1,
      evidence: { screenshot: screenshotName }
    };
  } finally {
    detach();
  }
}

function computeTotals(results) {
  let p0Pass = 0;
  let p0Fail = 0;
  let p0ReadOnly = 0;
  let p0NeedsScenario = 0;
  let p1Pass = 0;
  let p1Fail = 0;
  for (const r of results) {
    if (r.status === 'fail') {
      p0Fail += 1;
    } else if (r.status === 'needs_scenario') {
      p0NeedsScenario += 1;
    } else if (r.status === 'readOnly') {
      p0ReadOnly += 1;
      p0Pass += 1;
    } else if (r.status === 'pass') {
      p0Pass += 1;
    }
    const row = r.p1?.row?.status;
    const exp = r.p1?.export?.status;
    if (row === 'pass' || exp === 'pass') {
      p1Pass += 1;
    }
    if (row === 'fail' || exp === 'fail') {
      p1Fail += 1;
    }
  }
  return {
    pages: results.length,
    p0Pass,
    p0Fail,
    p0ReadOnly,
    p0NeedsScenario,
    p1Pass,
    p1Fail
  };
}

function runL1Crawl() {
  try {
    const out = execSync('node scripts/host-navigation-crawl.mjs', {
      cwd: rootDir,
      encoding: 'utf8',
      timeout: 600_000,
      env: {
        ...process.env,
        FULLNET_E2E_ADMIN_URL: adminBaseUrl,
        FULLNET_E2E_ADMIN_ORIGIN: origin
      }
    });
    const match = out.match(/Summary:.*fail[:\s]+(\d+)/i) ?? out.match(/失败[:\s]+(\d+)/);
    const fail = match ? Number(match[1]) : 0;
    return { summary: fail === 0 ? '97/97 通过（fail=0）' : `crawl fail=${fail}`, exitCode: fail === 0 ? 0 : 1 };
  } catch (error) {
    return {
      summary: `crawl 执行失败: ${error instanceof Error ? error.message.slice(0, 120) : String(error)}`,
      exitCode: 1
    };
  }
}


async function main() {
  const { onlyFailed, paths: pathFilter, skipL3, skipCrawl } = parseArgs(process.argv.slice(2));
  mkdirSync(outDir, { recursive: true });
  mkdirSync(screenshotDir, { recursive: true });

  let l1Crawl = { summary: '已跳过 (--skip-crawl)', exitCode: null };
  if (!skipCrawl) {
    l1Crawl = runL1Crawl();
  }

  let manifest;
  const browserNav = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHROMIUM_CHANNEL ?? 'msedge',
    headless: true
  });
  const navContext = await browserNav.newContext();
  const token = await loginAccessToken(navContext.request);
  const adminUserId = await fetchAdminUserId(navContext.request, token);
  const tree = await fetchNavigation(navContext.request, token);
  manifest = uniqueByPath(flattenNavigationWithDomain(tree));
  await browserNav.close();

  if (onlyFailed && existsSync(priorAuditPath)) {
    const prior = JSON.parse(readFileSync(priorAuditPath, 'utf8'));
    const failed = new Set(
      prior.results.filter(r => r.status === 'fail' || r.p0?.overall === 'fail').map(r => r.path)
    );
    manifest = manifest.filter(item => failed.has(item.path));
  }
  if (pathFilter?.length) {
    const allowed = new Set(pathFilter);
    manifest = manifest.filter(item => allowed.has(item.path));
  }

  writeFileSync(join(outDir, 'host-navigation-manifest.json'), JSON.stringify(manifest, null, 2), 'utf8');

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
    ctx.adminUserId = adminUserId;
    process.stdout.write(`[${index + 1}/${manifest.length}] ${item.path} ... `);
    const record = await auditPage(page, item, ctx);
    const row = { ...item, ...record };
    if (record.status === 'fail' && record.evidence?.screenshot) {
      await page.screenshot({
        path: join(screenshotDir, record.evidence.screenshot),
        fullPage: true
      }).catch(() => {});
    }
    results.push(row);
    console.log(record.status);
  }
  await browser.close();

  const totals = computeTotals(results);
  let l3Playwright = { summary: '已跳过 (--skip-l3)', exitCode: null, command: null };
  if (!skipL3) {
    l3Playwright = runL3FullSuite(rootDir);
    writeL3Artifact(rootDir, l3Playwright);
  }

  const audit = {
    auditedAt: new Date().toISOString(),
    environment: { apiBaseUrl, adminBaseUrl, origin, username },
    l1Crawl,
    l3Playwright,
    totals,
    results
  };
  writeFileSync(auditJsonPath, JSON.stringify(audit, null, 2), 'utf8');
  writeFileSync(reportMdPath, buildQaReportMarkdown(audit), 'utf8');

  console.log('\nTotals:', JSON.stringify(totals));
  console.log(`Report: ${reportMdPath}`);
  if (totals.p0Fail > 0) {
    process.exitCode = 1;
  }
}

await main();
