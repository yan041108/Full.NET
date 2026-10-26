import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
const { parse: parseSfc, compileScript, compileTemplate } = createRequire(new URL('../../../../packages/admin-form-designer/package.json', import.meta.url))('vue/compiler-sfc');
import { basicFields, advancedFields, containers } from '../../../../packages/admin-form-designer/vendor/vform3/upstream/src/components/form-designer/widget-panel/widgetsConfig.js';
import { alertSchema } from '../../../../packages/admin-form-designer/vendor/vform3/upstream/src/extension/samples/extension-schema.js';
import { getDefaultFormConfig } from '../../../../packages/admin-form-designer/vendor/vform3/upstream/src/utils/util.js';
import { test, expect } from '@playwright/test';
async function openDesigner(page) {
  await page.route('**/vform3-test-host', route => route.fulfill({ contentType: 'text/html', headers: { 'Content-Security-Policy': "frame-src 'self'" }, body: '<!doctype html><html><body></body></html>' }));
  await page.goto('/vform3-test-host');
  await page.evaluate(() => {
    window.vformReplies = []; const channel = 'browser-verification';
    window.addEventListener('message', event => { if (event.source === document.querySelector('iframe')?.contentWindow && event.data?.channel === channel) window.vformReplies.push(event.data); });
    const frame = document.createElement('iframe'); frame.style = 'width:1600px;height:1000px'; frame.sandbox = 'allow-scripts allow-downloads';
    frame.src = '/vform3/index.html#' + new URLSearchParams({ parent: location.origin, channel }); document.body.append(frame);
  });
  await expect.poll(() => page.evaluate(() => window.vformReplies.some(m => m.ready)), { timeout: 20000 }).toBe(true);
  return page.frameLocator('iframe');
}
async function rpc(page, method, value) {
  const id = await page.evaluate(({method, value}) => {
    const id = crypto.randomUUID(); document.querySelector('iframe').contentWindow.postMessage({ protocol: 'fullnet.vform3.v1', channel: 'browser-verification', id, method, value }, '*'); return id;
  }, {method, value});
  await expect.poll(() => page.evaluate(id => window.vformReplies.some(m => m.id === id), id)).toBe(true);
  const reply = await page.evaluate(id => window.vformReplies.find(m => m.id === id), id);
  expect(reply.error).toBeUndefined(); return reply.result;
}
test('VForm 3 完整原版：真实画布、复杂配置往返和工具栏', async ({ page }) => {
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  const frame = await openDesigner(page);
  await expect(frame.getByText('VForm 3', { exact: true })).toBeVisible();
  await expect(frame.locator('.container-widget-item')).toHaveCount(4);
  await expect(frame.getByText('预览', { exact: true }).first()).toBeVisible();
  const field = { id: 'field-name', type: 'input', formItemFlag: true, options: { name: 'title', label: '申请标题', placeholder: '填写标题', defaultValue: '原始默认值', required: true, customClass: ['test-class'], onChange: 'console.log(value)', extra: { keep: [1, true, null] } } };
  const json = { widgetList: [{ id: 'grid1', type: 'grid', category: 'container', cols: [{ id: 'col1', type: 'grid-col', category: 'container', options: { name: 'col1', span: 24 }, widgetList: [field] }], options: { name: 'grid1', gutter: 20 } }], formConfig: { modelName: 'formData', refName: 'vForm', rulesName: 'rules', labelWidth: 180, labelPosition: 'top', size: 'large', jsonVersion: 3, customConfig: { keep: '原样保留' } }, extraRoot: { keep: true } };
  await rpc(page, 'setFormJson', json);
  await expect(frame.getByText('申请标题', { exact: true }).first()).toBeVisible();
  const returned = await rpc(page, 'getFormJson');
  expect(returned.widgetList).toEqual(json.widgetList); expect(returned.extraRoot).toEqual(json.extraRoot);
  for (const [key, value] of Object.entries(json.formConfig)) expect(returned.formConfig[key]).toEqual(value);
  await frame.getByText('预览', { exact: true }).first().click();
  await expect(frame.locator('.el-dialog .render-form input').first()).toHaveValue('原始默认值');
  expect(errors).toEqual([]);
});

