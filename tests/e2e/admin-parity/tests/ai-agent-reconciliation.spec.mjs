import { expect, test } from '@playwright/test';

const id = n => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const runId = id(1), secondRunId = id(2);
const path = `/api/v1/ai/agent/runs/${runId}`;
const digest = 'a'.repeat(64);

// 真实 Vue/Edge 配合受控 HTTP 验证交互，双库事务和外部核证仍由对应集成验收证明。
async function setup(page, writable = true, recordedUsage = {}, creatable = false, operation) {
  let operationStatus = operation === 'resume' ? 'awaiting_approval' : 'running';
  const writes = []; let retryUnknown = false; let conflict = false; let closed = false; let readDenied = false; let writeDenied = false; let version = 8;
  const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'reconciliation-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', route => json(route, { id: id(3), username: 'reconciliation-test', displayName: '对账测试', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    permissions: ['identity.navigation.read', 'ai.agent_runs.read', ...(writable ? ['ai.agent_runs.reconcile'] : []), ...(creatable ? ['ai.agent_runs.create'] : []), ...(operation ? ['ai.agent_runs.cancel', 'ai.agent_runs.resume'] : [])], sessionId: id(4), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'runs', parentId: null, routeName: 'ai-agent-runs', path: '/ai/agent-runs', componentKey: 'ai-agent-runs', title: 'Agent运行', caption: 'Agent运行', icon: 'chat', order: 1, requiredPermission: 'ai.agent_runs.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => json(route, { activeTenantCount: 0, onlineSessionCount: 1, todayRequestCount: 0, todayErrorRate: 0, recentActivities: [], accessTrafficTrend: null, businessEntries: [] }));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/ai/model-configs?*', route => json(route, { items: [], page: 1, pageSize: 100, total: 0 }));
  const run = target => ({ id: target, statusKey: target === secondRunId || closed ? 'failed' : operation ? operationStatus : 'reconciliation_required', definitionKey: 'fullnet-single-text-v1', definitionVersion: 1,
    deadlineAtUtc: '2026-10-15T00:00:00Z', createdAtUtc: '2026-10-14T00:00:00Z', updatedAtUtc: '2026-10-14T00:00:00Z' });
  await page.route(`**${path}`, route => json(route, run(runId)));
  await page.route(`**/api/v1/ai/agent/runs/${secondRunId}`, route => json(route, run(secondRunId)));
  await page.route('**/api/v1/ai/agent/runs/*/events/stream', route => route.fulfill({ status: 200, contentType: 'text/event-stream', body: ': complete\n\n' }));
  await page.route(`**${path}/reconciliation`, async route => {
    if (route.request().method() === 'GET') return readDenied
      ? json(route, { type: 'about:blank', title: 'Unavailable', status: 404, code: 'ai.agent_run.not_found' }, 404)
      : json(route, { runId, version, statusKey: 'reconciliation_required', usageStatus: 'unknown', reservedTokens: 200, quotaMonthKey: '2026-09', inputTokens: null, outputTokens: null, cachedInputTokens: null, canReconcile: !closed, ...recordedUsage });
    writes.push(route.request().postDataJSON());
    if (retryUnknown) { retryUnknown = false; return route.abort('failed'); }
    if (writeDenied) return json(route, { type: 'about:blank', title: 'Unavailable', status: 403, code: 'identity.forbidden' }, 403);
    if (conflict) { conflict = false; version = 9; return json(route, { type: 'about:blank', title: 'Version conflict', status: 409, code: 'ai.agent_run.version_conflict' }, 409); }
    closed = true; return json(route, true);
  });
  await page.goto('/#/ai/agent-runs');
  await page.getByTestId('ai-agent-runs-id').fill(runId);
  await page.getByTestId('ai-agent-runs-load').click();
  const panel = page.getByTestId('ai-reconciliation-panel');
  if (operation) {
    await expect(page.getByTestId(`ai-agent-runs-${operation}`)).toBeVisible(); await expect(page.getByTestId('ai-agent-runs-load')).toBeEnabled();
  } else {
    await expect(panel).toBeVisible(); await panel.getByRole('button', { name: '读取账本', exact: true }).click(); await expect(panel).toContainText('2026-09');
  }
  return { panel, writes, setRunStatus: value => { operationStatus = value; }, failNext: () => { retryUnknown = true; }, conflictNext: () => { conflict = true; }, denyRead: () => { readDenied = true; }, denyWrite: () => { writeDenied = true; } };
}
// Element Plus 的原生复选框用于状态，真实用户通过可见标签切换；禁止强制操作隐藏输入。
async function review(panel) {
  const name = '我已核对外部执行结果及证据，确认关闭此运行';
  await panel.getByText(name, { exact: true }).click();
  await expect(panel.getByRole('checkbox', { name, exact: true })).toBeChecked();
}
async function confirm(panel) {
  await panel.getByRole('textbox', { name: '核证材料 SHA-256 摘要', exact: true }).fill(digest);
  await review(panel);
}
const submit = panel => panel.getByRole('button', { name: '关闭并记录对账', exact: true });

test('只有读取权限时显示原账本并隐藏人工写入入口', async ({ page }) => {
  const { panel, writes } = await setup(page, false);
  await expect(panel).toContainText('计量状态: 未知');
  await expect(panel.getByTestId('ai-reconciliation-usage')).toHaveText('原输入 Token: 未提供 · 原输出 Token: 未提供 · 原缓存输入 Token: 未提供');
  await expect(submit(panel)).toHaveCount(0);
  await expect(panel.getByRole('textbox')).toHaveCount(0); expect(writes).toHaveLength(0);
});
test('未知计量关闭须核对材料，成功后刷新真实运行状态', async ({ page }) => {
  const { panel, writes } = await setup(page); await expect(submit(panel)).toBeDisabled();
  await confirm(panel); await submit(panel).click(); await expect(panel).toHaveCount(0);
  expect(writes).toHaveLength(1); expect(writes[0]).toEqual({ clientRequestId: expect.any(String), expectedVersion: 8, evidenceDigest: digest.toUpperCase() });
  await expect(page.getByText('failed', { exact: true })).toBeVisible();
});
test('提交结果不明后保留材料及幂等身份，重试不重新执行', async ({ page }) => {
  const state = await setup(page); state.failNext(); await confirm(state.panel); await submit(state.panel).click();
  await expect(state.panel).toContainText('对账处理失败'); await expect(submit(state.panel)).toBeEnabled();
  await submit(state.panel).click(); await expect(state.panel).toHaveCount(0);
  expect(state.writes).toHaveLength(2); expect(state.writes[1]).toEqual(state.writes[0]);
});
test('计量边界和材料修改都要求重新确认，零用量保留数值', async ({ page }) => {
  const { panel, writes } = await setup(page); await confirm(panel); await panel.getByRole('switch').click();
  await panel.getByRole('textbox', { name: '输入 Token', exact: true }).fill('1');
  await panel.getByRole('textbox', { name: '输出 Token', exact: true }).fill('0');
  await panel.getByRole('textbox', { name: '缓存输入 Token（可选）', exact: true }).fill('2');
  await review(panel); await expect(submit(panel)).toBeDisabled();
  await panel.getByRole('textbox', { name: '缓存输入 Token（可选）', exact: true }).fill('0');
  await expect(panel.getByRole('checkbox')).not.toBeChecked(); await expect(submit(panel)).toBeDisabled();
  await review(panel); await submit(panel).click(); await expect(panel).toHaveCount(0);
  expect(writes[0]).toMatchObject({ inputTokens: 1, outputTokens: 0, cachedInputTokens: 0 });
});
test('版本冲突保留材料，切换运行清除旧账本', async ({ page }) => {
  const { panel, writes, conflictNext } = await setup(page); conflictNext(); await confirm(panel); await submit(panel).click();
  await expect(panel).toContainText('对账处理失败');
  await expect(panel.getByRole('textbox', { name: '核证材料 SHA-256 摘要', exact: true })).toHaveValue(digest);
  await expect(submit(panel)).toBeDisabled();
  await page.getByTestId('ai-agent-runs-id').fill(secondRunId); await page.getByTestId('ai-agent-runs-load').click();
  await expect(panel).toHaveCount(0); expect(writes).toHaveLength(1);
});
test('版本冲突必须读取新版本及重新核对才可再提交', async ({ page }) => {
  const { panel, writes, conflictNext } = await setup(page); conflictNext(); await confirm(panel); await submit(panel).click();
  await expect(panel).toContainText('请重新读取账本'); await expect(submit(panel)).toBeDisabled();
  await review(panel); await expect(submit(panel)).toBeDisabled();
  await panel.getByRole('button', { name: '读取账本', exact: true }).click();
  await expect(panel.getByRole('textbox', { name: '核证材料 SHA-256 摘要', exact: true })).toHaveValue(digest);
  await expect(panel.getByRole('checkbox')).not.toBeChecked(); await review(panel);
  await submit(panel).click(); await expect(panel).toHaveCount(0);
  expect(writes).toHaveLength(2); expect(writes[1].expectedVersion).toBe(9);
  expect(writes[1].clientRequestId).not.toBe(writes[0].clientRequestId);
});
test('读取撤权后刷新清除旧账本和提交入口', async ({ page }) => {
  const { panel, writes, denyRead } = await setup(page); await confirm(panel); denyRead();
  await panel.getByRole('button', { name: '读取账本', exact: true }).click();
  await expect(panel).toContainText('对账处理失败'); await expect(panel).not.toContainText('2026-09');
  await expect(panel.getByTestId('ai-reconciliation-usage')).toHaveCount(0);
  await expect(submit(panel)).toHaveCount(0); expect(writes).toHaveLength(0);
});
test('写入权威拒绝后清除旧账本并停止旧请求重试', async ({ page }) => {
  const { panel, writes, denyWrite } = await setup(page); await confirm(panel); denyWrite(); await submit(panel).click();
  await expect(panel).toContainText('对账处理失败'); await expect(panel).not.toContainText('2026-09');
  await expect(panel.getByTestId('ai-reconciliation-usage')).toHaveCount(0);
  await expect(submit(panel)).toHaveCount(0); expect(writes).toHaveLength(1);
});

test('只读账本显示已知原用量并区分零值与未提供', async ({ page }) => {
  const { panel, writes } = await setup(page, false, { usageStatus: 'known', inputTokens: 12, outputTokens: 0, cachedInputTokens: null });
  await expect(panel.getByTestId('ai-reconciliation-usage')).toHaveText('原输入 Token: 12 · 原输出 Token: 0 · 原缓存输入 Token: 未提供');
  await expect(submit(panel)).toHaveCount(0); expect(writes).toHaveLength(0);
});


test('创建结果不明沿用请求身份，正文改变后才采用新身份', async ({ page }) => {
  await setup(page, true, {}, true); const requests = [];
  await page.route('**/api/v1/ai/agent/runs', async route => {
    requests.push(route.request().postDataJSON());
    if (requests.length < 3) return route.abort('failed');
    return route.fulfill({ status: 202, contentType: 'application/json', body: JSON.stringify({ runId: secondRunId }) });
  });
  await page.locator('.ai-agent-runs-create-form .el-input__inner').fill(id(9));
  await page.getByTestId('ai-agent-runs-create-prompt').fill('Initial request');
  const create = page.getByTestId('ai-agent-runs-create');
  for (let attempt = 1; attempt <= 2; attempt++) {
    await create.click(); await expect.poll(() => requests.length).toBe(attempt);
    await expect(page.locator('.ai-agent-runs-view .art-page-alert')).toBeVisible(); await expect(create).toBeEnabled();
  }
  expect(requests[1]).toEqual(requests[0]);
  await page.getByTestId('ai-agent-runs-create-prompt').fill('Changed request'); await create.click();
  await expect(page.getByText('failed', { exact: true })).toBeVisible(); expect(requests).toHaveLength(3);
  expect(requests[2].clientRequestId).not.toBe(requests[0].clientRequestId); expect(requests[2].prompt).toBe('Changed request');
});

test('新运行尚在读取时旧账本和对账入口立即消失', async ({ page }) => {
  const { panel } = await setup(page); let release; let entered = false;
  const wait = new Promise(resolve => { release = resolve; });
  await page.route(`**/api/v1/ai/agent/runs/${secondRunId}`, async route => {
    entered = true; await wait;
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id: secondRunId, statusKey: 'failed', definitionKey: 'fullnet-single-text-v1', definitionVersion: 1, deadlineAtUtc: '2026-10-15T00:00:00Z', createdAtUtc: '2026-10-14T00:00:00Z', updatedAtUtc: '2026-10-14T00:00:00Z' }) });
  });
  try {
    await page.getByTestId('ai-agent-runs-id').fill(secondRunId); await page.getByTestId('ai-agent-runs-load').click();
    await expect.poll(() => entered).toBe(true); await expect(panel).toHaveCount(0);
  } finally { release(); }
  await expect(page.getByText('failed', { exact: true })).toBeVisible();
});


