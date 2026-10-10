import { expect, test } from '@playwright/test';
const id='019bc2b1-2a40-7cc3-8992-a80de51bf299',other='019bc2b1-2a40-7cc3-8992-a80de51bf298';
const share={id,documentId:id,shareCode:'SHARE-OLD',createdAtUtc:'2026-10-10T00:00:00Z',expireTime:'2026-10-17T00:00:00Z',maxAccessCount:10,accessCount:0,isEnabled:true,version:1,hasPassword:false};
const doc={id,documentNo:'DOC-1',title:'分享验收文档',description:null,categoryId:null,categoryName:null,categoryColor:null,documentType:1,sizeKb:0,thumbnail:null,status:1,accessCount:0,sort:0,lastAccessTime:null,currentVersion:null,tags:[],createdAtUtc:'2026-10-10T00:00:00Z',createdByUserId:id,updatedAtUtc:null,updatedByUserId:null,deletedAtUtc:null,deletedByUserId:null,version:1};
const json=(route,value)=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(value)});
async function boot(page,library=false){
 await page.addInitScript(()=>localStorage.setItem('fullnet.admin.locale','zh-CN'));
 await page.route('**/api/v1/**',route=>route.fulfill({status:404}));
 await page.route('**/api/v1/auth/refresh',route=>json(route,{accessToken:'fixture',tokenType:'Bearer',expiresAtUtc:'2099-01-01T00:00:00Z'}));
 const permissions=['document.host_documents.read','document.host_shares.create',...(library?[]:['document.host_shares.read','document.host_shares.update_status'])];
 await page.route('**/api/v1/me',route=>json(route,{id,username:'fixture',displayName:'Host',tenantId:null,actorScope:'host',scope:'host',isSuperAdministrator:false,passwordChangeRequired:false,permissions,sessionId:id,preferredLocale:'zh-CN',profileVersion:1}));
 const routeName=library?'host-document-items':'document-shares',path=library?'/document/host-items':'/document/shares';
 await page.route('**/api/v1/navigation',route=>json(route,[{id:'fixture',parentId:null,routeName,path,componentKey:routeName,title:'文档分享',caption:'',icon:'document',order:1,requiredPermission:library?'document.host_documents.read':'document.host_shares.read',children:[]}]));
 let lists=0;
 await page.route('**/api/v1/document/host/shares?*',route=>{lists++;return json(route,{items:[share],page:1,pageSize:20,total:1});});
 await page.route('**/api/v1/document/host/items?*',route=>json(route,{items:library?[doc,{...doc,id:other,documentNo:'DOC-2',title:'第二份文档'}]:[doc],page:1,pageSize:100,total:library?2:1}));
 await page.route('**/api/v1/document/host/tags*',route=>json(route,[]));
 await page.route('**/api/v1/document/host/categories*',route=>json(route,[]));
 await page.goto('/#'+path);
 await expect(page.getByTestId(library?'host-document-item-share':'document-share-create').first()).toBeEnabled();
 if(!library)await expect.poll(()=>lists).toBe(1);
 return()=>lists;
}
async function open(page,library=false){
 await page.getByTestId(library?'host-document-item-share':'document-share-create').first().click();
 if(!library){await page.getByTestId('document-share-document-select').click();await page.getByRole('option',{name:'分享验收文档 (DOC-1)',exact:true}).click();}
 return page.getByTestId('document-share-editor-form').locator('input');
}

test('非法访问上限不发请求，合法整数及有效期边界成功',async({page})=>{
 const lists=await boot(page);let creates=0;
 await page.route('**/api/v1/document/host/shares',route=>{creates++;expect(route.request().postDataJSON()).toMatchObject({documentId:id,validDays:365,maxAccessCount:1});return json(route,share);});
 await open(page);const inputs=page.getByTestId('document-share-editor-form').locator('input');
 await inputs.nth(3).fill('abc');await page.getByTestId('document-share-editor-submit').click();await expect(page.locator('.el-message--warning')).toBeVisible();expect(creates).toBe(0);
 await inputs.nth(1).fill('365');await inputs.nth(3).fill('1');await page.getByTestId('document-share-editor-submit').click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect.poll(lists).toBe(2);expect(creates).toBe(1);
});

test('错配文档创建失败关闭，保持弹窗并可重试',async({page})=>{
 const lists=await boot(page);let creates=0;
 await page.route('**/api/v1/document/host/shares',route=>json(route,++creates===1?{...share,documentId:other}:share));
 await open(page);await page.getByTestId('document-share-editor-submit').click();await expect(page.locator('.el-message--error')).toBeVisible();expect(lists()).toBe(1);await expect(page.getByTestId('document-share-editor-submit')).toBeEnabled();
 await page.getByTestId('document-share-editor-submit').click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect.poll(lists).toBe(2);expect(creates).toBe(2);
});

