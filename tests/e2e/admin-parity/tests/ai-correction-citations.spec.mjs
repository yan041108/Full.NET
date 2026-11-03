import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = n => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const [chatId, messageId, userId, modelId, baseId, docId, versionId] = [1, 2, 3, 4, 5, 6, 7].map(id);
// 页面使用明确的 HTTP 替身；画布由真实 PDF Worker 绘制，后端来源证明另由双库集成测试验收。
function pdf() {
  const objects = ['<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Count 3 /Kids [3 0 R 5 0 R 7 0 R] >>'];
  for (const [index, color] of ['1 0 0', '0 1 0', '0 0 1'].entries()) {
    const stream = `${color} rg 0 0 240 320 re f`;
    objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 320] /Contents ${4 + index * 2} 0 R >>`);
    objects.push(`<< /Length ${Buffer.byteLength(stream)} >>\nstream\n${stream}\nendstream`);
  }
  let body = '%PDF-1.7\n'; const offsets = [0];
  for (const [index, object] of objects.entries()) { offsets.push(Buffer.byteLength(body)); body += `${index + 1} 0 obj\n${object}\nendobj\n`; }
  const xref = Buffer.byteLength(body);
  body += `xref\n0 ${offsets.length}\n0000000000 65535 f \n` + offsets.slice(1).map(offset => `${String(offset).padStart(10, '0')} 00000 n \n`).join('');
  return Buffer.from(body + `trailer\n<< /Size ${offsets.length} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`);
}
async function setup(page) {
  let downloads = 0, denied = false;
  const bytes = pdf(); const now = '2026-10-28T00:00:00Z';
  const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'correction-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', route => json(route, { id: userId, username: 'correction-test', displayName: '校对引用', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    permissions: ['identity.navigation.read', 'ai.chat.sessions.read', 'ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.versions_read'],
    sessionId: id(8), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'chat', parentId: null, routeName: 'ai-chat', path: '/ai/chat', componentKey: 'ai-chat', title: 'AI聊天', caption: 'AI聊天', icon: 'chat', order: 1, requiredPermission: 'ai.chat.sessions.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => json(route, { activeTenantCount: 0, onlineSessionCount: 1, todayRequestCount: 0, todayErrorRate: 0, recentActivities: [], accessTrafficTrend: null, businessEntries: [] }));
  const chat = { id: chatId, tenantId: null, ownerUserId: userId, modelConfigId: modelId, modelName: 'fixture', title: '人工校对来源', messageCount: 1, lastMessageAtUtc: null, createdAtUtc: now, updatedAtUtc: null, version: 1 };
  const location = pageNumber => ({ blockSequence: pageNumber - 1, kind: 'pdf_page', pageNumber, paragraphNumber: null, tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null, image: null });
  const citation = { id: id(9), documentId: docId, versionId, sequence: 0, pageNumber: null, offset: null, length: null, correction: { revisionId: id(10), textLength: 12, spans: [
    { contentKind: 'original', textOffset: 0, textLength: 4, sourceLocation: location(1), sourceOffset: 2, sourceLength: 4 },
    { contentKind: 'corrected', textOffset: 5, textLength: 3, sourceLocation: location(3), sourceOffset: 0, sourceLength: 20 },
    { contentKind: 'supplement', textOffset: 9, textLength: 3, sourceLocation: null, sourceOffset: null, sourceLength: null }
  ] } };
  await page.route('**/api/v1/ai/chat/sessions?*', route => json(route, { items: [chat], page: 1, pageSize: 50, total: 1 }));
  await page.route(`**/api/v1/ai/chat/sessions/${chatId}`, route => json(route, { ...chat, isGenerating: false, messages: [{ id: messageId, sessionId: chatId, roleKey: 'assistant', statusKey: 'completed', content: '保留校对和补写性质的答案', promptTokens: null, completionTokens: null, createdAtUtc: now, isKnowledgeAnswer: true, knowledgeUnavailableCode: null, citations: [citation] }] }));
  await page.route('**/api/v1/ai/knowledge-bases?*', route => json(route, { items: [{ id: baseId, name: '授权校对库', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: now, updatedAtUtc: null, version: 1 }], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents/${docId}/versions?*`, route => json(route, { items: [{ id: versionId, documentId: docId, versionNumber: 1, originalFileName: 'source.pdf', contentType: 'application/pdf', contentHash: 'fixture', sizeBytes: bytes.byteLength, processingStatus: 'ready', createdAtUtc: now }], page: 1, pageSize: 100, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents/${docId}/versions/${versionId}/source`, route => {
    downloads++;
    return denied ? route.fulfill({ status: 403, contentType: 'application/problem+json', body: JSON.stringify({ status: 403, title: 'Forbidden' }) })
      : route.fulfill({ status: 200, contentType: 'application/octet-stream', body: bytes });
  });
  await page.goto('/ai/chat'); await expect(page.getByTestId('knowledge-citation')).toBeVisible();
  return { downloads: () => downloads, deny: () => { denied = true; } };
}

for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`人工引用全部来源与补写，键盘切换真实 PDF 上下文 ${viewport.width}`, async ({ page }, testInfo) => {
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    await page.setViewportSize(viewport); const state = await setup(page);
    const citation = page.getByTestId('knowledge-citation');
    await expect(citation.getByTestId('knowledge-correction-span')).toHaveCount(3);
    await expect(citation.locator('[data-kind="supplement"]')).toContainText('无原文位置');
    await expect(citation.locator('[data-kind="corrected"]')).toContainText('0 + 20');
    await citation.click();
    const dialog = page.getByRole('dialog').filter({ has: page.getByTestId('knowledge-source-read') });
    await expect(dialog.getByTestId('knowledge-source-context')).toHaveCount(2);
    await dialog.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: '授权校对库', exact: true }).click();
    await dialog.getByTestId('knowledge-source-read').click();
    const canvas = dialog.getByTestId('knowledge-pdf-canvas');
    const pixel = () => canvas.evaluate(c => Array.from(c.getContext('2d').getImageData(5, 5, 1, 1).data));
    await expect(canvas).toBeVisible(); await expect.poll(pixel).toEqual([255, 0, 0, 255]);
    const second = dialog.getByTestId('knowledge-source-context').nth(1); await second.focus(); await page.keyboard.press('Enter');
    await expect(second).toHaveAttribute('aria-pressed', 'true'); await expect(dialog.getByTestId('knowledge-pdf-page')).toHaveText('3 / 3');
    await expect.poll(pixel).toEqual([0, 0, 255, 255]); expect(state.downloads()).toBe(1);
    expect(await dialog.evaluate(el => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('correction-context.png'), fullPage: true });
    const accessibility = await new AxeBuilder({ page }).include('[data-testid="knowledge-source-dialog"]').withTags(['wcag2a', 'wcag2aa']).analyze();
    expect(accessibility.violations).toEqual([]);
    expect(errors).toEqual([]);
    await dialog.getByRole('button', { name: '取消', exact: true }).click(); await expect(canvas).toHaveCount(0);
    await citation.click(); await expect(dialog.getByTestId('knowledge-pdf-canvas')).toHaveCount(0);
    await dialog.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: '授权校对库', exact: true }).click();
    state.deny(); await dialog.getByTestId('knowledge-source-download').click();
    await expect(dialog.getByTestId('knowledge-source-error')).toBeVisible(); expect(state.downloads()).toBe(2);
  });
}
