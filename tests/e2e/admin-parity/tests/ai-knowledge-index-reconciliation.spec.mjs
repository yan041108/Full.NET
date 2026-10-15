import { expect, test } from '@playwright/test';

const baseId = '01900000-0000-7000-8000-000000000001';
const docId = '01900000-0000-7000-8000-000000000002';
const versionId = '01900000-0000-7000-8000-000000000003';
const taskId = '01900000-0000-7000-8000-000000000004';
const userId = '01900000-0000-7000-8000-000000000005';
const sessionId = '01900000-0000-7000-8000-000000000006';
const indexPath = `/api/v1/ai/knowledge-bases/${baseId}/documents/${docId}/versions/${versionId}/index`;
async function setup(page, allowIndex = true, allowRebuild = true) {
  let reads = 0; let writes = 0; const rebuilds = []; let loseNext = false; let currentTask = taskId;
  let reason = 'generation_unavailable'; let recoveryAction = 'rebuild'; let denied = false;
  const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'index-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', route => json(route, {
    id: userId, username: 'index-test', displayName: '索引测试', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    permissions: ['identity.navigation.read', 'ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.versions_read', ...(allowIndex ? ['ai.knowledge_documents.index_read', 'ai.knowledge_documents.index_retry', 'ai.knowledge_documents.index_enqueue', 'ai.knowledge_documents.upload', 'ai.knowledge_documents.source_read'] : []), ...(allowRebuild ? ['ai.knowledge_documents.index_rebuild'] : [])],
    sessionId, preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false,
  }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'knowledge', parentId: null, routeName: 'ai-knowledge-documents', path: '/ai/knowledge-documents', componentKey: 'ai-knowledge-documents', title: '知识文件', caption: '授权文件', icon: 'document', order: 1, requiredPermission: 'ai.knowledge_documents.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => json(route, { activeTenantCount: 0, onlineSessionCount: 1, todayRequestCount: 0, todayErrorRate: 0, recentActivities: [], accessTrafficTrend: null, businessEntries: [] }));
  await page.route('**/api/v1/ai/knowledge-bases?*', route => json(route, { items: [{ id: baseId, name: '对账知识库', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: '2026-10-08T00:00:00Z', updatedAtUtc: null, version: 1 }], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents?*`, route => json(route, { items: [{ id: docId, knowledgeBaseId: baseId, title: '待恢复文档', description: null, status: 'pending_processing', createdAtUtc: '2026-10-08T00:00:00Z', updatedAtUtc: null, version: 3 }], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents/${docId}/versions?*`, route => json(route, { items: [{ id: versionId, documentId: docId, versionNumber: 1, originalFileName: 'source.txt', contentType: 'text/plain', contentHash: 'test', sizeBytes: 10, processingStatus: 'pending_processing', createdAtUtc: '2026-10-08T00:00:00Z' }], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**${indexPath}`, route => route.request().method() === 'POST' ? (writes++, json(route, { taskId, versionId }, 202)) : json(route, { versionId, taskId, state: 'failed', failureCode: 'ai.knowledge.index_unavailable' }));
  await page.route(`**${indexPath}/reconciliation`, route => {
    reads++;
    return denied ? json(route, { status: 404, title: 'Resource unavailable', code: 'ai.knowledge.not_found' }, 404) : json(route, { versionId, taskId: currentTask, state: 'failed', reason, recoveryAction, documentVersion: 3, knowledgeBaseVersion: 4 });
  });
  await page.route(`**${indexPath}/${taskId}/retry`, route => { writes++; return json(route, { status: 409, title: 'Original session required', code: 'ai.knowledge.index_conflict' }, 409); });
  await page.route(`**${indexPath}/rebuild`, route => { writes++; rebuilds.push(route.request().postDataJSON());
    if (loseNext) { loseNext = false; return json(route, { status: 503, title: 'Result unknown', code: 'temporary.unavailable' }, 503); }
    return json(route, { taskId: '01900000-0000-7000-8000-000000000007', versionId }, 202);
  });
  await page.goto('/#/ai/knowledge-documents'); await page.getByTestId('knowledge-base-select').click();
  await page.getByRole('option', { name: '对账知识库', exact: true }).click(); await page.getByTestId('knowledge-versions').click();
  return { reads: () => reads, writes: () => writes, rebuilds, loseNext: () => { loseNext = true; }, currentTask: value => { currentTask = value; }, advise: (nextReason, nextAction) => { reason = nextReason; recoveryAction = nextAction; }, deny: () => { denied = true; } };
}

// 使用真实 Vue/Edge 与受控 HTTP，证明恢复交互；不冒充真实双库或模型执行验收。
test('重建建议不自动提交，重试冲突须重新对账，撤权清除操作资格', async ({ page }) => {
  const state = await setup(page); await page.getByTestId('knowledge-index').click(); expect(state.reads()).toBe(0);
  await page.getByTestId('knowledge-index-reconcile').click(); const result = page.getByTestId('knowledge-index-reconciliation');
  await expect(result).toContainText('重建可能产生额外费用'); await expect(page.getByTestId('knowledge-index-rebuild')).toBeDisabled(); await expect(page.getByTestId('knowledge-index-retry')).toHaveCount(0); await expect(page.getByTestId('knowledge-index-enqueue')).toHaveCount(0); expect(state.writes()).toBe(0);
  state.advise('task_failed', 'retry'); await page.getByTestId('knowledge-index-refresh').click(); await expect(page.getByTestId('knowledge-index-retry')).toBeVisible();
  await page.getByTestId('knowledge-index-retry').click(); await expect(page.getByText('Original session required', { exact: true })).toBeVisible(); await expect(result).toHaveCount(0); await expect(page.getByTestId('knowledge-index-retry')).toHaveCount(0); expect(state.writes()).toBe(1);
  state.deny(); await page.getByTestId('knowledge-index-refresh').click(); await expect(page.getByText('Resource unavailable', { exact: true })).toBeVisible();
  await expect(page.getByTestId('knowledge-index-enqueue')).toHaveCount(0); await expect(result).toHaveCount(0); expect(state.reads()).toBe(3); expect(state.writes()).toBe(1);
});
test('入队建议须显式提交，接受后不宣称索引就绪', async ({ page }) => {
  const state = await setup(page); state.advise('not_indexed', 'enqueue'); await page.getByTestId('knowledge-index').click(); await page.getByTestId('knowledge-index-reconcile').click();
  await expect(page.getByTestId('knowledge-index-enqueue')).toBeVisible(); expect(state.writes()).toBe(0); await page.getByTestId('knowledge-index-enqueue').click();
  await expect(page.getByTestId('knowledge-index-accepted')).toContainText('任务已接受'); await expect(page.getByTestId('knowledge-index-reconciliation')).toHaveCount(0); expect(state.writes()).toBe(1);
});
test('无索引读取权时不渲染索引与对账入口', async ({ page }) => {
  const state = await setup(page, false); await expect(page.getByTestId('knowledge-index')).toHaveCount(0); await expect(page.getByTestId('knowledge-index-reconcile')).toHaveCount(0); expect(state.reads()).toBe(0); expect(state.writes()).toBe(0);
});

test('独立重建必须确认新增费用并使用本次对账快照', async ({ page }) => {
  const state = await setup(page); await page.getByTestId('knowledge-index').click(); await page.getByTestId('knowledge-index-reconcile').click();
  const rebuild = page.getByTestId('knowledge-index-rebuild'); await expect(rebuild).toBeDisabled(); expect(state.writes()).toBe(0);
  await page.getByTestId('knowledge-index-rebuild-confirm').click(); await rebuild.click();
  await expect(page.getByTestId('knowledge-index-accepted')).toContainText('01900000-0000-7000-8000-000000000007');
  expect(state.rebuilds).toHaveLength(1); expect(state.rebuilds[0]).toEqual({ clientRequestId: expect.any(String), expectedTaskId: taskId, expectedDocumentVersion: 3, expectedKnowledgeBaseVersion: 4, confirmAdditionalCost: true });
  expect(state.rebuilds[0].clientRequestId).toMatch(/^[a-f0-9-]{36}$/i); await expect(page.getByTestId('knowledge-index-reconciliation')).toHaveCount(0);
});

test('重建结果未知后只允许显式同键确认，不自动补发', async ({ page }) => {
  const state = await setup(page); state.loseNext(); await page.getByTestId('knowledge-index').click(); await page.getByTestId('knowledge-index-reconcile').click();
  await page.getByTestId('knowledge-index-rebuild-confirm').click(); await page.getByTestId('knowledge-index-rebuild').click();
  await expect(page.getByTestId('knowledge-index-rebuild-pending')).toBeVisible(); expect(state.rebuilds).toHaveLength(1);
  await page.getByTestId('knowledge-index-rebuild').click(); await expect(page.getByTestId('knowledge-index-accepted')).toBeVisible();
  expect(state.rebuilds).toHaveLength(2); expect(state.rebuilds[1]).toEqual(state.rebuilds[0]);
});

test('缺少独立重建权限仍可对账，但不显示确认和提交入口', async ({ page }) => {
  const state = await setup(page, true, false); await page.getByTestId('knowledge-index').click(); await page.getByTestId('knowledge-index-reconcile').click();
  await expect(page.getByTestId('knowledge-index-reconciliation')).toBeVisible(); await expect(page.getByTestId('knowledge-index-rebuild')).toHaveCount(0);
  await expect(page.getByTestId('knowledge-index-rebuild-confirm')).toHaveCount(0); expect(state.writes()).toBe(0);
});

test('权威任务改变后清除未知请求和费用确认，重新提交使用新请求键', async ({ page }) => {
  const state = await setup(page); state.loseNext(); await page.getByTestId('knowledge-index').click(); await page.getByTestId('knowledge-index-reconcile').click();
  await page.getByTestId('knowledge-index-rebuild-confirm').click(); await page.getByTestId('knowledge-index-rebuild').click(); await expect(page.getByTestId('knowledge-index-rebuild-pending')).toBeVisible();
  const next = '01900000-0000-7000-8000-000000000008'; state.currentTask(next); await page.getByTestId('knowledge-index-refresh').click();
  await expect(page.getByTestId('knowledge-index-rebuild-pending')).toHaveCount(0); await expect(page.getByTestId('knowledge-index-rebuild')).toBeDisabled();
  await page.getByTestId('knowledge-index-rebuild-confirm').click(); await page.getByTestId('knowledge-index-rebuild').click(); await expect(page.getByTestId('knowledge-index-accepted')).toBeVisible();
  expect(state.rebuilds).toHaveLength(2); expect(state.rebuilds[1].expectedTaskId).toBe(next); expect(state.rebuilds[1].clientRequestId).not.toBe(state.rebuilds[0].clientRequestId);
});
