import { expect, test } from '@playwright/test';

const baseId = '01900000-0000-7000-8000-000000000001';
const docId = '01900000-0000-7000-8000-000000000002';
const versionId = '01900000-0000-7000-8000-000000000003';
const userId = '01900000-0000-7000-8000-000000000004';
const sessionId = '01900000-0000-7000-8000-000000000005';

// 完整 xref 的小型矢量 PDF；颜色页证明真实 Worker 绘制，脚本/链接不得成为交互层。
function samplePdf() {
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R /OpenAction << /S /JavaScript /JS (app.alert\\(1\\)) >> >>',
    '<< /Type /Pages /Count 3 /Kids [3 0 R 5 0 R 7 0 R] >>',
  ];
  for (const [index, color] of ['1 0 0', '0 1 0', '0 0 1'].entries()) {
    const stream = `${color} rg 0 0 240 320 re f 0 0 0 rg BT /F1 18 Tf 20 150 Td (PDF page ${index + 1}) Tj ET`;
    objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 320] /Resources << /Font << /F1 9 0 R >> >> /Contents ${4 + index * 2} 0 R /Annots [10 0 R] >>`);
    objects.push(`<< /Length ${Buffer.byteLength(stream)} >>\nstream\n${stream}\nendstream`);
  }
  objects.push('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>');
  objects.push('<< /Type /Annot /Subtype /Link /Rect [0 0 240 320] /A << /S /URI /URI (https://pdf-external.invalid/) >> >>');
  let body = '%PDF-1.7\n'; const offsets = [0];
  for (const [index, object] of objects.entries()) { offsets.push(Buffer.byteLength(body)); body += `${index + 1} 0 obj\n${object}\nendobj\n`; }
  const xref = Buffer.byteLength(body);
  body += `xref\n0 ${offsets.length}\n0000000000 65535 f \n` + offsets.slice(1).map(offset => `${String(offset).padStart(10, '0')} 00000 n \n`).join('');
  body += `trailer\n<< /Size ${offsets.length} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(body);
}
async function setup(page, allowSource = true, broken = false) {
  let downloads = 0; let denied = false;
  const pdf = broken ? Buffer.from('broken pdf') : samplePdf();
  const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'pdf-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', route => json(route, {
    id: userId, username: 'pdf-test', displayName: 'PDF测试', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    permissions: ['identity.navigation.read', 'ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.versions_read', ...(allowSource ? ['ai.knowledge_documents.source_read'] : [])],
    sessionId, preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false,
  }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'knowledge', parentId: null, routeName: 'ai-knowledge-documents', path: '/ai/knowledge-documents', componentKey: 'ai-knowledge-documents', title: '知识文件', caption: '授权文件', icon: 'document', order: 1, requiredPermission: 'ai.knowledge_documents.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => json(route, { activeTenantCount: 0, onlineSessionCount: 1, todayRequestCount: 0, todayErrorRate: 0, recentActivities: [], accessTrafficTrend: null, businessEntries: [] }));
  await page.route('**/api/v1/ai/knowledge-bases?*', route => json(route, { items: [{ id: baseId, name: 'PDF知识库', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: '2026-10-07T00:00:00Z', updatedAtUtc: null, version: 1 }], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents?*`, route => json(route, { items: [{ id: docId, knowledgeBaseId: baseId, title: 'PDF来源', description: null, status: 'ready', createdAtUtc: '2026-10-07T00:00:00Z', updatedAtUtc: null, version: 1 }], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents/${docId}/versions?*`, route => json(route, { items: [{ id: versionId, documentId: docId, versionNumber: 1, originalFileName: 'source.pdf', contentType: 'application/pdf', contentHash: 'test', sizeBytes: pdf.byteLength, processingStatus: 'ready', createdAtUtc: '2026-10-07T00:00:00Z' }], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/ai/knowledge-bases/${baseId}/documents/${docId}/versions/${versionId}/source`, route => {
    downloads++;
    return denied ? route.fulfill({ status: 403, contentType: 'application/problem+json', body: JSON.stringify({ status: 403, title: 'Forbidden' }) }) : route.fulfill({ status: 200, contentType: 'application/octet-stream', body: pdf });
  });
  await page.goto('/#/ai/knowledge-documents');
  await page.getByTestId('knowledge-base-select').click();
  await page.getByRole('option', { name: 'PDF知识库', exact: true }).click();
  await page.getByTestId('knowledge-versions').click();
  return { downloads: () => downloads, deny: () => { denied = true; } };
}
test('真实 PDF Worker 翻页缩放绘制，关闭清空，重新下载再次授权', async ({ page }) => {
  const external = []; page.on('request', request => { if (request.url().includes('pdf-external.invalid')) external.push(request.url()); });
  const state = await setup(page);
  await page.getByTestId('knowledge-source').click(); await page.getByTestId('knowledge-source-read').click();
  const canvas = page.getByTestId('knowledge-pdf-canvas'); await expect(canvas).toBeVisible();
  const pixel = () => canvas.evaluate(canvas => Array.from(canvas.getContext('2d').getImageData(5, 5, 1, 1).data));
  expect(await pixel()).toEqual([255, 0, 0, 255]);
  await page.getByTestId('knowledge-pdf-next').click(); await expect(page.getByTestId('knowledge-pdf-page')).toHaveText('2 / 3');
  await expect.poll(pixel).toEqual([0, 255, 0, 255]);
  await page.getByTestId('knowledge-pdf-zoom-in').click(); await expect.poll(() => canvas.evaluate(canvas => canvas.width)).toBe(300);
  await expect(canvas).toBeVisible(); expect(state.downloads()).toBe(1); expect(external).toEqual([]);
  await page.screenshot({ path: '../../../artifacts/r07d/pdf-browser.png', fullPage: true });
  const oldCanvas = await canvas.elementHandle();
  await page.getByRole('dialog').filter({ has: page.getByTestId('knowledge-source-read') }).getByRole('button', { name: '取消', exact: true }).click();
  await expect(canvas).toHaveCount(0); expect(await oldCanvas.evaluate(canvas => canvas.width)).toBe(0);
  await page.getByTestId('knowledge-source').click(); await page.getByTestId('knowledge-source-read').click(); await expect(canvas).toBeVisible();
  expect(state.downloads()).toBe(2); state.deny();
  await page.getByTestId('knowledge-source-download').click(); await expect(page.getByTestId('knowledge-source-error')).toBeVisible(); await expect(canvas).toHaveCount(0); expect(state.downloads()).toBe(3);
});
test('损坏 PDF 明确退回下载且不显示成功页面', async ({ page }) => {
  await setup(page, true, true); await page.getByTestId('knowledge-source').click(); await page.getByTestId('knowledge-source-read').click();
  await expect(page.getByTestId('knowledge-pdf-error')).toBeVisible(); await expect(page.getByTestId('knowledge-pdf-canvas')).toHaveCount(0); await expect(page.getByTestId('knowledge-source-download')).toBeEnabled();
});
test('无原文读取权的版本页不提供预览入口或下载请求', async ({ page }) => {
  const state = await setup(page, false); await expect(page.getByTestId('knowledge-source')).toHaveCount(0); expect(state.downloads()).toBe(0);
});
