import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = n => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const base = id(1), doc = id(2), second = id(3), version = id(4), user = id(5), proposalId = id(6);
const now = '2026-10-28T00:00:00Z', expiry = '2099-01-01T00:00:00Z';

// 真实 Vue/Edge 的受控 HTTP 验证；数据库锁、来源证明和真实授权另由 Integration 覆盖。
async function setup(page, { state = 'approved', reviewer = false, manage = true } = {}) {
  const batches = [], requests = [], decisions = [], reviews = [], withdrawals = []; let failRequest = false, failPublish = false, denyProposal = false, failBatch = false, conflictReview = false;
  let head = { documentId: doc, publicationId: state === 'published' ? proposalId : null, version: state === 'published' ? 1 : 0 };
  let proposal = { id: proposalId, documentId: doc, versionId: version, generationId: id(7), stateKey: state,
    snapshotHash: 'A'.repeat(64), policyVersion: 1, expectedHeadVersion: 0, version: 2,
    effectiveFromUtc: now, effectiveToUtc: expiry, actorUserId: reviewer ? id(8) : user,
    reviewerUserId: reviewer ? null : id(8), createdAtUtc: now, reviewedAtUtc: reviewer ? null : now };
  const rows = [doc, second].map((value, index) => ({ id: value, title: index ? '第二份来源' : '正式来源与新稿', folderId: null,
    tags: [], responsibleUserId: null, version: 0, documentVersion: 1, candidateVersionId: version, publicationId: null,
    pendingPublicationId: state ? proposalId : null, publicationState: 'draft', headVersion: 0 }));
  const json = (route, value, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'publication-test', tokenType: 'Bearer', expiresAtUtc: expiry }));
  await page.route('**/api/v1/me', route => json(route, { id: user, username: 'publication', displayName: '知识发布验收', tenantId: null,
    actorScope: 'host', scope: 'host', isSuperAdministrator: false, sessionId: id(9), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false,
    permissions: ['identity.navigation.read', 'ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.index_read',
      'ai.knowledge_publications.read', 'ai.knowledge_publications.request', 'ai.knowledge_publications.review', 'ai.knowledge_publications.publish', 'ai.knowledge_publications.withdraw',
      ...(manage ? ['ai.knowledge_directories.manage'] : [])] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'knowledge', parentId: null, routeName: 'ai-knowledge-documents', path: '/ai/knowledge-documents', componentKey: 'ai-knowledge-documents', title: '知识文件', caption: '授权文件', icon: 'document', order: 1, requiredPermission: 'ai.knowledge_documents.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => json(route, { activeTenantCount: 0, onlineSessionCount: 1, todayRequestCount: 0, todayErrorRate: 0, recentActivities: [], accessTrafficTrend: null, businessEntries: [] }));
  const knowledge = { id: base, name: '发布验收知识库', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: id(10), embeddingModelVersion: 1,
    generationModelConfigId: null, generationModelVersion: null, createdAtUtc: now, updatedAtUtc: null, version: 1 };
  await page.route('**/api/v1/ai/knowledge-bases?*', route => json(route, { items: [knowledge], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${base}`, route => json(route, knowledge));
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/documents?*`, route => json(route, { items: [], page: 1, pageSize: 20, total: 0 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/folders`, route => json(route, []));
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/directory?*`, route => json(route, { items: rows, page: 1, pageSize: 20, total: rows.length }));
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/directory/batch`, route => {
    const body = route.request().postDataJSON(); batches.push(body); if (failBatch) { failBatch = false; return route.abort('failed'); }
    return json(route, body.items.map(item => ({ documentId: item.documentId, requestKey: item.requestKey,
      succeeded: batches.length > 1 || item.documentId === doc, version: item.documentId === doc || batches.length > 1 ? 1 : null,
      errorCode: batches.length === 1 && item.documentId === second ? 'ai.knowledge.version_conflict' : null })));
  });
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/documents/*/publications/head`, route => json(route, head));
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/documents/*/publications/${proposalId}`, route => denyProposal ? json(route, { status: 403, title: 'Unavailable', code: 'authorization.permission_denied' }, 403) : json(route, proposal));
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/documents/*/publications`, route => {
    requests.push(route.request().postDataJSON()); if (failRequest) { failRequest = false; return route.abort('failed'); }
    proposal = { ...proposal, stateKey: 'pending', version: 1 }; return json(route, proposal, 201);
  });
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/documents/*/publications/${proposalId}/publish`, route => {
    decisions.push(route.request().postDataJSON()); proposal = { ...proposal, stateKey: 'published', version: 3 }; head = { ...head, publicationId: proposalId, version: 1 };
    if (failPublish) { failPublish = false; return route.abort('failed'); } return json(route, proposal);
  });
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/documents/*/publications/${proposalId}/review`, route => {
    const body = route.request().postDataJSON(); reviews.push(body);
    if (conflictReview) { proposal = { ...proposal, stateKey: 'rejected', version: 3 }; return json(route, { status: 409, title: 'Conflict', code: 'ai.knowledge.version_conflict' }, 409); }
    proposal = { ...proposal, stateKey: body.approve ? 'approved' : 'rejected', version: 3 }; return json(route, proposal);
  });
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/documents/*/publications/withdraw`, route => {
    withdrawals.push(route.request().postDataJSON()); head = { ...head, publicationId: null, version: 2 }; return json(route, head);
  });
  await page.goto('/#/ai/knowledge-documents'); await page.getByTestId('knowledge-base-select').click();
  await page.getByRole('option', { name: '发布验收知识库', exact: true }).click(); await page.getByTestId('knowledge-governance-open').click();
  await expect(page.getByTestId('knowledge-governance-publication')).toHaveCount(2);
  return { batches, requests, decisions, reviews, withdrawals, conflictReview: () => { conflictReview = true; }, denyProposal: () => { denyProposal = true; }, failBatch: () => { failBatch = true; }, failRequest: () => { failRequest = true; }, failPublish: () => { failPublish = true; } };
}
async function openPublication(page) {
  await page.getByTestId('knowledge-governance-publication').first().click();
  await expect(page.getByTestId('knowledge-publication')).toBeVisible();
  // 对比度与截图读取最终弹窗颜色，不把进入动画的临时透明度当成静态样式。
  await page.getByTestId('knowledge-publication').evaluate(async element => {
    const animations = [];
    for (let node = element; node; node = node.parentElement) animations.push(...node.getAnimations());
    await Promise.all(animations.map(animation => animation.finished.catch(() => {})));
  });
}
async function confirm(page) { await page.locator('.el-message-box').getByRole('button', { name: '确定', exact: true }).click(); }

test('批量部分失败只重试原失败条目及原请求身份', async ({ page }) => {
  const f = await setup(page);
  await page.getByTestId('knowledge-governance').locator('.el-table__header-wrapper .el-checkbox').click();
  await page.getByTestId('knowledge-governance-batch').click();
  await expect(page.getByTestId('knowledge-governance-results')).toContainText('成功');
  await expect(page.getByTestId('knowledge-governance-retry')).toBeEnabled();
  f.failBatch(); await page.getByTestId('knowledge-governance-retry').click();
  await expect.poll(() => f.batches.length).toBe(2);
  await expect(page.getByTestId('knowledge-governance-results')).toContainText('client.outcome_unknown');
  await expect(page.getByTestId('knowledge-governance-results')).toContainText(`${doc} · 成功`);
  await page.getByTestId('knowledge-governance-retry').click();
  await expect.poll(() => f.batches.length).toBe(3); expect(f.batches[2]).toEqual(f.batches[1]);
  expect(f.batches[0].items).toHaveLength(2); expect(f.batches[1].items).toEqual([f.batches[0].items[1]]);
  await expect(page.getByTestId('knowledge-governance-results')).not.toContainText('失败');
});
test('请求结果不明沿用冻结身份，原维护者不能自行审核', async ({ page }) => {
  const f = await setup(page, { state: '' }); await openPublication(page); f.failRequest();
  await page.getByTestId('knowledge-publication-request').click(); await confirm(page);
  await expect.poll(() => f.requests.length).toBe(1); await expect(page.getByTestId('knowledge-publication-request')).toBeEnabled();
  await page.getByTestId('knowledge-publication-request').click(); await confirm(page);
  await expect.poll(() => f.requests.length).toBe(2); expect(f.requests[0]).toEqual(f.requests[1]);
  await expect(page.getByTestId('knowledge-publication')).toContainText('待独立审核');
  await expect(page.getByTestId('knowledge-publication-approve')).toHaveCount(0);
});
test('发布响应丢失后禁止再派发，读取权威状态后展示已发布', async ({ page }) => {
  const f = await setup(page); await openPublication(page); f.failPublish();
  await page.getByTestId('knowledge-publication').locator('.el-checkbox').click();
  await page.getByTestId('knowledge-publication-publish').click(); await confirm(page);
  await expect(page.getByTestId('knowledge-publication')).toContainText('决定结果未确认');
  await expect(page.getByTestId('knowledge-publication-publish')).toBeDisabled();
  await page.getByTestId('knowledge-publication').getByRole('button', { name: '刷新', exact: true }).click();
  await expect(page.getByTestId('knowledge-publication')).toContainText('已发布');
  await expect(page.getByTestId('knowledge-publication-publish')).toHaveCount(0); expect(f.decisions).toHaveLength(1);
});
test('提案读取被拒绝后清除旧摘要及决定入口', async ({ page }) => {
  const f = await setup(page); await openPublication(page);
  await expect(page.getByTestId('knowledge-publication')).toContainText('A'.repeat(64)); f.denyProposal();
  await page.getByTestId('knowledge-publication').getByRole('button', { name: '刷新', exact: true }).click();
  await expect(page.getByTestId('knowledge-publication')).toContainText('操作未完成');
  await expect(page.getByTestId('knowledge-publication')).not.toContainText('A'.repeat(64));
  await expect(page.getByTestId('knowledge-publication-publish')).toHaveCount(0); expect(f.decisions).toHaveLength(0);
});
test('独立审核人明确驳回，冲突后刷新权威状态再操作', async ({ page }) => {
  const f = await setup(page, { state: 'pending', reviewer: true }); await openPublication(page);
  await page.getByTestId('knowledge-publication').getByRole('checkbox').focus(); await page.keyboard.press('Space');
  f.conflictReview(); await page.getByTestId('knowledge-publication').getByRole('button', { name: '拒绝提案', exact: true }).click(); await confirm(page);
  await expect(page.getByTestId('knowledge-publication')).toContainText('决定结果未确认');
  await expect(page.getByTestId('knowledge-publication-approve')).toBeDisabled();
  await page.getByTestId('knowledge-publication').getByRole('button', { name: '刷新', exact: true }).click();
  await expect(page.getByTestId('knowledge-publication')).toContainText('已拒绝');
  await expect(page.getByTestId('knowledge-publication-approve')).toHaveCount(0);
  expect(f.reviews).toEqual([{ approve: false, version: 2, snapshotHash: 'A'.repeat(64) }]);
});
test('撤回提交原发布身份及头版本，关闭确认窗不写入', async ({ page }) => {
  const f = await setup(page, { state: 'published' }); await openPublication(page);
  const withdraw = page.getByTestId('knowledge-publication').getByRole('button', { name: '撤回当前发布', exact: true });
  await withdraw.click(); await page.locator('.el-message-box').getByRole('button', { name: '取消', exact: true }).click();
  expect(f.withdrawals).toHaveLength(0);
  await withdraw.click(); await confirm(page); await expect(withdraw).toHaveCount(0);
  expect(f.withdrawals).toEqual([{ expectedHeadVersion: 1, publicationId: proposalId }]);
  await expect(page.getByTestId('knowledge-publication')).not.toContainText('A'.repeat(64));
});
test('切换文档后迟到提案响应不能恢复旧正文及发布入口', async ({ page }) => {
  await setup(page); await openPublication(page);
  let release, started; const waiting = new Promise(resolve => { started = resolve; });
  const gate = new Promise(resolve => { release = resolve; });
  await page.route(`**/api/v1/ai/knowledge-bases/${base}/documents/${doc}/publications/${proposalId}`, async route => {
    started(); await gate;
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ id: proposalId, documentId: doc, stateKey: 'approved', snapshotHash: 'LATE_OLD_DOCUMENT', actorUserId: user }) }).catch(() => {});
  });
  await page.getByTestId('knowledge-publication').getByRole('button', { name: '刷新', exact: true }).click(); await waiting;
  await page.getByRole('dialog').filter({ has: page.getByTestId('knowledge-publication') }).getByRole('button', { name: '关闭', exact: true }).click();
  await page.getByTestId('knowledge-governance-publication').nth(1).click(); release();
  await expect(page.getByTestId('knowledge-publication')).toContainText('第二份来源');
  await expect(page.getByTestId('knowledge-publication')).toContainText('A'.repeat(64));
  await expect(page.getByTestId('knowledge-publication')).not.toContainText('LATE_OLD_DOCUMENT');
});
test('移动视口显示独立审核及键盘焦点，缺少管理权限无批量写入口', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 }); await setup(page, { state: 'pending', reviewer: true, manage: false });
  await expect(page.getByTestId('knowledge-governance-batch')).toHaveCount(0); await openPublication(page);
  const approve = page.getByTestId('knowledge-publication-approve'); await expect(approve).toBeDisabled();
  const checkbox = page.getByTestId('knowledge-publication').getByRole('checkbox'); await checkbox.focus(); await page.keyboard.press('Space'); await expect(approve).toBeEnabled();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('knowledge-publication-mobile.png'), fullPage: true });
  expect((await new AxeBuilder({ page }).include('[data-testid="knowledge-publication"]').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
  await page.screenshot({ path: testInfo.outputPath('knowledge-publication-mobile.png'), fullPage: true });
});
