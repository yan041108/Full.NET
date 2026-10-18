import { expect, test } from '@playwright/test';

const id = n => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const sample = { languageTag: 'zh-CN', question: '人工改写问题', expectedAnswer: '人工改写参考答案', evidence: '人工改写公开依据', shouldRefuse: false };

async function setup(page, allowed = true) {
  const prepares = [], admissions = [];
  const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'evaluation-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', route => json(route, { id: id(1), username: 'evaluation-test', displayName: '评估测试', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    permissions: ['identity.navigation.read', 'ai.chat.sessions.read', 'ai.knowledge_answer_feedback.review_read', ...(allowed ? ['ai.knowledge_answer_feedback.evaluation_prepare', 'ai.knowledge_answer_feedback.evaluation_admit'] : [])],
    sessionId: id(2), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'chat', parentId: null, routeName: 'ai-chat', path: '/ai/chat', componentKey: 'ai-chat', title: 'AI聊天', caption: 'AI聊天', icon: 'chat', order: 1, requiredPermission: 'ai.chat.sessions.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/ai/chat/sessions?*', route => json(route, { items: [], page: 1, pageSize: 50, total: 0 }));
  await page.route('**/api/v1/ai/knowledge-answer-feedback/reviews*', route => json(route, { items: [{ feedbackId: id(3), ratingKey: 'unhelpful', reasonKey: 'unsupported', citationCount: 1, feedbackVersion: 2, reviewVersion: 3, reviewStateKey: 'approved' }], nextCursor: null }));
  await page.route(`**/api/v1/ai/knowledge-answer-feedback/${id(3)}/evaluation-candidate`, route => {
    prepares.push(route.request().postDataJSON());
    return json(route, { candidateToken: 'controlled-encrypted-token', sampleHash: 'a'.repeat(64), expiresAtUtc: new Date(Date.now() + 900000).toISOString() });
  });
  await page.route('**/api/v1/ai/knowledge-answer-feedback/evaluation-admissions', route => {
    admissions.push(route.request().postDataJSON());
    return json(route, { formatVersion: 1, caseKey: 'b'.repeat(64), sampleHash: 'a'.repeat(64), sample, admittedAtUtc: new Date().toISOString() });
  });
  await page.goto('/#/ai/chat'); await page.getByTestId('feedback-review-open').click();
  return { prepares, admissions };
}

async function prepare(page) {
  await page.getByTestId('feedback-evaluation-open').click();
  await page.getByTestId('evaluation-question').fill(sample.question);
  await page.getByTestId('evaluation-answer').fill(sample.expectedAnswer);
  await page.getByTestId('evaluation-evidence').fill(sample.evidence);
  await page.getByTestId('evaluation-prepare').click();
  await expect(page.getByTestId('evaluation-admit')).toBeEnabled();
}

// 真实页面与 SDK 消费受控 HTTP；不读取生产原文、不调用模型，完整来源资格另由双库验证。
test('人工改写样例按精确版本准备，二次确认后下载', async ({ page }) => {
  const state = await setup(page); await prepare(page);
  expect(state.prepares).toEqual([{ feedbackVersion: 2, reviewVersion: 3, sample }]); expect(state.admissions).toHaveLength(0);
  await page.getByTestId('evaluation-admit').click();
  await expect(page.locator('.el-message-box')).toContainText('a'.repeat(64)); expect(state.admissions).toHaveLength(0);
  const pending = page.waitForEvent('download'); await page.locator('.el-message-box').getByRole('button').last().click();
  const download = await pending; expect(download.suggestedFilename()).toBe(`evaluation-${'b'.repeat(64)}.json`);
  const stream = await download.createReadStream(); const chunks = []; for await (const chunk of stream) chunks.push(chunk);
  const payload = JSON.parse(Buffer.concat(chunks).toString('utf8'));
  expect(payload.sample).toEqual(sample); expect(payload.formatVersion).toBe(1);
  expect(state.admissions).toEqual([{ candidateToken: 'controlled-encrypted-token', confirmManualRedaction: true }]);
  await expect(page.getByTestId('evaluation-admit')).toBeDisabled();
});

test('编辑人工样例立即废弃旧候选', async ({ page }) => {
  const state = await setup(page); await prepare(page);
  await page.getByTestId('evaluation-question').fill('另一份人工问题');
  await expect(page.getByTestId('evaluation-admit')).toBeDisabled(); expect(state.admissions).toHaveLength(0);
});

test('缺少评估权限不展示准备入口或发送样例', async ({ page }) => {
  const state = await setup(page, false); await expect(page.getByTestId('feedback-evaluation-open')).toHaveCount(0);
  expect(state.prepares).toHaveLength(0); expect(state.admissions).toHaveLength(0);
});
