import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createWriteStream, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { watchPrintingBrowserCancellation, runPrintingBrowserResponseAction } from './application-printing-browser-lifecycle.mjs';
import { runPnpm } from './pnpm-process.mjs';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';

const requireE2e = createRequire(new URL('../../e2e/admin-real-stack/package.json', import.meta.url));
const requireParity = createRequire(new URL('../../e2e/admin-parity/package.json', import.meta.url));
const { chromium, expect } = requireE2e('@playwright/test');
const AxeBuilder = requireParity('@axe-core/playwright');

// 仅启动独立生成应用拥有的 Vue；端口与 API 由本次资源范围分配，不复用共享开发服务器。
export async function verifyApplicationPrintingBrowser(appRoot, apiUrl, reportDirectory, {port,fixture,signal}) {
 const origin = 'http://localhost:'+port;
 const evidence={completed:false,hostDirectory:false,hostGrant:false,tenantProfile:false,businessRecord:false,printCalls:0,revokedDenied:false,tenantImport:false,reportExecution:false,reportExport:false,reportRevokedDenied:false,accessibility:[],responses:[]};
 signal?.throwIfAborted();
 const execute=(stage,args)=>{
  signal?.throwIfAborted();
  const result=runPnpm(args,{cwd:appRoot,encoding:'utf8',timeout:300_000});
  writeFileSync(join(reportDirectory,'printing-'+stage+'.log'),(result.stdout??'')+(result.stderr??''));
  assert.equal(result.status,0,'generated printing '+stage+' failed');signal?.throwIfAborted();
 };
 execute('install',['install','--frozen-lockfile']);
 execute('contracts',['--filter','@fullnet/client-contracts','build']);
 execute('build',['--filter','@fullnet/admin','build']);
 const stream=createWriteStream(join(reportDirectory,'printing-vite.log'));
 const child=spawn(process.execPath,[join(appRoot,'ui/admin/node_modules/vite/bin/vite.js'),'--host','localhost','--port',String(port),'--strictPort','--logLevel','error'],
  {cwd:join(appRoot,'ui/admin'),env:{...process.env,VITE_API_PROXY_TARGET:apiUrl,VITE_STRICT_CSP:'1'},stdio:'pipe',windowsHide:true});
 child.stdout.pipe(stream,{end:false});child.stderr.pipe(stream,{end:false});
 let browser; let context; let stage='vite'; let failed=false; let cancellation;
 try {
  cancellation=watchPrintingBrowserCancellation(signal,async()=>{await context?.close();await browser?.close();});
  const deadline=Date.now()+60_000;
  while(true) {
   signal?.throwIfAborted();assert.equal(child.exitCode,null,'generated printing Vue exited');
   try { if((await fetch(origin,{signal:AbortSignal.timeout(2_000)})).ok) break; } catch { /* 等待本次自有服务器就绪。 */ }
   assert.ok(Date.now()<deadline,'generated printing Vue readiness timeout');
   await new Promise(resolve=>setTimeout(resolve,500));
  }
  browser=await chromium.launch(process.platform==='win32'?{channel:'msedge'}:{});
  signal?.throwIfAborted();context=await browser.newContext();const page=await context.newPage();
  page.on('response',response=>{
   const path=new URL(response.url()).pathname;
   if(['/api/v1/printing/','/api/v1/reporting/','/api/v1/import-export/'].some(prefix=>path.startsWith(prefix))) evidence.responses.push({path,method:response.request().method(),status:response.status()});
  });
  await page.addInitScript(()=>{
   localStorage.setItem('fullnet.admin.locale','zh-CN');
   window.print=()=>{window.fullnetPrintCalls=(window.fullnetPrintCalls??0)+1;window.fullnetPrintedText=document.querySelector('.printing-preview-surface')?.textContent??'';};
  });
  const audit=async(surface,selector)=>{
   evidence.auditProgress={surface,phase:'animations',matchingSurfaces:await page.locator(selector).count()};
   await page.locator(selector).evaluate(async element=>{
    await Promise.all(element.getAnimations({subtree:true}).filter(animation=>animation.effect?.getTiming().iterations!==Infinity).map(animation=>animation.finished.catch(()=>{})));
   });
   evidence.auditProgress.phase='axe';
   const result=await new AxeBuilder({page}).include(selector).withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa']).analyze();
   evidence.auditProgress.phase='results';
   const violations=result.violations.map(({id,nodes})=>({id,targets:nodes.map(node=>node.target)}));
   evidence.accessibility.push({surface,violations});assert.equal(violations.length,0,surface+' accessibility failed');
  };
  const choose=async(selector,label)=>{
   await page.locator(selector).click();await page.getByRole('option',{name:label,exact:true}).click();
  };
  signal?.throwIfAborted();stage='host-login';await page.goto(origin);
  await page.getByLabel('账号',{exact:true}).fill('admin');await page.getByLabel('密码',{exact:true}).fill('FullNet!2026Secure');
  await page.getByRole('button',{name:'进入控制台'}).click();
  await expect(page.getByRole('navigation',{name:'主导航'})).toBeVisible({timeout:30_000});
  signal?.throwIfAborted();stage='host-printing';await page.goto(origin+'/#/printing/preview');
  await expect(page.getByTestId('printing-preview-create')).toBeVisible();
  await choose('.printing-preview-view .el-select','Enterprise request print');
  evidence.hostDirectory=true;
  await page.getByTestId('printing-tenant-grants-open').click();
  stage='host-grant-dialog';
  const dialog=page.getByRole('dialog',{name:'租户版本授权：Enterprise request print'});
  await expect(dialog.locator('.el-table__row').filter({hasText:fixture.tenantId})).toBeVisible();
  await page.getByTestId('printing-grant-tenant').fill(fixture.tenantId);
  stage='host-grant-save';
  const grantResponse=await runPrintingBrowserResponseAction(page,response=>new URL(response.url()).pathname.endsWith('/tenant-grants/'+fixture.tenantId)&&response.request().method()==='PUT',()=>page.getByTestId('printing-grant-save').click());
  assert.equal(grantResponse.status(),200);
  stage='host-grant-accessibility';
  // 真实后台保留多个已关闭弹窗的 DOM；只审计本次拥有且按名称识别的授权弹窗。
  await audit('host-grants','[role="dialog"][aria-label="租户版本授权：Enterprise request print"]');evidence.hostGrant=true;
  await dialog.getByRole('button',{name:'关闭',exact:true}).click();
  signal?.throwIfAborted();stage='tenant-context';await page.goto(origin+'/#/tenant-context');
  const tenantRow=page.locator('.tenant-context-view .el-table__row').filter({hasText:fixture.tenantName});
  await expect(tenantRow).toBeVisible();await tenantRow.getByRole('button',{name:'进入租户'}).click();
  await expect(page.getByTestId('shell-current-context')).toHaveText(fixture.tenantName);
  await expect(page).toHaveURL(origin+'/#/');
  signal?.throwIfAborted();stage='tenant-import';await page.goto(origin+'/#/import-export/tasks');
  await page.getByTestId('import-export-task-create').click();
  await choose('[data-testid="import-create-schema"]','企业申请');
  const [templateDownload]=await Promise.all([page.waitForEvent('download'),page.getByTestId('import-create-template').click()]);
  const workbook=fixture.fillWorkbook(readFileSync(await templateDownload.path()),fixture.importValues);
  await page.getByTestId('import-create-file').setInputFiles({name:'browser-request.xlsx',mimeType:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',buffer:Buffer.from(workbook)});
  const uploaded=await runPrintingBrowserResponseAction(page,response=>new URL(response.url()).pathname==='/api/v1/import-export/tasks'&&response.request().method()==='POST',()=>page.getByTestId('import-create-submit').click());
  assert.equal(uploaded.status(),201);const importTask=await uploaded.json();
  assert.equal(importTask.tenantId,fixture.tenantId);assert.equal(importTask.schemaKey,'demo.enterprise_requests');assert.equal(importTask.validRowCount,1);assert.equal(importTask.invalidRowCount,0);
  const executed=await runPrintingBrowserResponseAction(page,response=>new URL(response.url()).pathname==='/api/v1/import-export/tasks/'+importTask.id+'/execute',()=>page.getByTestId('import-export-task-execute').click());
  assert.equal(executed.status(),200);
  await expect(page.getByRole('dialog',{name:'任务详情',exact:true}).locator('.el-tag')).toHaveText('执行成功',{timeout:90_000});
  await page.goto(origin+'/#/enterprise-requests');
  const importedRow=page.locator('.el-table__row').filter({hasText:fixture.importValues[0]});
  await expect(importedRow).toHaveCount(1);await expect(importedRow).toContainText('Enterprise Browser request');await expect(importedRow).toContainText('456.78');
  evidence.tenantImport=true;
  signal?.throwIfAborted();stage='tenant-report-execute';await page.goto(origin+'/#/reporting/execute');
  await page.getByTestId('reporting-execute-definition').click();
  await expect(page.getByRole('option',{name:'Output acceptance ('+fixture.reportingDefinitionKey+') · v2',exact:true})).toBeVisible();
  await page.getByRole('option',{name:'Output acceptance ('+fixture.reportingDefinitionKey+') · v1',exact:true}).click();
  const execution=await runPrintingBrowserResponseAction(page,response=>new URL(response.url()).pathname==='/api/v1/reporting/definitions/'+fixture.reportingDefinitionId+'/execute',()=>page.getByTestId('reporting-execute-run').click());
  assert.equal(execution.status(),200);assert.equal(execution.request().postDataJSON().versionNumber,1);
  await expect(page.locator('.result-card')).toContainText(fixture.reportingExpectedValue);evidence.reportExecution=true;
  signal?.throwIfAborted();stage='tenant-report-export';await page.goto(origin+'/#/reporting/export-tasks');
  await page.getByTestId('reporting-export-create').click();await choose('[data-testid="reporting-export-definition"]','Output acceptance · v1');
  const exported=await runPrintingBrowserResponseAction(page,response=>new URL(response.url()).pathname==='/api/v1/reporting/export-tasks'&&response.request().method()==='POST',()=>page.getByTestId('reporting-export-submit').click());
  assert.equal(exported.status(),201);stage='tenant-report-export-response';const exportTask=await exported.json();
  stage='tenant-report-export-identity';assert.equal(exportTask.definitionId,fixture.reportingDefinitionId);assert.equal(exportTask.versionNumber,1);assert.equal(exportTask.statusKey,'succeeded');
  // 稳定任务身份仅用于定位控件，不能下载同名旧任务冒充本次导出。
  const exportDownload=page.locator('[data-testid="reporting-export-download"][data-task-id="'+exportTask.id+'"]');
  stage='tenant-report-export-download-control';await expect(exportDownload).toHaveCount(1);
  stage='tenant-report-export-download';
  const [reportDownload]=await Promise.all([page.waitForEvent('download'),exportDownload.click()]);
  stage='tenant-report-export-filename';assert.equal(reportDownload.suggestedFilename(),exportTask.outputFileName);
  stage='tenant-report-export-workbook';
  const verified=fixture.verifyWorkbook(readFileSync(await reportDownload.path()),fixture.reportingExpectedValue);assert.equal(verified.dataRows,1);evidence.reportExport=true;
  signal?.throwIfAborted();stage='tenant-report-revoke';await fixture.revokeReporting();await page.goto(origin+'/#/reporting/execute');
  await expect(page.getByTestId('reporting-execute-run')).toBeDisabled();await expect(page.locator('.result-card')).toHaveCount(0);evidence.reportRevokedDenied=true;
  signal?.throwIfAborted();stage='tenant-profile';await page.goto(origin+'/#/printing/published-templates');
  await choose('[data-testid="printing-published-template"]','Tenant output card · v1');
  await page.getByTestId('printing-published-preview').click();
  await expect(page.locator('.printing-preview-surface')).toContainText(fixture.tenantName);
  await expect(page.getByTestId('printing-preview-create')).toHaveCount(0);evidence.tenantProfile=true;
  await page.getByTestId('printing-published-print').click();
  await expect.poll(()=>page.evaluate(()=>window.fullnetPrintCalls??0)).toBe(1);
  signal?.throwIfAborted();stage='tenant-business';await choose('[data-testid="printing-published-template"]','Enterprise request print · v1');
  await expect(page.locator('.printing-preview-surface')).toHaveCount(0);
  await expect(page.getByTestId('printing-published-preview')).toBeDisabled();
  await page.getByTestId('printing-published-record').fill(fixture.recordId);
  await page.getByTestId('printing-published-preview').click();
  await expect(page.locator('.printing-preview-surface')).toContainText('Enterprise Worker request');
  await expect(page.locator('.printing-preview-surface')).toContainText('123.45');
  await audit('tenant-business','.printing-published-view');
  await page.getByTestId('printing-published-print').click();
  await expect.poll(()=>page.evaluate(()=>window.fullnetPrintCalls??0)).toBe(2);
  assert.ok(await page.evaluate(()=>window.fullnetPrintedText.includes('Enterprise Worker request')),'printing invoked before business DOM binding');
  evidence.businessRecord=true;evidence.printCalls=2;
  stage='business-paper';await page.emulateMedia({media:'print'});
  await expect(page.locator('.el-message:visible,.el-notification:visible')).toHaveCount(0);
  await expect(page.locator('.printing-preview-surface')).toBeVisible();
  await expect(page.locator('.art-admin-shell__sidebar')).toBeHidden();
  await expect(page.getByTestId('printing-published-preview')).toBeHidden();
  await page.screenshot({path:join(reportDirectory,'printing-business-paper.png'),fullPage:true});
  await page.emulateMedia({media:'screen'});
  signal?.throwIfAborted();stage='revoke';await fixture.revoke();
  const denied=await runPrintingBrowserResponseAction(page,response=>new URL(response.url()).pathname.endsWith('/published-templates/'+fixture.businessTemplateId+'/preview'),()=>page.getByTestId('printing-published-print').click());
  assert.equal(denied.status(),403);
  await expect(page.locator('.printing-preview-surface')).toHaveCount(0);
  assert.equal(await page.evaluate(()=>window.fullnetPrintCalls),2,'revoked business content invoked print');
  await page.getByTestId('printing-published-refresh').click();
  await expect(page.getByText('暂无已授权模板，请联系平台管理员授权发布版本。',{exact:true})).toBeVisible();
  signal?.throwIfAborted();evidence.revokedDenied=true;evidence.completed=true;return evidence;
 } catch(error) {
  evidence.failureKind=error?.message?.includes('strict mode violation')?'strict-locator':error?.name==='TimeoutError'?'timeout':'execution-error';
  failed=true;evidence.error=stage+': generated printing browser acceptance failed';throw new Error(evidence.error);
 } finally {
  const errors=[];
  try {await cancellation?.dispose();} catch(error) {errors.push(error);}
  for(const cleanup of [()=>context?.close(),()=>browser?.close(),()=>stopLoggedProcess(child,stream)]) {
   try {await cleanup();} catch(error) {errors.push(error);}
  }
  evidence.cleanupSucceeded=errors.length===0;writeFileSync(join(reportDirectory,'printing-browser.json'),JSON.stringify(evidence,null,2));
  if(errors.length&&!failed) throw new Error('generated printing browser cleanup failed');
 }
}