test('VForm 3 隔离：宿主 DOM/Cookie、任意出站及伪造消息不可用', async ({ page }) => {
  const frame = await openDesigner(page);
  const checks = await frame.locator('body').evaluate(async () => {
    let parentDenied = false, cookieDenied = false, outboundDenied = false;
    try { void parent.document.body; } catch { parentDenied = true; }
    try { void document.cookie; } catch { cookieDenied = true; }
    try { await fetch('https://untrusted.invalid/collect', { credentials: 'include' }); } catch { outboundDenied = true; }
    return { parentDenied, cookieDenied, outboundDenied };
  });
  expect(checks).toEqual({ parentDenied: true, cookieDenied: true, outboundDenied: true });
  await page.evaluate(() => document.querySelector('iframe').contentWindow.postMessage({ protocol: 'fullnet.vform3.v1', channel: 'wrong', id: 'forged', method: 'getFormJson' }, '*'));
  expect(await page.evaluate(() => window.vformReplies.some(m => m.id === 'forged'))).toBe(false);
});

test('VForm 3 原版全部字段与内置扩展：设计、属性、预览及源码工具栏', async ({ page }) => {
  test.setTimeout(180000);
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  const frame = await openDesigner(page);
  await expect(frame.locator('.field-widget-item')).toHaveCount(23);
  const fields = [...basicFields, ...advancedFields, alertSchema].map((source, index) => {
    const field = structuredClone(source); field.id = 'original_' + index; field.options.name = 'original_' + index; field.options.label = '原版_' + source.type; return field;
  });
  const config = getDefaultFormConfig();
  await rpc(page, 'setFormJson', { widgetList: fields, formConfig: config });
  const saved = await rpc(page, 'getFormJson'); expect(saved.widgetList).toEqual(fields);
  await frame.getByRole('button', { name: '预览', exact: true }).click();
  const dialog = frame.locator('.el-dialog').filter({ has: frame.locator('.render-form') });
  await expect(dialog.locator('.render-form')).toBeVisible();
  await expect(dialog.locator('.ql-editor')).toHaveCount(1);
  await expect(dialog.locator('.el-upload')).toHaveCount(2);
  await expect(dialog.locator('.el-cascader')).toHaveCount(1);
  await expect(dialog.locator('.el-alert').getByText(alertSchema.options.title, { exact: true })).toBeVisible();
  await dialog.getByRole('button', { name: '关闭', exact: true }).click();
  await frame.getByRole('button', { name: '导出代码', exact: true }).click();
  await expect(frame.locator('.el-dialog').filter({ hasText: '导出代码' }).locator('.ace_editor').first()).toBeVisible();
  await frame.locator('.el-dialog').filter({ hasText: '导出代码' }).getByRole('button', { name: '关闭', exact: true }).click();
  await frame.getByRole('button', { name: '生成SFC', exact: true }).click();
  await expect(frame.locator('.el-dialog').filter({ hasText: '生成SFC' }).locator('.ace_editor').first()).toBeVisible();
  const generated = await frame.locator('.copy-vue3-sfc-btn').getAttribute('data-clipboard-text');
  const parsed = parseSfc(generated); expect(parsed.errors).toEqual([]);
  compileScript(parsed.descriptor, { id: 'original-vform-sfc' });
  expect(compileTemplate({ source: parsed.descriptor.template.content, filename: 'OriginalVForm.vue', id: 'original-vform-sfc' }).errors).toEqual([]);
  expect(errors).toEqual([]);
});

test('VForm 3 工具栏：撤销重做、布局切换、组件树和 JSON 导出', async ({ page }) => {
  const frame = await openDesigner(page);
  await frame.locator('.field-widget-item').first().dblclick();
  expect((await rpc(page, 'getFormJson')).widgetList).toHaveLength(1);
  await frame.locator('button[title="撤销"]').click(); expect((await rpc(page, 'getFormJson')).widgetList).toHaveLength(0);
  await frame.locator('button[title="重做"]').click(); expect((await rpc(page, 'getFormJson')).widgetList).toHaveLength(1);
  for (const layout of ['Pad', 'H5', 'PC']) { await frame.getByRole('button', { name: layout, exact: true }).click(); expect((await rpc(page, 'getFormJson')).formConfig.layoutType).toBe(layout); }
  await frame.locator('button[title="组件层次结构树"]').click(); await expect(frame.locator('.node-tree')).toBeVisible();
  await frame.locator('.el-drawer__close-btn').click();
  await frame.getByRole('button', { name: '导出JSON', exact: true }).click();
  await expect(frame.locator('.el-dialog .ace_editor')).toBeVisible();
  expect(await frame.locator('.copy-json-btn').getAttribute('data-clipboard-text')).toContain('widgetList');
  await frame.locator('.el-dialog').getByRole('button', { name: '保存为文件', exact: true }).click();
  const downloadPromise = page.waitForEvent('download');
  await frame.locator('.el-message-box').getByRole('button', { name: 'OK', exact: true }).click();
  const download = await downloadPromise;
  expect(JSON.parse(await readFile(await download.path(), 'utf8'))).toEqual(await rpc(page, 'getFormJson'));
});

