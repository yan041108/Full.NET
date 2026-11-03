import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => '01900000-0000-7000-8000-' + String(n).padStart(12, '0');
const [baseId, docId, versionId, oldSource, revisionId, newSource] = [1, 2, 3, 4, 5, 6].map(id);
const now = '2026-10-28T00:00:00Z';
const location = { blockSequence: 1, kind: 'pdf_page', pageNumber: 2, paragraphNumber: null, tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null, image: null };
function pdf() {
  const objects = ['<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>'];
  for (const color of ['1 0 0', '0 0 1']) {
    const stream = `${color} rg 0 0 240 320 re f`;
    const index = objects.length + 1;
    objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 320] /Contents ${index + 1} 0 R >>`, `<< /Length ${Buffer.byteLength(stream)} >>\nstream\n${stream}\nendstream`);
  }
  let body = '%PDF-1.7\n'; const offsets = [0];
  objects.forEach((object, index) => { offsets.push(Buffer.byteLength(body)); body += `${index + 1} 0 obj\n${object}\nendobj\n`; });
  const xref = Buffer.byteLength(body);
  body += `xref\n0 ${offsets.length}\n0000000000 65535 f \n` + offsets.slice(1).map(offset => String(offset).padStart(10, '0') + ' 00000 n \n').join('');
  return Buffer.from(body + `trailer\n<< /Size ${offsets.length} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`);
}
async function setup(page) {
  let polls = 0; let denied = false; const requests = [];
  const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
  const task = (state = 'completed', sourceId = oldSource, taskId = id(10)) => ({ id: taskId, versionId, documentVersion: 3, state, attemptCount: state === 'queued' ? 0 : 1,
    sourceId, failureCode: null, createdAtUtc: now, updatedAtUtc: now });
  const part = { id: id(12), text: '人工更正的当前片段', prefix: '', contentKind: 'corrected', anchorId: id(13), originalText: '第二页完整原识别上下文', sourceOffset: 0, sourceLength: '第二页完整原识别上下文'.length, sourceLocation: location };
  const workspace = (block = 0, sourceId) => ({ versionId, documentVersion: 3, sourceId: sourceId ?? oldSource, revisionId: sourceId ? null : revisionId,
    headVersion: sourceId ? 0 : 1, sourceHash: 'a'.repeat(64), payloadHash: (sourceId ? 'c' : 'b').repeat(64), parserRuntimeId: 'fixture:pdf-v1',
    contentType: 'application/pdf', originalFileName: 'source.pdf', blockIndex: block, totalBlocks: 2, isSourceView: !!sourceId,
    block: { id: id(11 + block), excluded: false, separator: '', parts: block === 1
      ? [{ ...part, id: id(14), contentKind: 'supplement', text: '明确人工补写', anchorId: null, originalText: null, sourceOffset: null, sourceLength: null, sourceLocation: null }]
      : [{ ...part, ...(sourceId ? { contentKind: 'original', text: part.originalText } : {}) }] } });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'workspace-fixture', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', route => json(route, { id: id(20), username: 'workspace', displayName: '校对工作台', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, sessionId: id(21), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false,
    permissions: ['identity.navigation.read', 'ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read',
      'ai.knowledge_documents.versions_read', 'ai.knowledge_corrections.read', 'ai.knowledge_corrections.prepare'] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'documents', parentId: null, routeName: 'ai-knowledge-documents', path: '/ai/knowledge-documents',
    componentKey: 'ai-knowledge-documents', title: '知识文档', caption: '知识文档', icon: 'document', order: 1, requiredPermission: 'ai.knowledge_documents.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => json(route, { activeTenantCount: 0, onlineSessionCount: 1, todayRequestCount: 0, todayErrorRate: 0, recentActivities: [], accessTrafficTrend: null, businessEntries: [] }));
  await page.route('**/api/v1/ai/knowledge-bases?*', route => json(route, { items: [{ id: baseId, name: '本地准备库', description: null, isEnabled: true, dataClassification: 'internal',
    embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: now, updatedAtUtc: null, version: 1 }], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents?*`, route => json(route, { items: [{ id: docId, knowledgeBaseId: baseId, title: '来源与人工头独立',
    description: null, status: 'draft', createdAtUtc: now, updatedAtUtc: null, version: 3 }], page: 1, pageSize: 20, total: 1 }));
  const version = { id: versionId, documentId: docId, versionNumber: 1, originalFileName: 'source.pdf', contentType: 'application/pdf', contentHash: 'a'.repeat(64),
    sizeBytes: pdf().byteLength, processingStatus: 'pending_processing', createdAtUtc: now };
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents/${docId}/versions?*`, route => json(route, { items: [version], page: 1, pageSize: 20, total: 1 }));
  const root = `**/api/v1/ai/knowledge-bases/${baseId}/documents/${docId}/versions/${versionId}`;
  await page.route(root + '/corrections/workspace?*', route => { const query = new URL(route.request().url()).searchParams; return json(route, workspace(Number(query.get('block') ?? 0), query.get('sourceId') ?? undefined)); });
  await page.route(root + '/corrections/preparations/latest', route => json(route, task()));
  await page.route(root + '/corrections/preparations/' + id(15), route => json(route, ++polls === 1 ? task('running', null, id(15)) : task('completed', newSource, id(15))));
  await page.route(root + '/corrections/prepare', route => {
    requests.push(route.request().postDataJSON());
    return denied ? route.fulfill({ status: 403, contentType: 'application/problem+json', body: JSON.stringify({ status: 403, code: 'authorization.permission_denied', title: 'Forbidden' }) })
      : json(route, task('queued', null, id(15)), 202);
  });
  await page.route(root + '/source', route => route.fulfill({ status: 200, contentType: 'application/octet-stream', body: pdf() }));
  await page.goto('/ai/knowledge-documents?knowledgeBaseId=' + baseId);
  await page.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: '本地准备库', exact: true }).click();
  await page.getByTestId('knowledge-versions').click(); await page.getByTestId('knowledge-correction').click();
  return { requests, denyPrepare: () => { denied = true; } };
}
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`校对来源准备、独立预览及真实 PDF 位置 ${viewport.width}`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport); const errors = [];
    page.on('pageerror', error => errors.push(error.message)); page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    const state = await setup(page); const dialog = page.getByTestId('knowledge-correction-dialog');
    await expect(dialog.getByTestId('correction-original')).toHaveText('第二页完整原识别上下文');
    await expect(dialog.getByTestId('correction-text')).toHaveText('人工更正的当前片段');
    await dialog.getByTestId('correction-next').click(); await expect(dialog.getByTestId('correction-text')).toHaveText('明确人工补写');
    await expect(dialog.getByTestId('correction-source')).toHaveCount(0);
    await dialog.getByTestId('correction-prepare').click(); await expect(dialog.getByTestId('correction-state')).toHaveText('等待 Worker 准备');
    await expect(dialog.getByTestId('correction-source-view')).toBeVisible({ timeout: 15000 });
    expect(state.requests).toHaveLength(1); expect(Object.keys(state.requests[0]).sort()).toEqual(['documentVersion', 'requestKey']); expect(state.requests[0].documentVersion).toBe(3);
    await expect(dialog.getByTestId('correction-text')).toHaveText('第二页完整原识别上下文');
    await dialog.getByTestId('correction-current').focus(); await page.keyboard.press('Enter');
    await expect(dialog.getByTestId('correction-text')).toHaveText('人工更正的当前片段');
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('correction-workspace.png'), fullPage: true });
    expect((await new AxeBuilder({ page }).include('.knowledge-correction-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await dialog.getByTestId('correction-source').click(); const source = page.getByTestId('knowledge-source-dialog');
    await source.getByTestId('knowledge-source-read').click(); await expect(source.getByTestId('knowledge-pdf-page')).toHaveText('2 / 2');
    const canvas = source.getByTestId('knowledge-pdf-canvas');
    await expect.poll(() => canvas.evaluate(c => Array.from(c.getContext('2d').getImageData(5, 5, 1, 1).data))).toEqual([0, 0, 255, 255]);
    expect(errors).toEqual([]);
    await source.getByRole('button', { name: '取消', exact: true }).click();
    state.denyPrepare(); await dialog.getByTestId('correction-prepare').click();
    await expect(dialog.getByTestId('correction-error')).toBeVisible(); await expect(dialog.getByTestId('correction-text')).toHaveCount(0);
    await expect(dialog.getByTestId('correction-original')).toHaveCount(0); await expect(source).toHaveCount(0);
    await page.getByRole('dialog').filter({ has: dialog }).getByRole('button', { name: '关闭', exact: true }).click();
    await expect(dialog).toHaveCount(0); await expect(canvas).toHaveCount(0);
  });
}
