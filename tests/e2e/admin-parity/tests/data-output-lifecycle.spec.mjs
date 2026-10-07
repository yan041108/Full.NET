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

async function boot(page, componentKey, path, requiredPermission, permissions, actorScope = 'tenant') {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, token));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: '夹具',
    tenantId: actorScope === 'host' ? null : id, actorScope, scope: actorScope, isSuperAdministrator: false, passwordChangeRequired: false,
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

const importTask = { id, tenantId: id, schemaKey: 'organization.tenant_positions', schemaDisplayName: '租户职位',
  worksheetKey: 'positions', sourceFileId: id, sourceFileName: 'positions.xlsx', statusKey: 'preview_succeeded',
  totalRows: 1, validRowCount: 1, invalidRowCount: 0, errorCode: null, requestedByUserId: id, createdAtUtc,
  previewCompletedAtUtc: null, processedRowCount: 0, succeededRowCount: 0, executionFailedRowCount: 0,
  nextLineNumber: 0, executionStartedAtUtc: null, executionCompletedAtUtc: null, hasErrorReceipt: false, version: 1, previewRows: [] };

for (const scenario of [
  { name: '导入', component: 'import-export-tasks', path: '/import-export/tasks', endpoint: '/import-export/tasks',
    permission: 'import_export.import_tasks.read', task: importTask, pending: 'queued', pendingLabel: '排队中', terminal: 'execution_succeeded', label: '执行成功' },
  { name: '报表导出', component: 'reporting-export-tasks', path: '/reporting/export-tasks', endpoint: '/reporting/export-tasks',
    permission: 'reporting.export_tasks.read', pending: 'queued', pendingLabel: '排队中', terminal: 'succeeded', label: '已成功', task: {
      id, definitionId: id, definitionKey: 'fixture', definitionName: '进度报表', versionNumber: 1,
      formatKey: 'excel', rowCount: 1, outputFileName: 'fixture.xlsx', outputFileId: id, errorCode: null,
      errorMessage: null, requestedByUserId: id, createdAtUtc, completedAtUtc: null, parameters: []
    } },
  { name: '文档预览', component: 'document-preview-tasks', path: '/document/preview-tasks', endpoint: '/document/host/preview-tasks',
    permission: 'document.host_preview_tasks.read', pending: 'pending', pendingLabel: '待处理', terminal: 'succeeded', label: '已成功', task: {
      id, documentItemId: id, documentTitle: '进度文档', versionId: null, sourceFileId: id, outputFileId: id,
      providerKey: 'fixture', errorCode: null, requestedByUserId: id, createdAtUtc, startedAtUtc: null, completedAtUtc: null, version: 1
    } }
]) {
  test(`${scenario.name}在途任务自动刷新到终态，随后停止读取`, async ({ page }) => {
    await page.clock.install();
    await page.clock.pauseAt(new Date());
    await boot(page, scenario.component, scenario.path, scenario.permission, [scenario.permission], scenario.name === '文档预览' ? 'host' : 'tenant');
    let reads = 0;
    await page.route(`**/api/v1${scenario.endpoint}?*`, route => {
      reads++;
      return json(route, { items: [{ ...scenario.task, statusKey: reads === 1 ? scenario.pending : scenario.terminal }], page: 1, pageSize: 20, total: 1 });
    });
    await page.goto(`/#${scenario.path}`);
    await expect.poll(() => reads).toBe(1);
    await expect(page.locator('.el-table__body-wrapper')).toContainText(scenario.pendingLabel);
    await page.clock.runFor(5_000);
    await expect.poll(() => reads).toBe(2);
    await expect(page.locator('.el-table__body-wrapper')).toContainText(scenario.label);
    await page.clock.runFor(20_000); expect(reads).toBe(2);
  });
}