test('VForm 3 正式原版 Renderer：实际输入、授权只读和完整数据回传', async ({ page }) => {
  const frame = await openDesigner(page);
  const json = { widgetList: [{ id: 'name', type: 'input', formItemFlag: true, options: { name: 'name', label: '填写标题', required: true } }, { id: 'readonly', type: 'input', formItemFlag: true, options: { name: 'readonly', label: '只读字段', disabled: true } }], formConfig: getDefaultFormConfig() };
  await rpc(page, 'render', { json, data: { name: '初始内容', readonly: '不可修改' } });
  await expect(frame.getByRole('textbox', { name: '填写标题' })).toHaveValue('初始内容');
  await frame.getByRole('textbox', { name: '填写标题' }).fill('实际新内容');
  await frame.getByRole('textbox', { name: '填写标题' }).blur();
  await expect.poll(() => page.evaluate(() => window.vformReplies.some(m => m.changed?.name === '实际新内容'))).toBe(true);
  expect(await rpc(page, 'getFormData')).toEqual({ name: '实际新内容', readonly: '不可修改' });
  await expect(frame.getByRole('textbox', { name: '只读字段' })).toBeDisabled();
});

test('VForm 3 属性编辑与原版上传：配置可编辑、保存回显及预览回执', async ({ page }) => {
  const frame = await openDesigner(page);
  const input = structuredClone(basicFields[0]); input.id = 'title'; input.options.name = 'title'; input.options.label = '初始标题';
  await rpc(page, 'setFormJson', { widgetList: [input], formConfig: getDefaultFormConfig() });
  await frame.locator('.widget-form .el-form-item').filter({ hasText: '初始标题' }).click();
  await frame.getByRole('textbox', { name: '标签', exact: true }).fill('修改后的标题');
  await frame.getByRole('textbox', { name: '占位内容', exact: true }).fill('属性面板修改占位');
  await frame.getByRole('textbox', { name: '占位内容', exact: true }).blur();
  const saved = await rpc(page, 'getFormJson');
  expect(saved.widgetList[0].options.label).toBe('修改后的标题'); expect(saved.widgetList[0].options.placeholder).toBe('属性面板修改占位');
  await rpc(page, 'setFormJson', saved);
  await expect(frame.locator('.widget-form').getByText('修改后的标题', { exact: true })).toBeVisible();
  const upload = structuredClone(advancedFields.find(field => field.type === 'file-upload'));
  upload.id = 'upload'; upload.options.name = 'upload'; upload.options.label = '附件'; upload.options.fileTypes = ['txt']; upload.options.uploadURL = '/vform3/demo/upload';
  await page.route('**/vform3/demo/upload', route => route.fulfill({ contentType: 'application/json', headers: { 'Access-Control-Allow-Origin': '*' }, body: JSON.stringify({ name: 'preview.txt', url: '/vform3/demo/preview.txt' }) }));
  await rpc(page, 'render', { json: { widgetList: [upload], formConfig: getDefaultFormConfig() }, data: {} });
  await frame.locator('input[type=file]').setInputFiles({ name: 'preview.txt', mimeType: 'text/plain', buffer: Buffer.from('original upload preview') });
  await expect(frame.locator('.el-upload-list').getByText('preview.txt')).toBeVisible();
  const values = await rpc(page, 'getFormData'); expect(values.upload).toEqual([{ name: 'preview.txt', url: '/vform3/demo/preview.txt' }]);
});

test('VForm 3 富文本 HTML 边界：保留正常格式并清理恶意事件属性', async ({ page }) => {
  const frame = await openDesigner(page);
  const rich = structuredClone(advancedFields.find(field => field.type === 'rich-editor'));
  rich.id = 'rich'; rich.options.name = 'rich'; rich.options.label = '富文本';
  await rpc(page, 'render', { json: { widgetList: [rich], formConfig: getDefaultFormConfig() }, data: { rich: '<p><strong>保留格式</strong><img src="/vform3/missing.png" onerror="window.__xss=1"></p>' } });
  await expect(frame.locator('.ql-editor strong')).toHaveText('保留格式');
  expect(await frame.locator('.ql-editor').innerHTML()).not.toContain('onerror');
  expect(await frame.locator('body').evaluate(() => window.__xss)).toBeUndefined();
  expect((await rpc(page, 'getFormData')).rich).not.toContain('onerror');
});

