import { expect, test } from '@playwright/test';

const base = '01900000-0000-7000-8000-000000000001'; const first = '01900000-0000-7000-8000-000000000002';
const second = '01900000-0000-7000-8000-000000000003'; const version = '01900000-0000-7000-8000-000000000004';
const user = '01900000-0000-7000-8000-000000000005';
async function setup(page, allowed = true, paidGrants = false) {
  const reads = []; const writes = []; let conflict = false;
  const grantWrites = []; let grant;
  const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'directory-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', route => json(route, { id: user, username: 'directory-test', displayName: '目录测试', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    permissions: ['identity.navigation.read', 'ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.versions_read', 'ai.knowledge_documents.index_read', ...(allowed ? ['ai.knowledge_documents.index_changes_read'] : []), ...(paidGrants ? ['ai.knowledge_documents.index_grant_read','ai.knowledge_documents.index_grant_create','ai.knowledge_documents.index_grant_revoke','ai.knowledge_documents.index_enqueue','ai.knowledge_documents.source_read','ai.knowledge_documents.upload'] : [])],
    sessionId: user, preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'knowledge', parentId: null, routeName: 'ai-knowledge-documents', path: '/ai/knowledge-documents', componentKey: 'ai-knowledge-documents', title: '知识文件', caption: '授权文件', icon: 'document', order: 1, requiredPermission: 'ai.knowledge_documents.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => json(route, { activeTenantCount: 0, onlineSessionCount: 1, todayRequestCount: 0, todayErrorRate: 0, recentActivities: [], accessTrafficTrend: null, businessEntries: [] }));
  await page.route('**/api/v1/ai/knowledge-bases?*', route => json(route, { items: [{ id: base, name: '确认来源知识库', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: '2026-10-16T00:00:00Z', updatedAtUtc: null, version: 1 }], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/documents?*`, route => json(route, { items: [], page: 1, pageSize: 20, total: 0 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${base}`, route => json(route, { id: base, name: '确认来源知识库', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: user, embeddingModelVersion: 1, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: '2026-10-16T00:00:00Z', updatedAtUtc: null, version: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/index-grants**`, route => {
    if (route.request().method() === 'GET') return json(route, grant ? [grant] : []);
    const body = route.request().postDataJSON(); grantWrites.push({ path: new URL(route.request().url()).pathname, body });
    if (route.request().url().endsWith('/revoke')) { expect(body).toEqual({ version: grant.version }); grant = { ...grant, isRevoked: true, version: grant.version + 1 }; }
    else grant = { id: first, knowledgeBaseId: base, knowledgeBaseVersion: 1, expiresAtUtc: body.expiresAtUtc, documentLimit: body.documentLimit, admittedDocuments: 0, inputTokenLimitPerDocument: body.inputTokenLimitPerDocument, isRevoked: false, version: 1 };
    return json(route, grant);
  });
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/index-changes?*`, route => {
    const url = new URL(route.request().url()); reads.push(url.searchParams.get('afterDocumentId'));
    if (conflict) return json(route, { title: '目录已变化，请刷新', status: 409, code: 'ai.knowledge.version_conflict' }, 409);
    const later = url.searchParams.has('afterDocumentId');
    return json(route, { items: [{ documentId: later ? second : first, title: later ? '来源第二页' : '来源第一页', versionId: version, versionNumber: 2,
      createdAtUtc: '2026-10-16T00:00:00Z', updatedAtUtc: '2026-10-16T01:00:00Z' }], nextDocumentId: later ? null : first });
  });
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/documents/*/versions/${version}/index`, route => json(route, { versionId: version, taskId: null, state: 'pending_processing', failureCode: null }));
  page.on('request', request => { if (/\/index(?:\/|$)/.test(new URL(request.url()).pathname) && request.method() !== 'GET') writes.push(request.url()); });
  await page.goto('/#/ai/knowledge-documents'); await page.getByTestId('knowledge-base-select').click();
  await page.getByRole('option', { name: '确认来源知识库', exact: true }).click();
  return { reads, writes, grantWrites, conflict: value => { conflict = value; } };
}

// 真实 Vue/Edge 与受控 HTTP 验证交互；服务端和双库以各自 Integration/Native 证据为准。
test('已确认来源只读分页，显式选择后重新读取索引状态', async ({ page }) => {
  const state = await setup(page); expect(state.reads).toHaveLength(0);
  await page.getByTestId('knowledge-changes').click(); await expect(page.getByText('来源第一页', { exact: true })).toBeVisible();
  await expect(page.getByText(/不表示索引已就绪/)).toBeVisible(); expect(state.reads).toEqual([null]);
  await page.getByTestId('knowledge-changes-next').click(); await expect(page.getByText('来源第二页', { exact: true })).toBeVisible();
  expect(state.reads).toEqual([null, first]); await expect(page.getByTestId('knowledge-changes-next')).toBeDisabled();
  await page.getByTestId('knowledge-changes-index').click(); await expect(page.getByTestId('knowledge-index-state')).toContainText('pending_processing');
  await expect(page.getByTestId('knowledge-index-enqueue')).toHaveCount(0); expect(state.writes).toHaveLength(0);
});
test('缺少目录精确权限不渲染入口或发起请求', async ({ page }) => {
  const state = await setup(page, false); await expect(page.getByTestId('knowledge-changes')).toHaveCount(0);
  await expect(page.getByTestId('index-grants-open')).toHaveCount(0); expect(state.grantWrites).toHaveLength(0);
  expect(state.reads).toHaveLength(0); expect(state.writes).toHaveLength(0);
});

// 受控 HTTP 只验证真实页面确认及 SDK 边界；收费授权与租户原子性由双库验收证明。
test('收费授权须显式确认并可按当前修订撤销', async ({ page }) => {
  const state = await setup(page, true, true); expect(state.grantWrites).toHaveLength(0);
  await page.getByTestId('index-grants-open').click();
  await expect(page.getByTestId('index-grants-create')).toBeEnabled(); expect(state.grantWrites).toHaveLength(0);
  await page.getByTestId('index-grants-create').click();
  await expect(page.locator('.el-message-box')).toContainText('不是金额硬限'); expect(state.grantWrites).toHaveLength(0);
  await page.locator('.el-message-box').getByRole('button').last().click();
  await expect.poll(() => state.grantWrites.length).toBe(1);
  const body = state.grantWrites[0].body;
  expect(Object.keys(body).sort()).toEqual(['clientRequestId','confirmPaidDispatch','documentLimit','expiresAtUtc','inputTokenLimitPerDocument','knowledgeBaseVersion'].sort());
  expect(body).toMatchObject({ confirmPaidDispatch: true, knowledgeBaseVersion: 1, documentLimit: 4, inputTokenLimitPerDocument: 100000 });
  expect(Date.parse(body.expiresAtUtc)).toBeGreaterThan(Date.now());
  await expect(page.getByTestId('index-grants-revoke')).toBeEnabled(); await page.getByTestId('index-grants-revoke').click();
  await expect(page.locator('.el-message-box')).toContainText('已发生或未知费用'); expect(state.grantWrites).toHaveLength(1);
  await page.locator('.el-message-box').getByRole('button').last().click();
  await expect.poll(() => state.grantWrites.length).toBe(2); expect(state.grantWrites[1].body).toEqual({ version: 1 });
  await expect(page.getByText('已撤销', { exact: true })).toBeVisible();
});

test('取消收费确认不创建授权', async ({ page }) => {
  const state = await setup(page, true, true); await page.getByTestId('index-grants-open').click();
  await expect(page.getByTestId('index-grants-create')).toBeEnabled(); await page.getByTestId('index-grants-create').click();
  await page.locator('.el-message-box').getByRole('button', { name: /取消|Cancel/ }).click();
  await expect(page.locator('.el-message-box')).not.toBeVisible();
  await expect(page.getByTestId('index-grants-create')).toBeEnabled(); expect(state.grantWrites).toHaveLength(0);
});
test('目录冲突清空旧来源，手动刷新从第一页开始', async ({ page }) => {
  const state = await setup(page); await page.getByTestId('knowledge-changes').click(); await expect(page.getByText('来源第一页', { exact: true })).toBeVisible();
  state.conflict(true); await page.getByTestId('knowledge-changes-next').click(); await expect(page.getByText('目录已变化，请刷新', { exact: true })).toBeVisible();
  await expect(page.getByText('来源第一页', { exact: true })).toHaveCount(0); expect(state.reads).toHaveLength(2);
  state.conflict(false); await page.getByTestId('knowledge-changes-refresh').click(); await expect(page.getByText('来源第一页', { exact: true })).toBeVisible();
  expect(state.reads).toEqual([null, first, null]); expect(state.writes).toHaveLength(0);
});
