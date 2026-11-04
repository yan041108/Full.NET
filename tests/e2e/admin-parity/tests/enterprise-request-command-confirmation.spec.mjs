import {expect,test} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {clickEnterpriseRequestAction} from '../../admin-real-stack/tests/support/enterprise-request-actions.mjs';
const id='019bc2b1-2a40-7cc3-8992-a80de51bf299',prefix='enterprise_request.enterprise_requests.';
const row={id,tenantId:id,organizationUnitId:id,requestNumber:'REQ-COMMAND',title:'原申请确认',status:'Draft',totalAmount:'12.00',applicantUserId:id,version:'1',createdAtUtc:'2026-11-05T00:00:00Z',createdById:id,updatedAtUtc:null,updatedById:null,isDeleted:false,deletedAtUtc:null,deletedById:null};
const json=(route,value,status=200)=>route.fulfill({status,contentType:status>=400?'application/problem+json':'application/json',body:JSON.stringify(value)});
for(const width of [1440,390])test(`申请命令显式确认、取消与未知结果 ${width}`,async({page},info)=>{
 await page.setViewportSize({width,height:1000});await page.addInitScript(()=>localStorage.setItem('fullnet.admin.locale','zh-CN'));await page.route('**/api/v1/**',route=>route.fulfill({status:404}));
 await page.route('**/api/v1/auth/refresh',route=>json(route,{accessToken:'fixture',tokenType:'Bearer',expiresAtUtc:'2099-01-01T00:00:00Z'}));
 await page.route('**/api/v1/me',route=>json(route,{id,username:'operator',displayName:'Operator',actorScope:'tenant',scope:`tenant:${id.replaceAll('-','')}`,tenantId:id,isSuperAdministrator:false,passwordChangeRequired:false,permissions:['identity.navigation.read',prefix+'read',prefix+'submit',prefix+'disable','enterprise_request.enterprise_request_lines.read','enterprise_request.attachments.read'],sessionId:id,preferredLocale:'zh-CN',profileVersion:1}));
 await page.route('**/api/v1/navigation',route=>json(route,[{id:'requests',parentId:null,routeName:'enterprise-requests',path:'/enterprise-requests',componentKey:'enterprise-requests',title:'企业申请',caption:'',icon:'collection',order:1,requiredPermission:prefix+'read',children:[]} ]));
 await page.route('**/api/v1/enterprise_request/enterprise-requests?*',route=>json(route,{items:[row],page:1,pageSize:20,total:1}));
 const writes=[];for(const suffix of ['submit-for-approval','delete'])await page.route(`**/api/v1/enterprise_request/enterprise-requests/${id}/${suffix}`,route=>{writes.push({suffix,body:route.request().postData()});return json(route,{status:503,code:'fixture.unconfirmed',title:'结果未确认'},503);});
 await page.goto('/#/enterprise-requests');
 for(const [action,title,confirmLabel,suffix] of [['提交审批','确认提交审批','确认提交','submit-for-approval'],['删除','确认删除','确认删除','delete']]){
   const before=writes.length,target=page.locator('.el-table__row').filter({hasText:'REQ-COMMAND'});
   await clickEnterpriseRequestAction(page,target,action);let dialog=page.getByRole('dialog',{name:title,exact:true});await expect(dialog).toContainText('REQ-COMMAND');await expect(dialog).toContainText('原申请确认');expect(writes).toHaveLength(before);await dialog.getByRole('button',{name:'取消',exact:true}).click();await expect(dialog).not.toBeVisible();expect(writes).toHaveLength(before);
   await clickEnterpriseRequestAction(page,target,action);dialog=page.getByRole('dialog',{name:title,exact:true});
   await expect.poll(async()=>{const box=await dialog.boundingBox();return !!box&&box.x>=0&&box.x+box.width<=width;}).toBe(true);
   await dialog.screenshot({path:info.outputPath(`${suffix}-confirm.png`),animations:'disabled'});expect((await new AxeBuilder({page}).include('[role="dialog"]').withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa']).analyze()).violations).toEqual([]);
   await dialog.getByRole('button',{name:confirmLabel,exact:true}).click();await expect(page.getByTestId('enterprise-editor-unconfirmed')).toContainText('REQ-COMMAND');await expect(dialog.getByTestId('enterprise-command-confirm')).toBeDisabled();expect(writes).toHaveLength(before+1);expect(writes.at(-1).suffix).toBe(suffix);if(suffix==='delete')expect(JSON.parse(writes.at(-1).body)).toEqual({version:'1'});else expect(writes.at(-1).body).toBeNull();
   await dialog.getByRole('button',{name:'取消',exact:true}).click();await expect(dialog).not.toBeVisible();await page.getByTestId('enterprise-editor-recover').click();const confirmation=page.locator('.el-popconfirm').filter({hasText:'确认已核对原申请的操作结果'});await expect(confirmation).toBeVisible();await confirmation.locator('.el-button--primary').click();await expect(page.getByTestId('enterprise-editor-unconfirmed')).toHaveCount(0);expect(writes).toHaveLength(before+1);
 }
});
