import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { zhCN } from '../../packages/admin-i18n/src/messages.ts';
import { verifyEnterpriseRequestCrudBrowser } from './support/application-enterprise-crud-browser.mjs';
import { withTestRun } from '../../scripts/testing/test-run-context.mjs';

const requireE2e = createRequire(new URL('../e2e/admin-real-stack/package.json', import.meta.url));
const { chromium } = requireE2e('@playwright/test');

test('当前中文表单标签贯通企业申请浏览器 CRUD 与离页输入丢弃', { timeout: 30_000 }, async () => {
  const runtimeRoot = path.resolve('.tmp/enterprise-crud-browser-localization');
  await mkdir(runtimeRoot, { recursive: true });
  await withTestRun({ cwd: runtimeRoot, disposableBuild: true }, async () => {
    const fields = ['organizationUnitId', 'requestNumber', 'title', 'totalAmount', 'applicantUserId'];
    const labels = Object.fromEntries(fields.map(key => [key, zhCN['enterpriseRequests.' + key]]));
    const calls = []; let record; let browser;
    // 隔离页面只保留验收器需要的交互；标签读取当前消息字典，完整 Vue 行为由独立应用验收负责。
    const html = `<!doctype html><html lang="zh-CN"><body><section class="generated-crud-view"></section><script>
      const fields=${JSON.stringify(fields)},labels=${JSON.stringify(labels)};
      let row=null;const view=document.querySelector('section');
      function closeDialog(){document.querySelector('dialog')?.remove();}
      function render(){view.innerHTML='<button onclick="edit(false)">创建</button>'+(row?'<table><tbody><tr class="el-table__row"><td>'+row.requestNumber+'</td><td>'+row.title+'</td><td><button onclick="edit(true)">编辑</button><button onclick="removeRow()">删除</button></td></tr></tbody></table>':'');}
      function edit(existing){closeDialog();const dialog=document.createElement('dialog');dialog.open=true;dialog.setAttribute('aria-label',existing?'编辑':'创建');
        dialog.innerHTML=fields.filter(key=>!existing||key!=='organizationUnitId').map(key=>'<label for="'+key+'">'+labels[key]+'</label><input id="'+key+'" name="'+key+'" value="'+(existing?row[key]:'')+'">').join('')+'<button id="save">保存</button><button id="cancel">取消</button>';
        document.body.append(dialog);dialog.querySelector('#cancel').onclick=closeDialog;
        dialog.querySelector('#save').onclick=async()=>{const values=Object.fromEntries([...dialog.querySelectorAll('input')].map(input=>[input.name,input.value]));if(existing)values.version=row.version;
          const response=await fetch('/api/v1/enterprise_request/enterprise-requests'+(existing?'/'+row.id:''),{method:existing?'PUT':'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(values)});row=await response.json();closeDialog();render();};}
      function removeRow(){closeDialog();const dialog=document.createElement('dialog');dialog.open=true;dialog.setAttribute('aria-label','确认删除');dialog.innerHTML='<button id="cancel">取消</button><button id="confirm">确认删除</button>';document.body.append(dialog);
        dialog.querySelector('#cancel').onclick=closeDialog;dialog.querySelector('#confirm').onclick=async()=>{await fetch('/api/v1/enterprise_request/enterprise-requests/'+row.id+'/delete',{method:'POST'});row=null;closeDialog();render();};}
      window.addEventListener('hashchange',()=>{closeDialog();view.hidden=location.hash.includes('tenant-context');render();});render();
    </script></body></html>`;
    const server = createServer(async (request, response) => {
      if (!request.url.startsWith('/api/')) { response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end(html); return; }
      const chunks = []; for await (const chunk of request) chunks.push(chunk);
      const body = chunks.length ? JSON.parse(Buffer.concat(chunks).toString()) : null;
      calls.push({ method: request.method, path: request.url, body });
      response.setHeader('Content-Type', 'application/json');
      if (request.method === 'POST' && !request.url.endsWith('/delete')) {
        record = { ...body, id: 'record-1', tenantId: 'tenant-1', version: 1 }; response.statusCode = 201;
      } else if (request.method === 'PUT') record = { ...record, ...body, version: 2 };
      response.end(JSON.stringify(record));
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    try {
      const origin = 'http://127.0.0.1:' + server.address().port;
      browser = await chromium.launch(process.platform === 'win32' ? { channel: 'msedge' } : {});
      const page = await browser.newPage(); page.setDefaultTimeout(2000);
      await page.goto(origin + '/#/enterprise-requests', { waitUntil: 'domcontentloaded', timeout: 15_000 });
      const result = await verifyEnterpriseRequestCrudBrowser(page, origin, { tenantId: 'tenant-1', importValues: ['localized', '', '', 'actor-1', 'org-1'] });
      assert.equal(result.completed, true); assert.equal(result.pageInputDiscarded, true); assert.equal(result.deleteCancelled, true);
      assert.deepEqual(calls.map(call => call.method), ['POST', 'PUT', 'POST']);
      assert.equal(calls[0].body.organizationUnitId, 'org-1'); assert.equal(calls[0].body.applicantUserId, 'actor-1');
      assert.equal(calls[1].body.version, 1);
    } finally { await browser?.close(); await new Promise(resolve => server.close(resolve)); }
  });
});
