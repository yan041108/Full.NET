import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const tenant = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const date = '2026-10-08T00:00:00Z';
const json = (route, value) => route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(value)});

test('Host 浏览器按所选发布版本授权、取消确认、撤销，并在关闭后丢弃旧分页', async ({page}) => {
 const errors=[];page.on('pageerror',error=>errors.push(error.message));
 await page.addInitScript(()=>localStorage.setItem('fullnet.admin.locale','zh-CN'));
 await page.route('**/api/v1/**',route=>route.fulfill({status:404}));
 await page.route('**/api/v1/auth/refresh',route=>json(route,{accessToken:'fixture',tokenType:'Bearer',expiresAtUtc:'2099-01-01T00:00:00Z'}));
 await page.route('**/api/v1/me',route=>json(route,{id,username:'fixture',displayName:'Host',tenantId:null,actorScope:'host',scope:'host',
  isSuperAdministrator:false,passwordChangeRequired:false,permissions:['printing.templates.read','printing.templates.grant_tenants'],
  sessionId:id,preferredLocale:'zh-CN',profileVersion:1}));
 await page.route('**/api/v1/navigation',route=>json(route,[{id:'printing-preview',parentId:null,routeName:'printing-preview',
  path:'/printing/preview',componentKey:'printing-preview',title:'打印模板',caption:'',icon:'document',order:1,
  requiredPermission:'printing.templates.read',children:[]} ]));
 let directoryReads=0;
 for (const path of ['groups','query-ports']) await page.route(`**/api/v1/printing/${path}`,route=>{directoryReads++;return route.fulfill({status:403});});
 let dataSourceReads=0;
 await page.route('**/api/v1/printing/data-sources?*',route=>{dataSourceReads++;return route.fulfill({status:403});});
 await page.route('**/api/v1/printing/templates',route=>json(route,[{id,templateKey:'fixture',name:'打印夹具',formSchemaKey:'printing.tenant_profile_card',layoutHtml:'<div>Draft</div>',latestPublishedVersionNumber:2,isEnabled:true,
  createdAtUtc:date,updatedAtUtc:null,version:1}]));
 await page.route(`**/api/v1/printing/templates/${id}/versions`,route=>json(route,[1,2].map(versionNumber=>({id:versionNumber===1?id:tenant,
  templateId:id,versionNumber,layoutHtml:'<div>Frozen</div>',changeNote:null,
  publishedByUserId:id,publishedAtUtc:date}))));
 const granted=new Set();const writes=[];let wait=false;let pending=false;let release;
 const waiting=new Promise(resolve=>{release=resolve;});
 await page.route(`**/api/v1/printing/templates/${id}/versions/*/tenant-grants?*`,async route=>{
  const version=Number(new URL(route.request().url()).pathname.match(/versions\/(\d+)\//u)[1]);
  if(wait){pending=true;await waiting;}
  await json(route,{items:granted.has(version)?[tenant]:[],page:1,pageSize:20,total:granted.has(version)?1:0}).catch(()=>{});
 });
 await page.route(`**/api/v1/printing/templates/${id}/versions/*/tenant-grants/${tenant}`,route=>{
  const version=Number(new URL(route.request().url()).pathname.match(/versions\/(\d+)\//u)[1]);
  const method=route.request().method();writes.push({version,method});
  if(method==='PUT')granted.add(version);else if(method==='DELETE')granted.delete(version);
  return json(route,true);
 });
 await page.goto('/#/printing/preview');await page.getByTestId('printing-tenant-grants-open').click();
 const dialog=page.getByRole('dialog',{name:'租户版本授权：打印夹具',exact:true});
 await expect(dialog.locator('.el-select')).toContainText('2');expect(dataSourceReads).toBe(0);expect(directoryReads).toBe(0);
 await dialog.locator('.el-select').click();await page.getByRole('option',{name:'1',exact:true}).click();
 await page.getByTestId('printing-grant-tenant').fill(tenant);
 await page.getByTestId('printing-grant-save').click();await expect(page.getByTestId('printing-grant-revoke')).toBeVisible();
 await dialog.evaluate(async element=>{await Promise.all(element.getAnimations({subtree:true}).filter(animation=>animation.effect?.getTiming().iterations!==Infinity).map(animation=>animation.finished.catch(()=>{})));});
 const accessibility=await new AxeBuilder({page}).include('[role="dialog"]').withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa']).analyze();
 expect(accessibility.violations.map(({id,nodes})=>({id,targets:nodes.map(node=>node.target)}))).toEqual([]);
 expect(writes).toEqual([{version:1,method:'PUT'}]);
 await page.getByTestId('printing-grant-revoke').click();
 const confirmation=page.getByRole('dialog').filter({has:page.getByTestId('printing-grant-confirm-revoke')});
 await expect(confirmation).toContainText(tenant);await confirmation.getByRole('button',{name:'取消',exact:true}).click();
 await expect(page.getByTestId('printing-grant-confirm-revoke')).toHaveCount(0);expect(writes).toHaveLength(1);
 await page.getByTestId('printing-grant-revoke').click();await page.getByTestId('printing-grant-confirm-revoke').click();
 await expect(page.getByTestId('printing-grant-revoke')).toHaveCount(0);expect(writes).toEqual([{version:1,method:'PUT'},{version:1,method:'DELETE'}]);
 wait=true;await page.getByTestId('printing-grant-refresh').click();await expect.poll(()=>pending).toBe(true);
 await dialog.getByRole('button',{name:'关闭',exact:true}).click();await expect(dialog).toHaveCount(0);release();
 await expect(page.getByTestId('printing-grant-revoke')).toHaveCount(0);expect(errors).toEqual([]);
});
