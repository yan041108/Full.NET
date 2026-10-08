import { expect, test } from '@playwright/test';
const id='019bc2b1-2a40-7cc3-8992-a80de51bf299';
const other='019bc2b1-2a40-7cc3-8992-a80de51bf298';
const date='2026-10-08T00:00:00Z';
const json=(route,value,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(value)});
const definition={definitionId:id,definitionKey:'fixture',name:'版本报表',queryPortKey:'fixture',parameterSchema:[],layoutConfigJson:'{}'};
const task={id,definitionId:id,definitionKey:'fixture',definitionName:'版本报表',versionNumber:1,formatKey:'excel',statusKey:'succeeded',rowCount:1,outputFileName:'version-1.xlsx',outputFileId:other,errorCode:null,errorMessage:null,requestedByUserId:id,createdAtUtc:date,completedAtUtc:date,parameters:[]};
async function boot(page,componentKey,path,permission){
 await page.addInitScript(()=>localStorage.setItem('fullnet.admin.locale','zh-CN'));
 await page.route('**/api/v1/**',route=>route.fulfill({status:404}));
 await page.route('**/api/v1/auth/refresh',route=>json(route,{accessToken:'fixture',tokenType:'Bearer',expiresAtUtc:'2099-01-01T00:00:00Z'}));
 await page.route('**/api/v1/me',route=>json(route,{id,username:'fixture',displayName:'Tenant',tenantId:id,actorScope:'host',scope:'tenant:'+id.replaceAll('-',''),isSuperAdministrator:false,passwordChangeRequired:false,permissions:['reporting.executions.run','reporting.export_tasks.read','reporting.export_tasks.create','reporting.export_tasks.download'],sessionId:id,preferredLocale:'zh-CN',profileVersion:1}));
 await page.route('**/api/v1/navigation',route=>json(route,[{id:componentKey,parentId:null,routeName:componentKey,path,componentKey,title:'报表',caption:'',icon:'document',order:1,requiredPermission:permission,children:[]}]));
 await page.route('**/api/v1/reporting/published-definitions',route=>json(route,[{...definition,versionNumber:2},{...definition,versionNumber:1}]));
}
test('浏览器选择旧版本，拒绝错版查询，再切新版清空旧结果',async({page})=>{
 await boot(page,'reporting-execute','/reporting/execute','reporting.executions.run');let mismatch=true;const versions=[];
 await page.route('**/api/v1/reporting/definitions/*/execute?*',route=>{
  const version=route.request().postDataJSON().versionNumber;versions.push(version);
  return json(route,{definitionId:id,definitionKey:'fixture',definitionName:'版本报表',versionNumber:mismatch?2:version,queryPortKey:'fixture',columns:[{columnKey:'value',displayName:'结果'}],rows:[{values:{value:'版本'+version+'内容'}}],page:1,pageSize:50,hasMore:false,totalRows:1,commandTimeoutSeconds:30,executedAtUtc:date});
 });
 await page.goto('/#/reporting/execute');await page.getByTestId('reporting-execute-definition').click();
 await expect(page.getByRole('option',{name:'版本报表 (fixture) · v2',exact:true})).toBeVisible();
 await page.getByRole('option',{name:'版本报表 (fixture) · v1',exact:true}).click();
 await page.getByTestId('reporting-execute-run').click();await expect(page.getByRole('alert')).toBeVisible();await expect(page.locator('.result-card')).toHaveCount(0);
 mismatch=false;await page.getByTestId('reporting-execute-run').click();await expect(page.locator('.result-card')).toContainText('版本1内容');
 await page.getByTestId('reporting-execute-definition').click();await page.getByRole('option',{name:'版本报表 (fixture) · v2',exact:true}).click();
 await expect(page.locator('.result-card')).toHaveCount(0);await page.getByTestId('reporting-execute-run').click();await expect(page.locator('.result-card')).toContainText('版本2内容');
 expect(versions).toEqual([1,1,2]);
});
test('浏览器创建指定旧版导出，错任务不关弹窗，下载定位当前任务',async({page})=>{
 await boot(page,'reporting-export-tasks','/reporting/export-tasks','reporting.export_tasks.read');let mismatch=true;let created=false;const versions=[];
 await page.route('**/api/v1/reporting/export-tasks?*',route=>json(route,{items:created?[task]:[],page:1,pageSize:20,total:created?1:0}));
 await page.route('**/api/v1/reporting/export-tasks',route=>{
  const body=route.request().postDataJSON();expect(body.definitionId).toBe(id);expect(body.formatKey).toBe('excel');versions.push(body.versionNumber);
  created=!mismatch;return json(route,{...task,definitionId:mismatch?other:id},201);
 });
 await page.route('**/api/v1/reporting/export-tasks/*/download',route=>route.fulfill({status:200,contentType:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',body:'owned mock workbook'}));
 await page.goto('/#/reporting/export-tasks');await page.getByTestId('reporting-export-create').click();await page.getByTestId('reporting-export-definition').click();
 await expect(page.getByRole('option',{name:'版本报表 · v2',exact:true})).toBeVisible();await page.getByRole('option',{name:'版本报表 · v1',exact:true}).click();
 await page.getByTestId('reporting-export-submit').click();await expect(page.getByRole('alert')).toBeVisible();await expect(page.getByTestId('reporting-export-submit')).toBeVisible();
 mismatch=false;await page.getByTestId('reporting-export-submit').click();await expect(page.getByTestId('reporting-export-submit')).toBeHidden();
 const [download]=await Promise.all([page.waitForEvent('download'),page.locator('[data-testid="reporting-export-download"][data-task-id="'+id+'"]').click()]);
 expect(download.suggestedFilename()).toBe('version-1.xlsx');expect(versions).toEqual([1,1]);
});