test('VForm 3 布局、导入导出与拖拽：表格、标签页、卡片和栅格保持结构', async ({ page }) => {
  test.setTimeout(120000);
  const frame = await openDesigner(page);
  for (const number of [2, 3, 4, 8]) {
    const template = JSON.parse(await readFile(new URL(`../../../../packages/admin-form-designer/runtime-public/templates/json${number}.txt`, import.meta.url), 'utf8'));
    await rpc(page, 'setFormJson', template);
    expect((await rpc(page, 'getFormJson')).widgetList).toEqual(template.widgetList);
    await frame.getByRole('button', { name: '预览', exact: true }).click();
    await expect(frame.locator('.el-dialog .render-form')).toBeVisible();
    await frame.locator('.el-dialog').filter({ has: frame.locator('.render-form') }).getByRole('button', { name: '关闭', exact: true }).click();
  }
  const imported = { widgetList: [{ id: 'imported', type: 'input', formItemFlag: true, options: { name: 'imported', label: '导入保留', placeholder: '原样占位' } }], formConfig: { ...getDefaultFormConfig(), labelWidth: 177 }, extraRoot: { untouched: true } };
  await frame.getByRole('button', { name: '导入JSON', exact: true }).click();
  await frame.locator('.el-dialog .ace_editor').click(); await page.keyboard.press('ControlOrMeta+A'); await page.keyboard.insertText(JSON.stringify(imported));
  await frame.locator('.el-dialog').getByRole('button', { name: '导入', exact: true }).click();
  expect(await rpc(page, 'getFormJson')).toEqual(imported);
  await frame.getByRole('button', { name: '导出JSON', exact: true }).click();
  expect(JSON.parse(await frame.locator('.copy-json-btn').getAttribute('data-clipboard-text'))).toEqual(imported);
  await frame.locator('.el-dialog').getByRole('button', { name: '关闭', exact: true }).click();
  await rpc(page, 'setFormJson', { widgetList: [], formConfig: getDefaultFormConfig() });
  await frame.locator('.field-widget-item').first().dragTo(frame.locator('.form-widget-container .form-widget-list').first());
  await expect.poll(async () => (await rpc(page, 'getFormJson')).widgetList.length).toBe(1);
});

test('VForm 3 模板与预览操作：本地模板、禁用恢复、重置、获取数据和清空', async ({ page }) => {
  const failures = [];
  page.on('requestfailed', request => failures.push(request.url() + ': ' + request.failure()?.errorText));
  page.on('console', message => { if (message.type() === 'error') failures.push(message.text()); });
  const frame = await openDesigner(page);
  await frame.getByRole('tab', { name: '表单模板' }).click();
  await frame.locator('.ft-card').first().getByRole('button').click();
  await frame.locator('.el-message-box').getByRole('button', { name: '确定', exact: true }).click();
  const template = JSON.parse(await readFile(new URL('../../../../packages/admin-form-designer/runtime-public/templates/json1.txt', import.meta.url), 'utf8'));
  await expect.poll(async () => ({ widgets: (await rpc(page, 'getFormJson')).widgetList, failures })).toEqual({ widgets: template.widgetList, failures: [] });
  const field = structuredClone(basicFields[0]); field.id = 'preview'; field.options.name = 'preview'; field.options.label = '预览操作'; field.options.defaultValue = '默认内容';
  await rpc(page, 'setFormJson', { widgetList: [field], formConfig: { ...getDefaultFormConfig(), onFormMounted: 'window.__originalScriptRan = true' } });
  await frame.getByRole('button', { name: '预览', exact: true }).click();
  const preview = frame.locator('.el-dialog').filter({ has: frame.locator('.render-form') });
  const input = preview.getByRole('textbox', { name: '预览操作' });
  await expect(input).toHaveValue('默认内容');
  expect(await frame.locator('body').evaluate(() => window.__originalScriptRan)).toBe(true);
  await preview.getByRole('button', { name: '禁用编辑', exact: true }).click(); await expect(input).toBeDisabled();
  await preview.getByRole('button', { name: '恢复编辑', exact: true }).click(); await expect(input).toBeEnabled();
  await input.fill('修改内容'); await input.blur();
  await preview.getByRole('button', { name: '获取数据', exact: true }).click();
  await expect(frame.locator('.copy-form-data-json-btn')).toHaveAttribute('data-clipboard-text', /修改内容/);
  await frame.locator('.el-dialog').filter({ has: frame.locator('.copy-form-data-json-btn') }).getByRole('button', { name: '关闭', exact: true }).click();
  await preview.getByRole('button', { name: '重置表单', exact: true }).click(); await expect(input).toHaveValue('默认内容');
  await preview.getByRole('button', { name: '关闭', exact: true }).click();
  await frame.getByRole('button', { name: '清空', exact: true }).click();
  await expect.poll(async () => (await rpc(page, 'getFormJson')).widgetList).toEqual([]);
});

