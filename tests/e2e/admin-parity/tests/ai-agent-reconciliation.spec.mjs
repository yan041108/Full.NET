import { expect, test } from '@playwright/test';

const id = n => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const runId = id(1), secondRunId = id(2);
const path = `/api/v1/ai/agent/runs/${runId}`;
const digest = 'a'.repeat(64);

// 真实 Vue/Edge 配合受控 HTTP 验证交互，双库事务和外部核证仍由对应集成验收证明。
async function setup(page, writable = true) {
  const writes = []; let retryUnknown = false; let conflict = false; let closed = false;
  const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'reconciliation-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', route => json(route, { id: id(3), username: 'reconciliation-test', displayName: '对账测试', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    permissions: ['identity.navigation.read', 'ai.agent_runs.read', ...(writable ? ['ai.agent_runs.reconcile'] : [])], sessionId: id(4), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'runs', parentId: null, routeName: 'ai-agent-runs', path: '/ai/agent-runs', componentKey: 'ai-agent-runs', title: 'Agent运行', caption: 'Agent运行', icon: 'chat', order: 1, requiredPermission: 'ai.agent_runs.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/ai/model-configs?*', route => json(route, { items: [], page: 1, pageSize: 100, total: 0 }));
  const run = target => ({ id: target, statusKey: target === secondRunId || closed ? 'failed' : 'reconciliation_required', definitionKey: 'fullnet-single-text-v1', definitionVersion: 1,
    deadlineAtUtc: '2026-10-15T00:00:00Z', createdAtUtc: '2026-10-14T00:00:00Z', updatedAtUtc: '2026-10-14T00:00:00Z' });
  await page.route(`**${path}`, route => json(route, run(runId)));
  await page.route(`**/api/v1/ai/agent/runs/${secondRunId}`, route => json(route, run(secondRunId)));
  await page.route('**/api/v1/ai/agent/runs/*/events/stream', route => route.fulfill({ status: 200, contentType: 'text/event-stream', body: ': complete\n\n' }));
  await page.route(`**${path}/reconciliation`, async route => {
    if (route.request().method() === 'GET') return json(route, { runId, version: 8, statusKey: 'reconciliation_required', usageStatus: 'unknown', reservedTokens: 200, quotaMonthKey: '2026-09', inputTokens: null, outputTokens: null, cachedInputTokens: null, canReconcile: !closed });
    writes.push(route.request().postDataJSON());
    if (retryUnknown) { retryUnknown = false; return route.abort('failed'); }
    if (conflict) { conflict = false; return json(route, { type: 'about:blank', title: 'Version conflict', status: 409, code: 'ai.agent_run.version_conflict' }, 409); }
    closed = true; return json(route, true);
  });
  await page.goto('/ai/agent-runs');
  await page.getByTestId('ai-agent-runs-id').locator('input').fill(runId);
  await page.getByTestId('ai-agent-runs-load').click();
  const panel = page.getByTestId('ai-reconciliation-panel'); await expect(panel).toBeVisible();
  await panel.getByRole('button', { name: '读取账本', exact: true }).click(); await expect(panel).toContainText('2026-09');
  return { panel, writes, failNext: () => { retryUnknown = true; }, conflictNext: () => { conflict = true; } };
}
async function confirm(panel) {
  await panel.getByRole('textbox', { name: '核证材料 SHA-256 摘要', exact: true }).fill(digest);
  await panel.getByRole('checkbox').check();
}
const submit = panel => panel.getByRole('button', { name: '关闭并记录对账', exact: true });

test('只有读取权限时显示原账本并隐藏人工写入入口', async ({ page }) => {
  const { panel, writes } = await setup(page, false);
  await expect(panel).toContainText('计量状态: 未知'); await expect(submit(panel)).toHaveCount(0);
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
  await panel.getByRole('checkbox').check(); await expect(submit(panel)).toBeDisabled();
  await panel.getByRole('textbox', { name: '缓存输入 Token（可选）', exact: true }).fill('0');
  await expect(panel.getByRole('checkbox')).not.toBeChecked(); await expect(submit(panel)).toBeDisabled();
  await panel.getByRole('checkbox').check(); await submit(panel).click(); await expect(panel).toHaveCount(0);
  expect(writes[0]).toMatchObject({ inputTokens: 1, outputTokens: 0, cachedInputTokens: 0 });
});
test('版本冲突保留材料，切换运行清除旧账本', async ({ page }) => {
  const { panel, writes, conflictNext } = await setup(page); conflictNext(); await confirm(panel); await submit(panel).click();
  await expect(panel).toContainText('对账处理失败');
  await expect(panel.getByRole('textbox', { name: '核证材料 SHA-256 摘要', exact: true })).toHaveValue(digest);
  await page.getByTestId('ai-agent-runs-id').locator('input').fill(secondRunId); await page.getByTestId('ai-agent-runs-load').click();
  await expect(panel).toHaveCount(0); expect(writes).toHaveLength(1);
});
