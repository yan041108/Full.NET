import { expect, test } from '@playwright/test';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const nextId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const createdAtUtc = '2026-10-07T00:00:00Z';
const token = { accessToken: 'fixture-access', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' };
const template = { id, templateKey: 'fixture', name: '打印夹具', formSchemaKey: 'printing.tenant_profile_card',
  layoutHtml: '<div>夹具</div>', latestPublishedVersionNumber: 1, isEnabled: true, createdAtUtc, updatedAtUtc: null, version: 1 };
const definition = { id, groupId: id, dataSourceId: id, definitionKey: 'fixture', name: '报表夹具',
  description: null, queryPortKey: 'fixture', parameterSchema: [], layoutConfigJson: '{}',
  latestPublishedVersionNumber: 1, isEnabled: true, createdAtUtc, updatedAtUtc: null, version: 1 };
const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

async function boot(page, componentKey, path, requiredPermission, permissions) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, token));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: '夹具',
    tenantId: id, actorScope: 'tenant', scope: 'tenant', isSuperAdministrator: false, passwordChangeRequired: false,
    permissions, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: componentKey, parentId: null,
    routeName: componentKey, path, componentKey, title: '数据输出', caption: '', icon: 'document', order: 10,
    requiredPermission, children: [] }]));
}

test('打印只读用户可以加载目录，但没有预览和创建入口', async ({ page }) => {
  await boot(page, 'printing-preview', '/printing/preview', 'printing.templates.read', ['printing.templates.read']);
  await page.route('**/api/v1/printing/templates', route => json(route, [template]));
  await page.goto('/#/printing/preview');
  await expect(page.getByTestId('printing-preview-template')).toContainText('打印夹具');
  await expect(page.getByTestId('printing-preview-run')).toHaveCount(0);
  await expect(page.getByTestId('printing-preview-create')).toHaveCount(0);
});

test('打印切换模板取消旧预览，新预览在真实 DOM 中净化', async ({ page }) => {
  await boot(page, 'printing-preview', '/printing/preview', 'printing.templates.read',
    ['printing.templates.read', 'printing.templates.preview']);
  await page.route('**/api/v1/printing/templates', route => json(route, [template, { ...template, id: nextId, name: '新打印夹具' }]));
  let release; let pending = false;
  const waiting = new Promise(resolve => { release = resolve; });
  const preview = (templateId, html) => ({ templateId, templateKey: 'fixture', templateName: '打印夹具',
    formSchemaKey: template.formSchemaKey, versionNumber: 1, html, boundFields: {}, generatedAtUtc: createdAtUtc });
  await page.route(`**/api/v1/printing/templates/${id}/preview`, async route => {
    pending = true; await waiting; await json(route, preview(id, '<div>旧敏感预览</div>')).catch(() => {});
  });
  await page.route(`**/api/v1/printing/templates/${nextId}/preview`, route =>
    json(route, preview(nextId, '<div>新预览内容<script>window.__unsafePreview=true</script><img src=x onerror="window.__unsafePreview=true"></div>')));
  await page.goto('/#/printing/preview');
  await page.getByTestId('printing-preview-run').click();
  await expect.poll(() => pending).toBe(true);
  await page.getByTestId('printing-preview-template').click();
  await page.getByRole('option', { name: '新打印夹具', exact: true }).click();
  release();
  await expect(page.getByTestId('printing-preview-run')).toBeEnabled();
  await page.getByTestId('printing-preview-run').click();
  await expect(page.locator('.printing-preview-html')).toContainText('新预览内容');
  await expect(page.locator('.printing-preview-html')).not.toContainText('旧敏感预览');
  await expect(page.locator('.printing-preview-html script, .printing-preview-html [onerror]')).toHaveCount(0);
  expect(await page.evaluate(() => window.__unsafePreview)).toBeUndefined();
});

test('执行切换报表后丢弃旧查询，只展示新报表结果', async ({ page }) => {
  await boot(page, 'reporting-execute', '/reporting/execute', 'reporting.executions.run',
    ['reporting.definitions.read', 'reporting.executions.run']);
  await page.route('**/api/v1/reporting/definitions', route => json(route, [definition, { ...definition, id: nextId, name: '新报表夹具' }]));
  let release; let pending = false;
  const waiting = new Promise(resolve => { release = resolve; });
  const result = (definitionId, value) => ({ definitionId, definitionKey: 'fixture', definitionName: '报表夹具',
    versionNumber: 1, queryPortKey: 'fixture', columns: [{ columnKey: 'value', displayName: '结果' }],
    rows: [{ values: { value } }], page: 1, pageSize: 50, hasMore: false, totalRows: 1,
    commandTimeoutSeconds: 30, executedAtUtc: createdAtUtc });
  await page.route(`**/api/v1/reporting/definitions/${id}/execute?*`, async route => {
    pending = true; await waiting; await json(route, result(id, '旧敏感查询')).catch(() => {});
  });
  await page.route(`**/api/v1/reporting/definitions/${nextId}/execute?*`, route => json(route, result(nextId, '新查询内容')));
  await page.goto('/#/reporting/execute');
  await page.getByTestId('reporting-execute-run').click();
  await expect.poll(() => pending).toBe(true);
  await page.getByTestId('reporting-execute-definition').click();
  await page.getByRole('option', { name: '新报表夹具 (fixture)', exact: true }).click();
  release();
  await expect(page.getByTestId('reporting-execute-run')).toBeEnabled();
  await page.getByTestId('reporting-execute-run').click();
  await expect(page.locator('.result-card')).toContainText('新查询内容');
  await expect(page.locator('.result-card')).not.toContainText('旧敏感查询');
});

test('导出查看及下载不请求创建目录，真实浏览器下载保留文件名和字节', async ({ page }) => {
  await boot(page, 'reporting-export-tasks', '/reporting/export-tasks', 'reporting.export_tasks.read',
    ['reporting.export_tasks.read', 'reporting.export_tasks.download', 'reporting.definitions.read']);
  let definitions = 0; let downloads = 0;
  await page.route('**/api/v1/reporting/definitions', route => { definitions++; return json(route, [definition]); });
  await page.route('**/api/v1/reporting/export-tasks?*', route => json(route, { items: [{
    id, definitionId: id, definitionKey: 'fixture', definitionName: '报表夹具', versionNumber: 1,
    formatKey: 'excel', statusKey: 'succeeded', rowCount: 1, outputFileName: 'fixture.xlsx', outputFileId: id,
    errorCode: null, errorMessage: null, requestedByUserId: id, createdAtUtc, completedAtUtc: createdAtUtc
  }], page: 1, pageSize: 20, total: 1 }));
  // 受控下载字节用于验证客户端传输与资源释放，不代表 Worker 生成了有效 Excel。
  const bytes = Buffer.from('controlled-export-bytes');
  await page.route(`**/api/v1/reporting/export-tasks/${id}/download`, route => {
    downloads++; return route.fulfill({ status: 200, contentType: 'application/octet-stream', body: bytes });
  });
  await page.goto('/#/reporting/export-tasks');
  await expect(page.getByTestId('reporting-export-download')).toBeVisible();
  await expect(page.getByTestId('reporting-export-create')).toHaveCount(0);
  const downloading = page.waitForEvent('download');
  await page.getByTestId('reporting-export-download').click();
  const download = await downloading;
  expect(download.suggestedFilename()).toBe('fixture.xlsx');
  const stream = await download.createReadStream();
  const chunks = []; for await (const chunk of stream) chunks.push(chunk);
  expect(Buffer.concat(chunks)).toEqual(bytes);
  expect(definitions).toBe(0); expect(downloads).toBe(1);
});