test('VForm 3 全字段属性面板：逐项编辑名称并完整保留其余属性', async ({ page }) => {
  test.setTimeout(180000);
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  const frame = await openDesigner(page);
  for (const [index, source] of [...basicFields, ...advancedFields, alertSchema].entries()) {
    const field = structuredClone(source); field.id = 'field_' + index; field.options.name = 'field_' + index;
    await rpc(page, 'setFormJson', { widgetList: [field], formConfig: getDefaultFormConfig() });
    expect((await rpc(page, 'getFormJson')).widgetList).toEqual([field]);
    await frame.locator('.widget-form .el-form-item, .widget-form .static-content-item').first().click({ position: { x: 8, y: 8 } });
    const name = frame.getByRole('textbox', { name: /唯一名称/ });
    await expect(name).toHaveValue(field.options.name);
    const edited = 'edited_' + index; await name.fill(edited); await name.blur();
    expect((await rpc(page, 'getFormJson')).widgetList).toEqual([{ ...field, options: { ...field.options, name: edited } }]);
  }
  expect(errors).toEqual([]);
});

test('VForm 3 容器交互：跨列拖动、复制删除及表格合并回显', async ({ page }) => {
  test.setTimeout(120000);
  const frame = await openDesigner(page);
  const make = (type, id) => { const value = structuredClone(containers.find(item => item.type === type)); value.id = id; value.options.name = id; return value; };
  const grid = make('grid', 'grid'); const left = make('grid-col', 'left'); const right = make('grid-col', 'right');
  const field = structuredClone(basicFields[0]); field.id = 'moving'; field.options.name = 'moving'; field.options.label = '跨列移动';
  left.widgetList = [field]; grid.cols = [left, right];
  await rpc(page, 'setFormJson', { widgetList: [grid], formConfig: getDefaultFormConfig() });
  await frame.locator('.grid-cell').first().getByText('跨列移动', { exact: true }).click();
  await frame.locator('.grid-cell').first().locator('.drag-handler').dragTo(frame.locator('.grid-cell').nth(1));
  await expect.poll(async () => (await rpc(page, 'getFormJson')).widgetList[0].cols[1].widgetList.map(item => item.id)).toEqual(['moving']);
  expect((await rpc(page, 'getFormJson')).widgetList[0].cols[0].widgetList).toEqual([]);
  await frame.locator('.grid-container').click({ position: { x: 2, y: 2 } });
  await frame.locator('[title="复制组件"]').click();
  await expect.poll(async () => (await rpc(page, 'getFormJson')).widgetList.length).toBe(2);
  await frame.locator('.container-action [title="移除组件"]').click();
  await expect.poll(async () => (await rpc(page, 'getFormJson')).widgetList.length).toBe(1);
  const table = make('table', 'table'); table.rows = [{ cols: [make('table-cell', 'cell1'), make('table-cell', 'cell2')] }];
  await rpc(page, 'setFormJson', { widgetList: [table], formConfig: getDefaultFormConfig() });
  await frame.locator('.table-cell').first().click(); await frame.locator('[title="单元格操作"]').click();
  await frame.getByRole('menuitem', { name: '合并右侧单元格', exact: true }).click();
  const merged = await rpc(page, 'getFormJson'); expect(merged.widgetList[0].rows[0].cols[0].options.colspan).toBe(2);
  expect(merged.widgetList[0].rows[0].cols[1].merged).toBe(true);
  await rpc(page, 'setFormJson', merged); await expect(frame.locator('.table-cell')).toHaveCount(1);
  expect((await rpc(page, 'getFormJson')).widgetList).toEqual(merged.widgetList);
});
