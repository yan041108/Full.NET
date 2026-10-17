import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { after, before, test } from 'node:test';
import { collectApplicationBrowserDiagnostic, waitForApplicationTenantLanding } from './support/application-crud-browser.mjs';
import * as browserSupport from './support/application-crud-browser.mjs';

const requireFromE2e = createRequire(new URL('../e2e/admin-real-stack/package.json', import.meta.url));
const { chromium } = requireFromE2e('@playwright/test');
const origin = 'http://localhost:25183';
let browser;
before(async () => { browser = await chromium.launch(); });
after(async () => { await browser?.close(); });

test('browser diagnostic preserves completed values', async () => {
  assert.equal(await collectApplicationBrowserDiagnostic(() => 'captured', 'missing'), 'captured');
});

test('navigation diagnostic keeps replayable route metadata and excludes other response fields', () => {
  const snapshot = browserSupport.summarizeApplicationNavigation([{ id: 'catalog', path: '/catalog',
    accessToken: 'never-record', children: [{ id: 'products', path: '/catalog/products',
      componentKey: 'm7-catalog-products', title: 'Products', requiredPermission: 'catalog.products.read', children: [] }] }]);
  assert.deepEqual(snapshot.nodes, [{ id: 'catalog', path: '/catalog', children: [{ id: 'products',
    path: '/catalog/products', componentKey: 'm7-catalog-products', title: 'Products',
    requiredPermission: 'catalog.products.read', children: [] }] }]);
  assert.equal(snapshot.truncated, false);
  assert.equal(JSON.stringify(snapshot).includes('never-record'), false);
});

test('navigation diagnostic bounds node count, depth, text and redacts current credentials', () => {
  const tree = Array.from({ length: 80 }, (_, index) => ({ id: String(index), title: 'credential-' + 'x'.repeat(800), children: [] }));
  const snapshot = browserSupport.summarizeApplicationNavigation(tree, text => text.replaceAll('credential', '[REDACTED]'));
  assert.equal(snapshot.nodes.length, 64);
  assert.equal(snapshot.truncated, true);
  assert.ok(snapshot.nodes.every(node => node.title.length <= 500 && !node.title.includes('credential')));
  const root = { id: 'root', children: [] }; root.children.push(root);
  assert.equal(browserSupport.summarizeApplicationNavigation([root]).truncated, true);
});

test('navigation diagnostic safely rejects non-array bodies', () => {
  assert.deepEqual(browserSupport.summarizeApplicationNavigation({ accessToken: 'never-record' }), { nodes: [], truncated: false });
});

test('CPU diagnostic reports sampled hot frames without URL queries or profile payloads', () => {
  const result = browserSupport.summarizeApplicationCpuProfile({ startTime: 100, endTime: 500,
    nodes: [{ id: 1, callFrame: { functionName: 'render', url: 'http://localhost:25183/src/view.ts?token=never-record', lineNumber: 4 } },
      { id: 2, callFrame: { functionName: 'load', url: 'http://localhost:25183/src/api.ts#never-record', lineNumber: 7 } }],
    samples: [1, 2, 1, 1, 999] });
  assert.equal(result.durationMicroseconds, 400);
  assert.equal(result.sampleCount, 5);
  assert.deepEqual(result.hotFrames[0], { functionName: 'render', path: '/src/view.ts', lineNumber: 4, samples: 3 });
  assert.equal(JSON.stringify(result).includes('never-record'), false);
});

test('CPU diagnostic bounds output and tolerates unavailable profiles', () => {
  assert.equal(browserSupport.summarizeApplicationCpuProfile(undefined), undefined);
  const result = browserSupport.summarizeApplicationCpuProfile({ nodes: Array.from({ length: 100 }, (_, id) =>
    ({ id, callFrame: { functionName: 'f' + id, url: '', lineNumber: 0 } })), samples: Array.from({ length: 100 }, (_, id) => id) });
  assert.equal(result.hotFrames.length, 32);
});

