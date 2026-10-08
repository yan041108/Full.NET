import { expect, test } from '@playwright/test';
const id = n => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const chatId = id(1), messageId = id(2), userId = id(3), modelId = id(4);
const feedbackPath = `/api/v1/ai/chat/sessions/${chatId}/messages/${messageId}/feedback`;
// 浏览器验证页面版本恢复和精确权限；反馈 HTTP/双库/原生闭包另由集成测试证明。
async function setup(page, writable = true) {
  let version = 0, writes = 0, conflict = false, revoked = false;
  let row = null;
  const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'feedback-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', route => json(route, { id: userId, username: 'feedback-test', displayName: '反馈测试', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    permissions: ['identity.navigation.read', 'ai.chat.sessions.read', 'ai.knowledge_answer_feedback.read', 'ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.index_read', ...(writable ? ['ai.knowledge_answer_feedback.submit', 'ai.knowledge_answer_feedback.withdraw'] : [])], sessionId: id(5), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'chat', parentId: null, routeName: 'ai-chat', path: '/ai/chat', componentKey: 'ai-chat', title: 'AI聊天', caption: 'AI聊天', icon: 'chat', order: 1, requiredPermission: 'ai.chat.sessions.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  const chat = { id: chatId, tenantId: null, ownerUserId: userId, modelConfigId: modelId, modelName: 'fixture', title: '来源回答', messageCount: 1, lastMessageAtUtc: null, createdAtUtc: '2026-10-09T00:00:00Z', updatedAtUtc: null, version: 1 };
  await page.route('**/api/v1/ai/chat/sessions?*', route => json(route, { items: [chat], page: 1, pageSize: 50, total: 1 }));
  await page.route(`**/api/v1/ai/chat/sessions/${chatId}`, route => json(route, { ...chat, isGenerating: false, messages: [{ id: messageId, sessionId: chatId, roleKey: 'assistant', statusKey: 'completed', content: '来源回答', promptTokens: null, completionTokens: null, createdAtUtc: chat.createdAtUtc, isKnowledgeAnswer: true, knowledgeUnavailableCode: null, citations: [] }] }));
  await page.route(url => url.pathname === feedbackPath || url.pathname === `${feedbackPath}/state`, async route => {
    if (route.request().url().endsWith('/state')) return row ? json(route, { messageId, statusKey: row.statusKey, version }) : json(route, { status: 404, code: 'ai.knowledge.feedback_not_found', title: 'Unavailable', type: 'about:blank' }, 404);
    if (revoked && route.request().method() === 'GET') return json(route, { status: 404, code: 'ai.knowledge.feedback_not_found', title: 'Unavailable', type: 'about:blank' }, 404);
    if (route.request().method() === 'GET') return row ? json(route, row) : json(route, { status: 404, code: 'ai.knowledge.feedback_not_found', title: 'Unavailable', type: 'about:blank' }, 404);
    writes++;
    if (conflict) { conflict = false; version = 4; row = { id: id(6), messageId, ratingKey: 'helpful', reasonKey: 'accurate', citationIds: [], statusKey: 'pending_review', version, updatedAtUtc: chat.createdAtUtc }; return json(route, { status: 409, code: 'ai.knowledge.feedback_conflict', title: 'Conflict', type: 'about:blank' }, 409); }
    if (route.request().method() === 'PUT') { const body = route.request().postDataJSON(); expect(body.version).toBe(version); row = { id: id(6), messageId, ...body, statusKey: 'pending_review', version: ++version, updatedAtUtc: chat.createdAtUtc }; }
    else { expect(new URL(route.request().url()).searchParams.get('version')).toBe(String(version)); row = { ...row, ratingKey: null, reasonKey: null, citationIds: [], statusKey: 'withdrawn', version: ++version }; }
    return json(route, row);
  });
  await page.goto('/ai/chat'); await expect(page.getByTestId('feedback-open')).toBeVisible();
  return { conflict: () => { conflict = true; }, revoke: () => { revoked = true; }, writes: () => writes };
}
test('回答反馈冲突保留选择，显式刷新后提交及撤回', async ({ page }) => {
  const state = await setup(page); await page.getByTestId('feedback-open').click();
  await page.getByTestId('feedback-rating').selectOption('unhelpful'); await page.getByTestId('feedback-reason').selectOption('other'); state.conflict();
  await page.getByTestId('feedback-save').click(); await expect(page.getByTestId('feedback-problem')).toContainText('保留选择'); await expect(page.getByTestId('feedback-save')).toBeDisabled();
  await page.getByTestId('feedback-refresh').click(); await expect(page.getByTestId('feedback-save')).toBeEnabled(); await expect(page.getByTestId('feedback-reason')).toHaveValue('other');
  await page.getByTestId('feedback-save').click(); await expect(page.getByText('反馈待审核', { exact: true })).toBeVisible();
  await page.getByTestId('feedback-withdraw').click(); await expect(page.getByText('反馈已撤回', { exact: true })).toBeVisible(); expect(state.writes()).toBe(3);
});
test('反馈只读权限不渲染写操作', async ({ page }) => {
  const state = await setup(page, false); await page.getByTestId('feedback-open').click(); await expect(page.getByTestId('feedback-save')).toHaveCount(0); await expect(page.getByTestId('feedback-withdraw')).toHaveCount(0); expect(state.writes()).toBe(0);
});
test('来源撤权后刷新只取版本状态，仍可清除本人反馈', async ({ page }) => {
  const state = await setup(page); await page.getByTestId('feedback-open').click(); await page.getByTestId('feedback-save').click();
  await expect(page.getByText('反馈待审核', { exact: true })).toBeVisible(); state.revoke(); await page.getByTestId('feedback-refresh').click();
  await expect(page.getByTestId('feedback-save')).toBeDisabled(); await expect(page.getByTestId('feedback-withdraw')).toBeEnabled();
  await page.getByTestId('feedback-withdraw').click(); await expect(page.getByText('反馈已撤回', { exact: true })).toBeVisible(); await expect(page.getByTestId('feedback-save')).toBeDisabled(); expect(state.writes()).toBe(2);
});