test('关闭在途创建取消请求，迟到结果不刷新，重开清空密码',async({page})=>{
 const lists=await boot(page);let release,started=false,finished=false,cancelled=false;const pending=new Promise(resolve=>{release=resolve;});
 page.on('requestfailed',request=>{if(request.url().endsWith('/host/shares')&&request.method()==='POST')cancelled=true;});
 await page.route('**/api/v1/document/host/shares',async route=>{started=true;await pending;await json(route,share).catch(()=>{});finished=true;});
 await open(page);await page.getByTestId('document-share-editor-form').locator('input').nth(2).fill('Password@2026');await page.getByTestId('document-share-editor-submit').click();await expect.poll(()=>started).toBe(true);
 await page.getByRole('dialog').getByRole('button',{name:'取消',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);release();await expect.poll(()=>finished).toBe(true);await expect.poll(()=>cancelled).toBe(true);expect(lists()).toBe(1);await expect(page.locator('.el-message--success')).toHaveCount(0);await expect(page.locator('.el-message--error')).toHaveCount(0);
 await page.getByTestId('document-share-create').click();await expect(page.getByTestId('document-share-editor-form').locator('input').nth(2)).toHaveValue('');
});

test('状态更新拒绝其他分享响应，重试合法身份后刷新',async({page})=>{
 const lists=await boot(page);let updates=0;
 await page.route('**/api/v1/document/host/shares/'+id+'/status',route=>json(route,++updates===1?{...share,id:other}:{...share,isEnabled:false,version:2}));
 await page.getByTestId('document-share-toggle').click();await expect(page.locator('.el-alert--error')).toBeVisible();expect(lists()).toBe(1);
 await page.getByTestId('document-share-toggle').click();await expect.poll(lists).toBe(2);await expect(page.locator('.el-message--success')).toBeVisible();expect(updates).toBe(2);
});

test('离开分享页取消在途状态更新，旧成功不反馈',async({page})=>{
 const lists=await boot(page);let release,started=false,finished=false,cancelled=false;const pending=new Promise(resolve=>{release=resolve;});
 page.on('requestfailed',request=>{if(request.url().endsWith('/status'))cancelled=true;});
 await page.route('**/api/v1/document/host/shares/'+id+'/status',async route=>{started=true;await pending;await json(route,{...share,isEnabled:false,version:2}).catch(()=>{});finished=true;});
 await page.getByTestId('document-share-toggle').click();await expect.poll(()=>started).toBe(true);await page.goto('/#/document/share/PUBLIC');release();await expect.poll(()=>finished).toBe(true);await expect.poll(()=>cancelled).toBe(true);expect(lists()).toBe(1);await expect(page.locator('.el-message--success')).toHaveCount(0);
});

test('文档库仅凭自身读取和分享创建权限完成单项分享',async({page})=>{
 await boot(page,true);let creates=0;await page.route('**/api/v1/document/host/shares',route=>{creates++;expect(route.request().postDataJSON().documentId).toBe(id);return json(route,share);});
 await open(page,true);await expect(page.getByTestId('document-share-document-select')).toHaveCount(0);await page.getByTestId('document-share-editor-submit').click();await expect(page.getByRole('dialog')).toHaveCount(0);expect(creates).toBe(1);await expect(page.locator('.el-message--success')).toBeVisible();
});

test('文档库批量拒绝错配计数，正常部分成功按实际数量反馈',async({page})=>{
 await boot(page,true);let creates=0;
 const results=[{documentId:id,succeeded:true,share,errorCode:null,message:null},{documentId:other,succeeded:false,share:null,errorCode:'denied',message:'拒绝'}];
 await page.route('**/api/v1/document/host/shares/batch',route=>{expect(route.request().postDataJSON().documentIds).toEqual([id,other]);return json(route,{succeededCount:++creates===1?2:1,results});});
 await page.locator('.el-table__header-wrapper .el-checkbox').first().click();await page.getByTestId('host-document-item-batch-share').click();await page.getByTestId('document-share-editor-submit').click();await expect(page.locator('.el-message--error')).toBeVisible();await expect(page.getByTestId('document-share-editor-submit')).toBeEnabled();
 await page.getByTestId('document-share-editor-submit').click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.locator('.el-message--success')).toContainText('1');await expect(page.locator('.el-message--success')).toContainText('2');expect(creates).toBe(2);await expect(page).toHaveURL(/document\/host-items$/);
});
