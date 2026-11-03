import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => '01900000-0000-7000-8000-' + String(n).padStart(12, '0');
const [baseId, docId, versionId, sourceId] = [1, 2, 3, 4].map(id), now = '2026-10-28T00:00:00Z';
const original = '原文😀内容';
const location = { blockSequence: 0, kind: 'document', pageNumber: null, paragraphNumber: null, tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null, image: null };
async function setup(page) {
  let head = 0, conflict = false, writes = 0, forbidden = 0;
  let blocks = [{ id: id(5), excluded: false, separator: '', parts: [{ id: id(6), text: original, prefix: '', contentKind: 'original', anchorId: id(7),
    originalText: original, sourceOffset: 0, sourceLength: original.length, sourceLocation: location }] }];
  const revisions = [];
  const hash = () => (head ? head.toString(16) : 'b').repeat(64);
  const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
  const workspace = (block, revision) => {
    const prior = revision ? revisions.find(r => r.receipt.id === revision) : null;
    return { versionId, documentVersion: head + 2, sourceId, revisionId: prior ? prior.receipt.id : head ? id(30 + head) : null, headVersion: prior ? prior.receipt.headVersion : head,
      sourceHash: 'a'.repeat(64), payloadHash: prior ? prior.receipt.payloadHash : hash(), parserRuntimeId: 'managed:utf8-text-v1', contentType: 'text/plain',
      originalFileName: 'source.txt', blockIndex: block, totalBlocks: (prior?.blocks ?? blocks).length, isSourceView: false, block: (prior?.blocks ?? blocks)[block] };
  };
  function commit(reason, next) {
    const parentId = head ? id(30 + head) : null; head++; blocks = structuredClone(next);
    const receipt = { id: id(30 + head), versionId, sourceId, parentId, headVersion: head, documentVersion: head + 2, payloadHash: hash(), reason, createdAtUtc: now };
    revisions.unshift({ receipt, blocks: structuredClone(blocks) }); return receipt;
  }
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'edit-fixture', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', route => json(route, { id: id(20), username: 'editor', displayName: '校对编辑', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    sessionId: id(21), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false, permissions: ['identity.navigation.read', 'ai.knowledge_bases.read',
      'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.versions_read', 'ai.knowledge_corrections.read', 'ai.knowledge_corrections.edit', 'ai.knowledge_corrections.restore'] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'documents', parentId: null, routeName: 'ai-knowledge-documents', path: '/ai/knowledge-documents',
    componentKey: 'ai-knowledge-documents', title: '知识文档', caption: '知识文档', icon: 'document', order: 1, requiredPermission: 'ai.knowledge_documents.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => json(route, { activeTenantCount: 0, onlineSessionCount: 1, todayRequestCount: 0, todayErrorRate: 0, recentActivities: [], accessTrafficTrend: null, businessEntries: [] }));
  await page.route('**/api/v1/ai/knowledge-bases?*', route => json(route, { items: [{ id: baseId, name: '编辑验收库', description: null, isEnabled: true, dataClassification: 'internal',
    embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: now, updatedAtUtc: null, version: 1 }], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents?*`, route => json(route, { items: [{ id: docId, knowledgeBaseId: baseId, title: '人工操作不发布',
    description: null, status: 'draft', createdAtUtc: now, updatedAtUtc: null, version: 2 }], page: 1, pageSize: 20, total: 1 }));
  const version = { id: versionId, documentId: docId, versionNumber: 1, originalFileName: 'source.txt', contentType: 'text/plain', contentHash: 'a'.repeat(64), sizeBytes: Buffer.byteLength(original),
    processingStatus: 'pending_processing', createdAtUtc: now };
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents/${docId}/versions?*`, route => json(route, { items: [version], page: 1, pageSize: 20, total: 1 }));
  const root = `**/api/v1/ai/knowledge-bases/${baseId}/documents/${docId}/versions/${versionId}`;
  await page.route(root + '/corrections/preparations/latest', route => json(route, { id: id(10), versionId, documentVersion: 2, state: 'completed', attemptCount: 1, sourceId, failureCode: null, createdAtUtc: now, updatedAtUtc: now }));
  await page.route(root + '/corrections/workspace?*', route => { const q = new URL(route.request().url()).searchParams; return json(route, workspace(Number(q.get('block') ?? 0), q.get('revisionId'))); });
  await page.route(root + '/corrections/revisions*', route => {
    if (route.request().method() === 'GET') return json(route, { items: revisions.map(r => r.receipt), nextHeadVersion: null });
    writes++; const body = route.request().postDataJSON();
    if (conflict) { conflict = false; const next = structuredClone(blocks); next[0].parts[0].text = '另一维护已更新'; next[0].parts[0].contentKind = 'corrected'; commit('并发维护', next);
      return route.fulfill({ status: 409, contentType: 'application/problem+json', body: JSON.stringify({ status: 409, code: 'ai.knowledge.correction_conflict', title: 'Conflict' }) }); }
    expect(body.expected.headVersion).toBe(head); expect(body.expected.documentVersion).toBe(head + 2);
    const next = structuredClone(blocks), op = body.operation, index = next.findIndex(b => b.id === op.blockId);
    if (op.kind === 'replace') { const p = next[index].parts.find(p => p.id === op.partId); p.text = op.text; p.contentKind = 'corrected'; }
    else if (op.kind === 'append') next.splice(index + 1, 0, { id: id(15), excluded: false, separator: '\n', parts: [{ id: id(16), text: op.text, prefix: '\n',
      contentKind: 'supplement', anchorId: null, originalText: null, sourceOffset: null, sourceLength: null, sourceLocation: null }] });
    else if (op.kind === 'exclude') next[index].excluded = op.excluded;
    else throw Error('Unexpected fixture operation ' + op.kind);
    return json(route, commit(body.reason, next));
  });
  await page.route(root + '/corrections/restore', route => {
    writes++; const body = route.request().postDataJSON(); expect(body.expected.headVersion).toBe(head);
    const target = revisions.find(r => r.receipt.id === body.targetRevisionId); expect(target).toBeTruthy(); return json(route, commit(body.reason, target.blocks));
  });
  await page.route(root + '/source', route => route.fulfill({ status: 200, contentType: 'application/octet-stream', body: Buffer.from(original) }));
  page.on('request', request => { if (/\/(?:index|publications|embeddings)(?:\/|\?|$)/.test(new URL(request.url()).pathname)) forbidden++; });
  await page.goto('/ai/knowledge-documents'); await page.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: '编辑验收库', exact: true }).click();
  await page.getByTestId('knowledge-versions').click(); await page.getByTestId('knowledge-correction').click();
  return { conflict: () => { conflict = true; }, writes: () => writes, forbidden: () => forbidden };
}
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`更正冲突保稿、历史恢复和补写排除 ${viewport.width}`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport); const errors = []; page.on('pageerror', e => errors.push(e.message));
    const state = await setup(page), dialog = page.getByTestId('knowledge-correction-dialog');
    const input = (testId, value) => dialog.getByTestId(testId).fill(value);
    await dialog.getByTestId('correction-edit-part').click(); await input('correction-draft-text', '人工更正'); await input('correction-reason', '识别校对');
    await dialog.getByTestId('correction-save-draft').focus(); await page.keyboard.press('Enter'); await expect(dialog.getByTestId('correction-saved')).toContainText('尚未发布');
    await expect(dialog.getByTestId('correction-original')).toHaveText(original); await expect(dialog.getByTestId('correction-text')).toHaveText('人工更正');
    await dialog.getByTestId('correction-edit-part').click(); await input('correction-draft-text', '冲突草稿保留'); await input('correction-reason', '冲突核对');
    state.conflict(); await dialog.getByTestId('correction-save-draft').click(); await expect(dialog.getByTestId('correction-conflict')).toBeVisible();
    await expect(dialog.getByTestId('correction-draft-text')).toHaveValue('冲突草稿保留');
    await dialog.getByTestId('correction-refresh').click(); await expect(dialog.getByTestId('correction-text')).toHaveText('另一维护已更新');
    await expect(dialog.getByTestId('correction-draft-text')).toHaveValue('冲突草稿保留');
    await dialog.getByTestId('correction-save-draft').click(); await expect(dialog.getByTestId('correction-text')).toHaveText('冲突草稿保留');
    await dialog.getByTestId('correction-history').click(); await dialog.getByRole('button', { name: '预览修订 1', exact: true }).click();
    await expect(dialog.getByTestId('correction-history-view')).toBeVisible(); await expect(dialog.getByTestId('correction-edit-part')).toHaveCount(0);
    await input('correction-reason', '恢复首稿'); await dialog.getByTestId('correction-restore').click(); await expect(dialog.getByTestId('correction-text')).toHaveText('人工更正');
    await dialog.getByTestId('correction-append').click(); await input('correction-draft-text', '明确人工补写'); await input('correction-reason', '补充说明');
    await dialog.getByTestId('correction-save-draft').click(); await expect(dialog.getByTestId('correction-page')).toHaveText('1 / 2');
    await dialog.getByTestId('correction-next').click(); await expect(dialog.getByTestId('correction-text')).toHaveText('明确人工补写'); await expect(dialog.getByTestId('correction-source')).toHaveCount(0);
    await input('correction-reason', '排除噪声'); await dialog.getByTestId('correction-exclude').click(); await expect(dialog).toContainText('已排除，后续索引不使用');
    expect(state.writes()).toBe(6); expect(state.forbidden()).toBe(0); expect(errors).toEqual([]);
    expect(await dialog.evaluate(el => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
    expect((await new AxeBuilder({ page }).include('.knowledge-correction-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: testInfo.outputPath('correction-editing.png'), fullPage: true });
  });
}
