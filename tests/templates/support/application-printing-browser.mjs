import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createWriteStream, writeFileSync } from 'node:fs';
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
 const evidence={completed:false,hostDirectory:false,hostGrant:false,tenantProfile:false,businessRecord:false,printCalls:0,revokedDenied:false,accessibility:[],responses:[]};
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
   if(path.startsWith('/api/v1/printing/')) evidence.responses.push({path,method:response.request().method(),status:response.status()});
  });
  await page.addInitScript(()=>{
   localStorage.setItem('fullnet.admin.locale','zh-CN');
   window.print=()=>{window.fullnetPrintCalls=(window.fullnetPrintCalls??0)+1;window.fullnetPrintedText=document.querySelector('.printing-preview-surface')?.textContent??'';};
  });
  const audit=async(surface,selector)=>{
   await page.locator(selector).evaluate(async element=>{
    await Promise.all(element.getAnimations({subtree:true}).filter(animation=>animation.effect?.getTiming().iterations!==Infinity).map(animation=>animation.finished.catch(()=>{})));
   });
   const result=await new AxeBuilder({page}).include(selector).withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa']).analyze();
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
  await audit('host-grants','[role="dialog"]');evidence.hostGrant=true;
  await dialog.getByRole('button',{name:'关闭',exact:true}).click();
  signal?.throwIfAborted();stage='tenant-context';await page.goto(origin+'/#/tenant-context');
  const tenantRow=page.locator('.tenant-context-view .el-table__row').filter({hasText:fixture.tenantName});
  await expect(tenantRow).toBeVisible();await tenantRow.getByRole('button',{name:'进入租户'}).click();
  await expect(page.getByTestId('shell-current-context')).toHaveText(fixture.tenantName);
  await expect(page).toHaveURL(origin+'/#/');
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
  await page.emulateMedia({media:'print'});
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
 } catch {
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
