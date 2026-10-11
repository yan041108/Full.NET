import { expect, test } from '@playwright/test';
const id='019bc2b1-2a40-7cc3-8992-a80de51bf299',other='019bc2b1-2a40-7cc3-8992-a80de51bf298';
const task={id,documentItemId:id,documentTitle:'预览归属样例',versionId:null,sourceFileId:id,outputFileId:id,statusKey:'succeeded',providerKey:'fixture',errorCode:null,requestedByUserId:id,createdAtUtc:'2026-10-10T00:00:00Z',startedAtUtc:null,completedAtUtc:null,version:1};
const access=(code,title,mimeType='text/plain')=>({shareId:id,documentId:id,shareCode:code,title,fileName:'spec.docx',mimeType,fileSizeBytes:12,hasPassword:false,accessCountRemaining:9});
const json=(route,value)=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(value)});
const office='application/vnd.openxmlformats-officedocument.wordprocessingml.document';
async function publicBoot(page){await page.addInitScript(()=>localStorage.setItem('fullnet.admin.locale','zh-CN'));await page.route('**/api/v1/**',route=>route.fulfill({status:404}));}

test('公开分享切换码后重读新分享，旧访问迟到不能覆盖',async({page})=>{
 await publicBoot(page);let release,started=false,finished=false,oldCancelled=false;page.on('requestfailed',request=>{if(request.url().endsWith('/OLD/access'))oldCancelled=true;});const pending=new Promise(resolve=>{release=resolve;});
 await page.route('**/api/v1/document/public/shares/OLD/access',async route=>{started=true;await pending;await json(route,access('OLD','旧分享敏感文档')).catch(()=>{});finished=true;});
 await page.route('**/api/v1/document/public/shares/NEW/access',route=>json(route,access('NEW','新分享文档')));
 await page.route('**/api/v1/document/public/shares/NEW/content',route=>route.fulfill({status:200,contentType:'text/plain',body:'new'}));
 await page.goto('/#/document/share/OLD');await expect.poll(()=>started).toBe(true);
 await page.goto('/#/document/share/NEW');await expect(page.getByText('新分享文档',{exact:true})).toBeVisible();release();await expect.poll(()=>finished).toBe(true);await expect.poll(()=>oldCancelled).toBe(true);
 await expect(page.getByText('旧分享敏感文档')).toHaveCount(0);await expect(page.locator('iframe')).toBeVisible();
});
for(const change of [{documentItemId:other},{versionId:other}]){
 test('公开分享拒绝错配预览创建 '+Object.keys(change)[0],async({page})=>{
  await publicBoot(page);let polls=0,contents=0;
  await page.route('**/api/v1/document/public/shares/OLD/access',route=>json(route,access('OLD','当前分享',office)));
  await page.route('**/api/v1/document/public/shares/OLD/preview-task',route=>json(route,{...task,...change}));
  await page.route('**/api/v1/document/public/shares/OLD/preview-tasks/*',route=>{polls++;return json(route,task);});
  await page.route('**/api/v1/document/public/shares/OLD/preview-tasks/*/content',route=>{contents++;return route.fulfill({status:200,contentType:'application/pdf',body:'controlled'});});
  await page.goto('/#/document/share/OLD');await expect(page.locator('.el-alert')).toBeVisible();expect(polls).toBe(0);expect(contents).toBe(0);await expect(page.locator('iframe')).toHaveCount(0);
 });
}
test('公开分享轮询拒绝其他任务ID，不下载输出',async({page})=>{
 await publicBoot(page);let contents=0;
 await page.route('**/api/v1/document/public/shares/OLD/access',route=>json(route,access('OLD','当前分享',office)));
 await page.route('**/api/v1/document/public/shares/OLD/preview-task',route=>json(route,task));
 await page.route('**/api/v1/document/public/shares/OLD/preview-tasks/'+id,route=>json(route,{...task,id:other}));
 await page.route('**/api/v1/document/public/shares/OLD/preview-tasks/*/content',route=>{contents++;return route.fulfill({status:200,contentType:'application/pdf',body:'controlled'});});
 await page.goto('/#/document/share/OLD');await expect(page.locator('.el-alert')).toBeVisible();expect(contents).toBe(0);await expect(page.locator('iframe')).toHaveCount(0);
});
async function hostBoot(page){
 await publicBoot(page);let lists=0;
 await page.route('**/api/v1/auth/refresh',route=>json(route,{accessToken:'fixture',tokenType:'Bearer',expiresAtUtc:'2099-01-01T00:00:00Z'}));
 await page.route('**/api/v1/me',route=>json(route,{id,username:'fixture',displayName:'Host',tenantId:null,actorScope:'host',scope:'host',isSuperAdministrator:false,passwordChangeRequired:false,permissions:['document.host_preview_tasks.read','document.host_preview_tasks.create'],sessionId:id,preferredLocale:'zh-CN',profileVersion:1}));
 await page.route('**/api/v1/navigation',route=>json(route,[{id:'fixture',parentId:null,routeName:'document-preview-tasks',path:'/document/preview-tasks',componentKey:'document-preview-tasks',title:'文档预览任务',caption:'',icon:'document',order:1,requiredPermission:'document.host_preview_tasks.read',children:[]}]));
 await page.route('**/api/v1/document/host/preview-tasks?*',route=>{lists++;return json(route,{items:[],page:1,pageSize:20,total:0});});
 await page.goto('/#/document/preview-tasks');await expect(page.getByTestId('document-preview-task-create')).toBeEnabled();await expect.poll(()=>lists).toBe(1);return()=>lists;
}
async function openCreate(page){await page.getByTestId('document-preview-task-create').click();await page.getByTestId('document-preview-task-editor-form').locator('input').nth(0).fill(id);}
test('后台创建拒绝错配文档，不误报成功或刷新，重试仍可提交',async({page})=>{
 const lists=await hostBoot(page);let creates=0;
 await page.route('**/api/v1/document/host/preview-tasks',route=>{creates++;return json(route,creates===1?{...task,documentItemId:other}:task);});
 await openCreate(page);await page.getByTestId('document-preview-task-editor-submit').click();await expect(page.locator('.el-alert')).toBeVisible();
 await expect(page.getByTestId('document-preview-task-editor-submit')).toBeEnabled();expect(lists()).toBe(1);await expect(page.locator('.el-message--success')).toHaveCount(0);
 await page.getByTestId('document-preview-task-editor-submit').click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect.poll(lists).toBe(2);await expect(page.locator('.el-message--success')).toBeVisible();
});
test('后台关闭在途创建不刷新或反馈，重开不残留文档和版本',async({page})=>{
 const lists=await hostBoot(page);let release,started=false,finished=false;const pending=new Promise(resolve=>{release=resolve;});
 await page.route('**/api/v1/document/host/preview-tasks',async route=>{started=true;await pending;await json(route,{...task,versionId:id}).catch(()=>{});finished=true;});
 await openCreate(page);await page.getByTestId('document-preview-task-editor-form').locator('input').nth(1).fill(id);
 await page.getByTestId('document-preview-task-editor-submit').click();await expect.poll(()=>started).toBe(true);
 await page.getByRole('dialog').getByRole('button',{name:'取消',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);release();await expect.poll(()=>finished).toBe(true);
 expect(lists()).toBe(1);await expect(page.locator('.el-message--success')).toHaveCount(0);await expect(page.locator('.el-alert')).toHaveCount(0);await page.getByTestId('document-preview-task-create').click();
 const inputs=page.getByTestId('document-preview-task-editor-form').locator('input');await expect(inputs.nth(0)).toHaveValue('');await expect(inputs.nth(1)).toHaveValue('');
});

test('公开分享输错密码后仍能输入正确密码完成预览',async({page})=>{
 await publicBoot(page);let visits=0;
 await page.route('**/api/v1/document/public/shares/OLD/access',route=>{
  visits++;const body=route.request().postDataJSON();
  if(visits===1)return route.fulfill({status:400,contentType:'application/problem+json',body:JSON.stringify({status:400,code:'document.host_share.password_required',title:'需要密码'})});
  if(body.password!=='correct')return route.fulfill({status:403,contentType:'application/problem+json',body:JSON.stringify({status:403,code:'document.host_share.access_denied',title:'密码错误'})});
  return json(route,access('OLD','密码分享文档'));
 });
 await page.route('**/api/v1/document/public/shares/OLD/content',route=>{expect(route.request().postDataJSON().password).toBe('correct');return route.fulfill({status:200,contentType:'text/plain',body:'controlled'});});
 await page.goto('/#/document/share/OLD');await page.getByTestId('document-public-share-password').fill('wrong');await page.getByTestId('document-public-share-submit').click();
 await expect(page.getByText('密码错误',{exact:true})).toBeVisible();await expect(page.getByTestId('document-public-share-password')).toBeVisible();
 await page.getByTestId('document-public-share-password').fill('correct');await page.getByTestId('document-public-share-submit').click();
 await expect(page.getByText('密码分享文档',{exact:true})).toBeVisible();await expect(page.locator('iframe')).toBeVisible();expect(visits).toBe(3);
});