test('导入执行等待时关闭抽屉，重新打开另一个任务不被旧完成覆盖', async ({ page }) => {
  await boot(page, 'import-export-tasks', '/import-export/tasks', 'import_export.import_tasks.read',
    ['import_export.import_tasks.read', 'import_export.import_tasks.execute']);
  const nextTask = { ...importTask, id: nextId, schemaDisplayName: '新任务详情' };
  await page.route('**/api/v1/import-export/tasks?*', route => json(route, { items: [importTask, nextTask], page: 1, pageSize: 20, total: 2 }));
  await page.route(`**/api/v1/import-export/tasks/${id}`, route => json(route, importTask));
  await page.route(`**/api/v1/import-export/tasks/${nextId}`, route => json(route, nextTask));
  let release; let pending = false; let finished = false;
  const waiting = new Promise(resolve => { release = resolve; });
  await page.route(`**/api/v1/import-export/tasks/${id}/execute`, async route => {
    pending = true; await waiting; await json(route, { ...importTask, schemaDisplayName: '迟到旧任务' }).catch(() => {});
    finished = true;
  });
  await page.goto('/#/import-export/tasks');
  await page.getByTestId('import-export-task-detail').first().click();
  await page.getByTestId('import-export-task-execute').click();
  await expect.poll(() => pending).toBe(true);
  const drawer = page.getByRole('dialog', { name: '任务详情', exact: true });
  await drawer.locator('.el-drawer__close-btn').click();
  await expect(drawer).not.toBeVisible();
  await page.getByTestId('import-export-task-detail').nth(1).click();
  release();
  await expect.poll(() => finished).toBe(true);
  await expect(drawer).toContainText('新任务详情');
  await expect(drawer).not.toContainText('迟到旧任务');
  await expect(page.getByTestId('import-export-task-execute')).toBeEnabled();
});

