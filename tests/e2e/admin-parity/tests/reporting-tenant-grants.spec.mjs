import { expect, test } from '@playwright/test';

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
  isSuperAdministrator:false,passwordChangeRequired:false,permissions:['reporting.definitions.read','reporting.definitions.grant_tenants'],
  sessionId:id,preferredLocale:'zh-CN',profileVersion:1}));
 await page.route('**/api/v1/navigation',route=>json(route,[{id:'reporting-definitions',parentId:null,routeName:'reporting-definitions',
  path:'/reporting/definitions',componentKey:'reporting-definitions',title:'报表定义',caption:'',icon:'document',order:1,
  requiredPermission:'reporting.definitions.read',children:[]} ]));
 let directoryReads=0;
 for (const path of ['groups','query-ports']) await page.route(`**/api/v1/reporting/${path}`,route=>{directoryReads++;return route.fulfill({status:403});});
 let dataSourceReads=0;
 await page.route('**/api/v1/reporting/data-sources?*',route=>{dataSourceReads++;return route.fulfill({status:403});});
 await page.route('**/api/v1/reporting/definitions',route=>json(route,[{id,groupId:id,dataSourceId:id,definitionKey:'fixture',name:'报表夹具',
  description:null,queryPortKey:'fixture',parameterSchema:[],layoutConfigJson:'{}',latestPublishedVersionNumber:2,isEnabled:true,
  createdAtUtc:date,updatedAtUtc:null,version:1}]));
 await page.route(`**/api/v1/reporting/definitions/${id}/versions`,route=>json(route,[1,2].map(versionNumber=>({id:versionNumber===1?id:tenant,
  definitionId:id,versionNumber,dataSourceId:id,queryPortKey:'fixture',parameterSchema:[],layoutConfigJson:'{}',changeNote:null,
  publishedByUserId:id,publishedAtUtc:date}))));
 const granted=new Set();const writes=[];let wait=false;let pending=false;let release;
 const waiting=new Promise(resolve=>{release=resolve;});
 await page.route(`**/api/v1/reporting/definitions/${id}/versions/*/tenant-grants?*`,async route=>{
  const version=Number(new URL(route.request().url()).pathname.match(/versions\/(\d+)\//u)[1]);
  if(wait){pending=true;await waiting;}
  await json(route,{items:granted.has(version)?[tenant]:[],page:1,pageSize:20,total:granted.has(version)?1:0}).catch(()=>{});
 });
 await page.route(`**/api/v1/reporting/definitions/${id}/versions/*/tenant-grants/${tenant}`,route=>{
  const version=Number(new URL(route.request().url()).pathname.match(/versions\/(\d+)\//u)[1]);
  const method=route.request().method();writes.push({version,method});
  if(method==='PUT')granted.add(version);else if(method==='DELETE')granted.delete(version);
  return json(route,true);
 });
 await page.goto('/#/reporting/definitions');await page.getByTestId('reporting-tenant-grants-open').click();
 const dialog=page.getByRole('dialog').filter({has:page.getByTestId('reporting-grant-save')});
 await expect(dialog.locator('.el-select')).toContainText('2');expect(dataSourceReads).toBe(0);expect(directoryReads).toBe(0);
 await dialog.locator('.el-select').click();await page.getByRole('option',{name:'1',exact:true}).click();
 await page.getByTestId('reporting-grant-tenant').fill(tenant);
 await page.getByTestId('reporting-grant-save').click();await expect(page.getByTestId('reporting-grant-revoke')).toBeVisible();
 expect(writes).toEqual([{version:1,method:'PUT'}]);
 await page.getByTestId('reporting-grant-revoke').click();
 const confirmation=page.getByRole('dialog').filter({has:page.getByTestId('reporting-grant-confirm-revoke')});
 await expect(confirmation).toContainText(tenant);await confirmation.getByRole('button',{name:'取消',exact:true}).click();
 await expect(page.getByTestId('reporting-grant-confirm-revoke')).toHaveCount(0);expect(writes).toHaveLength(1);
 await page.getByTestId('reporting-grant-revoke').click();await page.getByTestId('reporting-grant-confirm-revoke').click();
 await expect(page.getByTestId('reporting-grant-revoke')).toHaveCount(0);expect(writes).toEqual([{version:1,method:'PUT'},{version:1,method:'DELETE'}]);
 wait=true;await page.getByTestId('reporting-grant-refresh').click();await expect.poll(()=>pending).toBe(true);
 await dialog.getByRole('button',{name:'关闭',exact:true}).click();await expect(dialog).toHaveCount(0);release();
 await expect(page.getByTestId('reporting-grant-revoke')).toHaveCount(0);expect(errors).toEqual([]);
});