test('CPU diagnostic never terminates a successful page when stopping is unresponsive', async () => {
  const commands = [];
  const session = { send(command) { commands.push(command); return new Promise(() => {}); },
    async detach() { commands.push('detach'); } };
  const result = await browserSupport.finishApplicationCpuProfile(session, { timeoutMs: 20 });
  assert.equal(result.summary, undefined);
  assert.equal(result.executionTerminatedForDiagnostic, false);
  assert.deepEqual(commands, ['Profiler.stop', 'detach']);
});

test('CPU diagnostic detaches a session created after initialization expired', async () => {
  const commands = [];
  let created;
  const context = { newCDPSession: () => new Promise(resolve => { created = resolve; }) };
  const result = await browserSupport.startApplicationCpuProfile(context, {}, { timeoutMs: 20 });
  assert.equal(result, undefined);
  created({ async send(command) { commands.push(command); }, async detach() { commands.push('detach'); } });
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(commands, ['detach']);
});

for (const blockedCommand of ['Profiler.enable', 'Profiler.start']) {
  test(`CPU diagnostic owns cleanup while ${blockedCommand} is blocked`, async () => {
    const commands = [];
    let complete;
    const session = { send(command) { commands.push(command); return command === blockedCommand
      ? new Promise(resolve => { complete = resolve; }) : Promise.resolve(); },
    async detach() { commands.push('detach'); } };
    const result = await browserSupport.startApplicationCpuProfile({ async newCDPSession() { return session; } }, {}, { timeoutMs: 20 });
    assert.equal(result, undefined);
    assert.equal(commands.at(-1), 'detach');
    const atExpiry = [...commands]; complete();
    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(commands, atExpiry, 'expired initialization continued issuing profiler commands');
  });
}

test('browser diagnostic tolerates rejected and synchronous failures', async () => {
  assert.equal(await collectApplicationBrowserDiagnostic(() => Promise.reject(new Error('closed')), 'missing'), 'missing');
  assert.equal(await collectApplicationBrowserDiagnostic(() => { throw new Error('closed'); }, 'missing'), 'missing');
});

test('browser diagnostic abandons a stuck page operation and permits cleanup', { timeout: 1_000 }, async () => {
  const events = [];
  const original = new Error('original navigation failure');
  await assert.rejects(async () => {
    try { throw original; }
    finally {
      assert.equal(await collectApplicationBrowserDiagnostic(() => new Promise(() => {}), 'timed-out', 20), 'timed-out');
      events.push('cleanup');
    }
  }, error => error === original);
  assert.deepEqual(events, ['cleanup']);
});

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

test('CPU diagnostic recovers samples by terminating an already failed blocked page', { timeout: 15_000 }, () =>
  fixture('/', 'Host', async page => {
    const session = await browserSupport.startApplicationCpuProfile(page.context(), page);
    assert.ok(session);
    let blocked;
    try {
      // 故意阻塞本夹具页面；失败后的协议中止必须取回采样，不能依赖页面 evaluate。
      blocked = page.evaluate(function spinForCpuProbe() { while (true) Math.sqrt(Math.random()); }).catch(() => {});
      await new Promise(resolve => setTimeout(resolve, 150));
      const result = await browserSupport.finishApplicationCpuProfile(session, { allowTerminate: true, timeoutMs: 200 });
      assert.equal(result.executionTerminatedForDiagnostic, true);
      assert.ok(result.summary?.sampleCount > 0);
      assert.ok(result.summary.hotFrames.some(frame => frame.functionName === 'spinForCpuProbe'));
    } finally {
      await collectApplicationBrowserDiagnostic(() => session.send('Runtime.terminateExecution'), undefined);
      await collectApplicationBrowserDiagnostic(() => blocked, undefined);
      await collectApplicationBrowserDiagnostic(() => session.detach(), undefined);
    }
  }));

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
