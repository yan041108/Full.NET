import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = n => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const runId = id(1), targetId = id(2), modelId = id(3), approvalId = id(4);
const now = '2026-10-28T00:00:00Z', expiry = '2099-01-01T00:00:00Z';
const args = JSON.stringify({ sessionId: targetId, title: 'Reviewed title' });
// Edge 执行真实 Vue 页面；模型及数据库的事实由双库集成测试单独证明。
async function setup(page, decide = true) {
  const requests = [], decisions = []; let failCreate = false, failDecision = false, proposalDenied = false;
  let approval = { id: approvalId, runId, operationId: id(6), toolName: 'ai.chat.sessions.rename', toolVersion: 1,
    argumentsHash: 'a'.repeat(64), decisionKey: 'pending', actionSummary: '重命名聊天会话', targetSummary: 'Original', changeSummary: 'Reviewed title',
    scopeSummary: '本人聊天会话', expiresAtUtc: expiry, consumedAtUtc: null, version: 1, currency: null, costCeiling: null, reasonCode: null, targetResourceVersion: 1, createdAtUtc: now, updatedAtUtc: now };
  const json = (route, value, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'parallel-test', tokenType: 'Bearer', expiresAtUtc: expiry }));
  await page.route('**/api/v1/me', route => json(route, { id: id(7), username: 'parallel', displayName: '并行编排验收', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    permissions: ['identity.navigation.read', 'ai.agent_runs.read', 'ai.agent_runs.create', 'ai.agent_runs.resume', 'ai.chat.sessions.read', 'ai.chat.sessions.update', 'ai.agent_approvals.read', 'ai.agent_approvals.request', ...(decide ? ['ai.agent_approvals.decide'] : [])],
    sessionId: id(8), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'runs', parentId: null, routeName: 'ai-agent-runs', path: '/ai/agent-runs', componentKey: 'ai-agent-runs', title: 'Agent运行', caption: 'Agent运行', icon: 'chat', order: 1, requiredPermission: 'ai.agent_runs.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => json(route, { activeTenantCount: 0, onlineSessionCount: 1, todayRequestCount: 0, todayErrorRate: 0, recentActivities: [], accessTrafficTrend: null, businessEntries: [] }));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/ai/model-configs?*', route => json(route, { items: [], page: 1, pageSize: 100, total: 0 }));
  await page.route('**/api/v1/ai/chat/sessions?*', route => json(route, { items: [{ id: targetId, tenantId: null, ownerUserId: id(7), messageCount: 0, lastMessageAtUtc: null, title: 'Original', modelConfigId: modelId, modelName: 'Model', createdAtUtc: now, updatedAtUtc: now, version: 1 }], page: 1, pageSize: 100, total: 1 }));
  await page.route('**/api/v1/ai/agent/runs', route => {
    requests.push(route.request().postDataJSON()); if (failCreate) { failCreate = false; return route.abort('failed'); }
    return json(route, { runId }, 202);
  });
  await page.route(`**/api/v1/ai/agent/runs/${runId}`, route => json(route, { id: runId, definitionKey: 'fullnet-chat-review-workflow-v1', definitionVersion: 1, statusKey: 'awaiting_approval', deadlineAtUtc: expiry, createdAtUtc: now, updatedAtUtc: now }));
  await page.route('**/api/v1/ai/agent/runs/*/events/stream', route => route.fulfill({ status: 200, contentType: 'text/event-stream', body: ': complete\n\n' }));
  await page.route('**/api/v1/ai/agent/runs/*/workflow-proposal', route => proposalDenied
    ? json(route, { type: 'about:blank', title: 'Unavailable', status: 403, code: 'ai.agent_run.proposal_unavailable' }, 403)
    : json(route, { runId, operationId: id(6), targetChatSessionId: targetId, currentTitle: 'Original', targetVersion: 1, proposedTitle: 'Reviewed title', toolName: 'ai.chat.sessions.rename', toolVersion: 1, argumentsJson: args }));
  await page.route('**/api/v1/ai/agent/approvals', route => json(route, { approvalId }, 201));
  await page.route(`**/api/v1/ai/agent/approvals/${approvalId}`, route => json(route, approval));
  await page.route(`**/api/v1/ai/agent/approvals/${approvalId}/decide`, route => {
    decisions.push(route.request().postDataJSON());
    approval = { ...approval, decisionKey: 'approved', version: 2 };
    if (failDecision) { failDecision = false; return route.abort('failed'); } return json(route, approval);
  });
  await page.goto('/#/ai/agent-runs');
  return { requests, decisions, failCreate: () => { failCreate = true; }, failDecision: () => { failDecision = true; }, denyProposal: () => { proposalDenied = true; } };
}
async function create(page) {
  await page.locator('.ai-agent-runs-create-form .el-select').first().click();
  await page.getByRole('option', { name: '并行评审会话标题', exact: true }).click();
  await page.locator('.ai-agent-runs-create-form > .el-input input').fill(modelId);
  await page.getByTestId('ai-workflow-target').click();
  await page.getByRole('option', { name: 'Original', exact: true }).click();
}
async function load(page) { await page.getByTestId('ai-agent-runs-id').fill(runId); await page.getByTestId('ai-agent-runs-load').click(); }
async function requestApproval(page) {
  await load(page); await page.getByTestId('ai-workflow-read-proposal').click();
  await expect(page.getByTestId('ai-workflow-approval')).toContainText('Reviewed title');
  await page.getByTestId('ai-workflow-request-approval').click();
  await page.getByRole('button', { name: '确定', exact: true }).click();
  await expect(page.getByTestId('ai-workflow-approval')).toContainText('审批尚未消费');
}
test('目标独立于登录会话，创建结果不明沿用原请求身份', async ({ page }) => {
  const f = await setup(page); await create(page); f.failCreate();
  await page.getByTestId('ai-agent-runs-create').click();
  await expect.poll(() => f.requests.length).toBe(1); await expect(page.getByTestId('ai-agent-runs-create')).toBeEnabled();
  await page.getByTestId('ai-agent-runs-create').click(); await expect.poll(() => f.requests.length).toBe(2);
  expect(f.requests[0]).toEqual(f.requests[1]); expect(f.requests[0].targetChatSessionId).toBe(targetId); expect(f.requests[0].targetChatSessionId).not.toBe(id(8));
  await expect(page.getByTestId('ai-workflow-read-proposal')).toBeVisible();
  expect((await new AxeBuilder({ page }).include('.ai-workflow-target').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
});
test('决定结果不明先读权威审批，批准与执行结果分别展示', async ({ page }) => {
  const f = await setup(page); await requestApproval(page); f.failDecision();
  await page.getByTestId('ai-workflow-approve').click(); await page.getByRole('button', { name: '确定', exact: true }).click();
  await expect(page.getByTestId('ai-workflow-approval')).toContainText('决定结果未确认');
  await expect(page.getByTestId('ai-workflow-approve')).toBeDisabled();
  await page.getByTestId('ai-workflow-lookup-approval').click(); await expect(page.getByTestId('ai-workflow-approve')).toHaveCount(0);
  await expect(page.getByTestId('ai-workflow-approval')).toContainText('已批准');
  await expect(page.getByTestId('ai-workflow-approval')).toContainText('审批尚未消费'); expect(f.decisions).toEqual([{ approve: true, expectedVersion: 1 }]);
  expect((await new AxeBuilder({ page }).include('.ai-workflow-approval').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
});
test('没有决定权限不能批准，提案读取撤权即清除旧提案', async ({ page }) => {
  const f = await setup(page, false); await requestApproval(page); await expect(page.getByTestId('ai-workflow-approve')).toHaveCount(0);
  f.denyProposal(); await page.getByTestId('ai-workflow-read-proposal').click();
  await expect(page.getByTestId('ai-workflow-request-approval')).toHaveCount(0);
  expect(f.decisions).toHaveLength(0);
});
test('移动视口可读取完整提案并使用键盘确认', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 }); await setup(page); await load(page);
  const read = page.getByTestId('ai-workflow-read-proposal'); await read.focus(); await page.keyboard.press('Enter');
  await expect(page.getByTestId('ai-workflow-approval')).toContainText('Reviewed title');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(await page.locator('.ai-agent-runs-view > .el-card').evaluateAll(cards => cards.every(card => card.scrollHeight <= card.clientHeight + 2))).toBe(true);
  await page.getByTestId('ai-agent-runs-create').scrollIntoViewIfNeeded(); await expect(page.getByTestId('ai-agent-runs-create')).toBeInViewport();
  await page.screenshot({ path: testInfo.outputPath('ai-parallel-mobile.png'), fullPage: true });
});