for (const action of ['cancel', 'resume']) {
  test(`${action} 的迟到成功不能切回已离开的运行`, async ({ page }) => {
    await setup(page, true, {}, false, action); let release; let calls = 0;
    const wait = new Promise(resolve => { release = resolve; });
    await page.route(`**${path}/${action}`, async route => { calls++; await wait; return route.fulfill({ status: 200, contentType: 'application/json', body: 'true' }); });
    try {
      await page.getByTestId(`ai-agent-runs-${action}`).click(); await expect.poll(() => calls).toBe(1);
      await page.getByTestId('ai-agent-runs-id').fill(secondRunId); await page.getByTestId('ai-agent-runs-load').click();
      await expect(page.getByText('failed', { exact: true })).toBeVisible();
      const response = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith(`${path}/${action}`));
      release(); await (await response).finished(); await expect(page.locator('.ai-agent-runs-view')).toHaveAttribute('aria-busy', 'false');
      await expect(page.getByTestId('ai-agent-runs-id')).toHaveValue(secondRunId); await expect(page.getByText('failed', { exact: true })).toBeVisible();
    } finally { release(); }
  });
  test(`${action} 等待期间禁用重复派发，明确确认后恢复操作`, async ({ page }) => {
    const state = await setup(page, true, {}, false, action); let release; let calls = 0;
    const wait = new Promise(resolve => { release = resolve; });
    await page.route(`**${path}/${action}`, async route => { calls++; await wait; state.setRunStatus(action === 'cancel' ? 'failed' : 'running'); return route.fulfill({ status: 200, contentType: 'application/json', body: 'true' }); });
    try {
      const button = page.getByTestId(`ai-agent-runs-${action}`); await button.click(); await expect(button).toBeDisabled(); await expect.poll(() => calls).toBe(1);
    } finally { release(); }
    await expect(page.getByText(action === 'cancel' ? 'failed' : 'running', { exact: true })).toBeVisible();
    if (action === 'resume') await expect(page.getByTestId('ai-agent-runs-cancel')).toBeEnabled();
  });
  test(`${action} 的 false 回执显示失败且不自动重新读取`, async ({ page }) => {
    const reads = []; page.on('request', request => { if (request.method() === 'GET' && request.url().endsWith(path)) reads.push(request.url()); });
    await setup(page, true, {}, false, action); const count = reads.length;
    await page.route(`**${path}/${action}`, route => route.fulfill({ status: 200, contentType: 'application/json', body: 'false' }));
    await page.getByTestId(`ai-agent-runs-${action}`).click(); await expect(page.locator('.ai-agent-runs-view .art-page-alert')).toBeVisible();
    await expect(page.getByTestId(`ai-agent-runs-${action}`)).toBeEnabled(); expect(reads).toHaveLength(count);
  });
}

test('恢复成功且事件流未结束时，用户仍可取消运行', async ({ page }) => {
  const state = await setup(page, true, {}, false, 'resume'); let release; let streaming = false;
  const wait = new Promise(resolve => { release = resolve; });
  await page.route('**/api/v1/ai/agent/runs/*/events/stream', async route => { streaming = true; await wait; return route.fulfill({ status: 200, contentType: 'text/event-stream', body: ': complete\n\n' }); });
  await page.route(`**${path}/resume`, route => { state.setRunStatus('running'); return route.fulfill({ status: 200, contentType: 'application/json', body: 'true' }); });
  await page.route(`**${path}/cancel`, route => route.fulfill({ status: 200, contentType: 'application/json', body: 'false' }));
  try {
    await page.getByTestId('ai-agent-runs-resume').click(); await expect.poll(() => streaming).toBe(true);
    const cancel = page.getByTestId('ai-agent-runs-cancel'); await expect(cancel).toBeEnabled(); await cancel.click();
    await expect(page.locator('.ai-agent-runs-view .art-page-alert')).toBeVisible();
  } finally { release(); }
});
