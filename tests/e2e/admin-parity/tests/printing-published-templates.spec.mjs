import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id='019bc2b1-2a40-7cc3-8992-a80de51bf299';
const date='2026-10-08T00:00:00Z';
const json=(route,value)=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(value)});
async function boot(page,permissions=['printing.published_templates.read','printing.published_templates.preview']){
 await page.addInitScript(()=>{localStorage.setItem('fullnet.admin.locale','zh-CN');window.__printCalls=0;window.print=()=>{window.__printCalls++;window.__printedText=document.querySelector('.printing-preview-html')?.textContent;};});
 await page.route('**/api/v1/**',route=>route.fulfill({status:404}));
 await page.route('**/api/v1/auth/refresh',route=>json(route,{accessToken:'fixture',tokenType:'Bearer',expiresAtUtc:'2099-01-01T00:00:00Z'}));
 await page.route('**/api/v1/me',route=>json(route,{id,username:'fixture',displayName:'Tenant',tenantId:id,actorScope:`tenant:${id.replaceAll('-', '')}`,scope:`tenant:${id.replaceAll('-', '')}`,
 isSuperAdministrator:false,passwordChangeRequired:false,permissions,sessionId:id,preferredLocale:'zh-CN',profileVersion:1}));
 await page.route('**/api/v1/navigation',route=>json(route,[{id:'printing-published-templates',parentId:null,routeName:'printing-published-templates',
 path:'/printing/published-templates',componentKey:'printing-published-templates',title:'已授权打印',caption:'',icon:'printer',order:1,
 requiredPermission:'printing.published_templates.read',children:[]}]));
 let hostReads=0;await page.route('**/api/v1/printing/templates**',route=>{hostReads++;return route.fulfill({status:403});});
 await page.route('**/api/v1/printing/published-templates',route=>json(route,[2,1].map(versionNumber=>({templateId:id,templateKey:'fixture',
 templateName:'租户档案卡',formSchemaKey:'printing.tenant_profile_card',versionNumber}))));
 return ()=>hostReads;
}
test('租户浏览器精确选择旧版、净化预览、打印重验及撤权清空',async({page})=>{
 const errors=[];page.on('pageerror',error=>errors.push(error.message));const hostReads=await boot(page);const versions=[];let revoke=false;
 await page.route('**/api/v1/printing/published-templates/*/preview',route=>{
  const body=route.request().postDataJSON();expect(Object.keys(body)).toEqual(['versionNumber','recordId']);expect(body.recordId).toBeNull();versions.push(body.versionNumber);
  if(revoke)return route.fulfill({status:403,contentType:'application/problem+json',body:JSON.stringify({status:403,code:'authorization.permission_denied',title:'已撤销授权'})});
  return json(route,{templateId:id,templateKey:'fixture',templateName:'租户档案卡',formSchemaKey:'printing.tenant_profile_card',versionNumber:body.versionNumber,
  html:'<div style="color:#8b0000">当前租户版本 '+body.versionNumber+'</div><script>window.__unsafe=true</script><img src=x alt="" onerror="window.__unsafe=true">',boundFields:{},generatedAtUtc:date});
 });
 await page.goto('/#/printing/published-templates');
 await page.getByTestId('printing-published-template').click();await page.getByRole('option',{name:'租户档案卡 · v1',exact:true}).click();
 await page.getByTestId('printing-published-preview').click();await expect(page.locator('.printing-preview-html')).toContainText('当前租户版本 1');
 await expect(page.locator('.printing-preview-html script,.printing-preview-html [onerror]')).toHaveCount(0);
 expect(await page.evaluate(()=>window.__unsafe)).toBeUndefined();
 const axe=await new AxeBuilder({page}).include('.printing-published-view').withTags(['wcag2a','wcag2aa','wcag21aa']).analyze();
 expect(axe.violations).toEqual([]);
 // 通知挂在 body，不能依靠隐藏后台壳排除；保持通知可见来稳定复现纸面污染。
 await page.evaluate(()=>{for(const className of ['el-message','el-notification']) {
  const notice=document.createElement('div');notice.className=className;notice.textContent='打印外临时通知';document.body.append(notice);
 }});
 await expect(page.locator('.el-message,.el-notification')).toHaveCount(2);
 await expect(page.locator('.el-message')).toBeVisible();
 await page.emulateMedia({media:'print'});
 await expect(page.locator('.el-message:visible,.el-notification:visible')).toHaveCount(0);
 await expect(page.locator('.art-admin-shell__sidebar')).toBeHidden();
 await expect(page.locator('.art-admin-shell__header')).toBeHidden();
 await expect(page.locator('.printing-preview-html')).toBeVisible();
 await page.emulateMedia({media:'screen'});
 await page.getByTestId('printing-published-print').click();await expect.poll(()=>page.evaluate(()=>window.__printCalls)).toBe(1);
 expect(await page.evaluate(()=>window.__printedText)).toContain('当前租户版本 1');expect(versions).toEqual([1,1]);
 revoke=true;await page.getByTestId('printing-published-print').click();await expect(page.locator('.printing-preview-html')).toHaveCount(0);
 expect(await page.evaluate(()=>window.__printCalls)).toBe(1);expect(hostReads()).toBe(0);expect(errors).toEqual([]);
});
test('租户只读目录没有预览或打印按钮，刷新仍不读取 Host 草稿',async({page})=>{
 const hostReads=await boot(page,['printing.published_templates.read']);await page.goto('/#/printing/published-templates');
 await expect(page.getByTestId('printing-published-template')).toContainText('租户档案卡');
 await expect(page.getByTestId('printing-published-preview')).toHaveCount(0);await expect(page.getByTestId('printing-published-print')).toHaveCount(0);
 await page.getByTestId('printing-published-refresh').click();expect(hostReads()).toBe(0);
});