test('导入错误回执实际下载保留派生文件名与字节', async ({ page }) => {
  await boot(page, 'import-export-tasks', '/import-export/tasks', 'import_export.import_tasks.read',
    ['import_export.import_tasks.read', 'import_export.import_tasks.execute']);
  const task = { ...importTask, hasErrorReceipt: true };
  await page.route('**/api/v1/import-export/tasks?*', route => json(route, { items: [task], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/import-export/tasks/${id}`, route => json(route, task));
  const bytes = Buffer.from('controlled-error-receipt');
  await page.route(`**/api/v1/import-export/tasks/${id}/error-receipt`, route =>
    route.fulfill({ status: 200, contentType: 'application/octet-stream', body: bytes }));
  await page.goto('/#/import-export/tasks'); await page.getByTestId('import-export-task-detail').click();
  const downloading = page.waitForEvent('download'); await page.getByTestId('import-export-task-error-receipt').click();
  const download = await downloading; expect(download.suggestedFilename()).toBe('positions-errors.xlsx');
  const stream = await download.createReadStream(); const chunks = [];
  for await (const chunk of stream) chunks.push(chunk);
  expect(Buffer.concat(chunks)).toEqual(bytes);
});

test('离开文档预览页取消待返回 PDF，不调用打开窗口', async ({ page }) => {
  await boot(page, 'document-preview-tasks', '/document/preview-tasks', 'document.host_preview_tasks.read',
    ['document.host_preview_tasks.read', 'import_export.import_tasks.read'], 'host');
  await page.route('**/api/v1/navigation', route => json(route, [
    { id: 'document-preview-tasks', parentId: null, routeName: 'document-preview-tasks', path: '/document/preview-tasks',
      componentKey: 'document-preview-tasks', title: '文档预览', caption: '', icon: 'document', order: 10,
      requiredPermission: 'document.host_preview_tasks.read', children: [] },
    { id: 'import-export-tasks', parentId: null, routeName: 'import-export-tasks', path: '/import-export/tasks',
      componentKey: 'import-export-tasks', title: '导入任务', caption: '', icon: 'document', order: 20,
      requiredPermission: 'import_export.import_tasks.read', children: [] }
  ]));
  await page.route('**/api/v1/document/host/preview-tasks?*', route => json(route, { items: [{
    id, documentItemId: id, documentTitle: 'PDF 夹具', versionId: null, sourceFileId: id, outputFileId: id,
    statusKey: 'succeeded', providerKey: 'fixture', errorCode: null, requestedByUserId: id, createdAtUtc,
    startedAtUtc: null, completedAtUtc: createdAtUtc, version: 1
  }], page: 1, pageSize: 20, total: 1 }));
  await page.route('**/api/v1/import-export/tasks?*', route => json(route, { items: [], page: 1, pageSize: 20, total: 0 }));
  let release; let pending = false; let finished = false;
  const waiting = new Promise(resolve => { release = resolve; });
  await page.route(`**/api/v1/document/host/preview-tasks/${id}/content`, async route => {
    pending = true; await waiting;
    await route.fulfill({ status: 200, contentType: 'application/pdf', body: 'controlled-pdf' }).catch(() => {});
    finished = true;
  });
  await page.goto('/#/document/preview-tasks');
  await page.evaluate(() => { window.__pdfOpens = 0; window.open = () => { window.__pdfOpens++; return null; }; });
  await page.getByTestId('document-preview-task-open-pdf').click(); await expect.poll(() => pending).toBe(true);
  await page.evaluate(() => { window.location.hash = '/import-export/tasks'; });
  await expect(page.getByRole('heading', { name: '导入任务', exact: true })).toBeVisible();
  release(); await expect.poll(() => finished).toBe(true);
  expect(await page.evaluate(() => window.__pdfOpens)).toBe(0);
});

test('导入模板下载、真实 multipart 上传、预校验行和独立执行入口贯通', async ({ page }) => {
  await boot(page, 'import-export-tasks', '/import-export/tasks', 'import_export.import_tasks.read', [
    'import_export.import_tasks.read', 'import_export.import_tasks.create', 'import_export.static_schemas.read',
    'import_export.import_tasks.execute', 'organization.positions.import'
  ]);
  let schemas = 0; let uploads = 0; let submittedBody = ''; let contentType = '';
  await page.route('**/api/v1/import-export/schemas', route => {
    schemas++; return json(route, [{ schemaKey: importTask.schemaKey, displayName: '租户职位', scopeKey: 'tenant',
      requiredPermission: 'organization.positions.import',
      worksheets: [{ worksheetKey: 'positions', displayName: '职位', headerColumns: ['Code', 'Name'] }] }]);
  });
  const bytes = Buffer.from('controlled-template');
  await page.route(`**/api/v1/import-export/schemas/${importTask.schemaKey}/worksheets/positions/template`, route =>
    route.fulfill({ status: 200, contentType: 'application/octet-stream', body: bytes }));
  await page.route('**/api/v1/import-export/tasks?*', route => json(route, { items: [], page: 1, pageSize: 20, total: 0 }));
  await page.route('**/api/v1/import-export/tasks', route => {
    if (route.request().method() !== 'POST') return json(route, { items: [], page: 1, pageSize: 20, total: 0 });
    uploads++; submittedBody = route.request().postData() ?? ''; contentType = route.request().headers()['content-type'] ?? '';
    return json(route, { ...importTask, previewRows: [{ lineNumber: 2, isValid: true, errorCode: null, message: '预校验说明' }] });
  });
  await page.goto('/#/import-export/tasks'); await expect(page.getByTestId('import-export-task-create')).toBeEnabled();
  expect(schemas).toBe(0);
  await page.getByTestId('import-export-task-create').click();
  await expect(page.getByTestId('import-create-schema')).toContainText('租户职位');
  await expect(page.getByTestId('import-create-worksheet')).toContainText('职位');
  const downloading = page.waitForEvent('download'); await page.getByTestId('import-create-template').click();
  const download = await downloading; expect(download.suggestedFilename()).toBe('organization-tenant_positions-positions-template.xlsx');
  const stream = await download.createReadStream(); const chunks = []; for await (const chunk of stream) chunks.push(chunk);
  expect(Buffer.concat(chunks)).toEqual(bytes);
  // 受控字节只验收 multipart 传输和交互，不证明服务端已解析真实工作簿。
  await page.getByTestId('import-create-file').setInputFiles({ name: 'positions.xlsx',
    mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: Buffer.from('controlled-upload') });
  await page.getByTestId('import-create-submit').click();
  await expect(page.getByTestId('import-task-preview-rows')).toContainText('预校验说明');
  await expect(page.getByTestId('import-export-task-execute')).toBeVisible();
  expect(uploads).toBe(1); expect(contentType).toMatch(/^multipart\/form-data; boundary=/);
  expect(submittedBody).toContain('name="schemaKey"'); expect(submittedBody).toContain(importTask.schemaKey);
  expect(submittedBody).toContain('name="worksheetKey"'); expect(submittedBody).toContain('positions');
  expect(submittedBody).toContain('filename="positions.xlsx"'); expect(submittedBody).toContain('controlled-upload');
});
