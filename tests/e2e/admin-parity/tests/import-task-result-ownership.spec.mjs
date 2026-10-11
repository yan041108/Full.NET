import { expect, test } from '@playwright/test';
const id='019bc2b1-2a40-7cc3-8992-a80de51bf299';
const otherId='019bc2b1-2a40-7cc3-8992-a80de51bf298';
const task={id,tenantId:id,schemaKey:'organization.tenant_positions',schemaDisplayName:'导入归属样例',worksheetKey:'positions',
 sourceFileId:id,sourceFileName:'positions.xlsx',statusKey:'preview_succeeded',totalRows:1,validRowCount:1,invalidRowCount:0,errorCode:null,
 requestedByUserId:id,createdAtUtc:'2026-10-10T00:00:00Z',previewCompletedAtUtc:null,processedRowCount:0,succeededRowCount:0,
 executionFailedRowCount:0,nextLineNumber:0,executionStartedAtUtc:null,executionCompletedAtUtc:null,hasErrorReceipt:false,version:1,previewRows:[]};
const json=(route,value)=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(value)});
async function boot(page,withTask=false){
 await page.addInitScript(()=>localStorage.setItem('fullnet.admin.locale','zh-CN'));
 await page.route('**/api/v1/**',route=>route.fulfill({status:404}));
 await page.route('**/api/v1/auth/refresh',route=>json(route,{accessToken:'fixture',tokenType:'Bearer',expiresAtUtc:'2099-01-01T00:00:00Z'}));
 await page.route('**/api/v1/me',route=>json(route,{id,username:'fixture',displayName:'Tenant',tenantId:id,
  actorScope:`tenant:${id.replaceAll('-','')}`,scope:`tenant:${id.replaceAll('-','')}`,isSuperAdministrator:false,passwordChangeRequired:false,
  permissions:['import_export.import_tasks.read','import_export.import_tasks.create','import_export.static_schemas.read',
   'import_export.import_tasks.execute','organization.positions.import'],sessionId:id,preferredLocale:'zh-CN',profileVersion:1}));
 await page.route('**/api/v1/navigation',route=>json(route,[{id:'fixture',parentId:null,routeName:'import-export-tasks',path:'/import-export/tasks',
  componentKey:'import-export-tasks',title:'导入任务',caption:'',icon:'document',order:1,requiredPermission:'import_export.import_tasks.read',children:[]}]));
 const counters={lists:0,schemas:0};
 await page.route('**/api/v1/import-export/tasks?*',route=>{counters.lists++;return json(route,{items:withTask?[task]:[],page:1,pageSize:20,total:withTask?1:0});});
 await page.route('**/api/v1/import-export/schemas',route=>{counters.schemas++;return json(route,[{schemaKey:task.schemaKey,displayName:'租户职位',
  scopeKey:'tenant',requiredPermission:'organization.positions.import',worksheets:[{worksheetKey:'positions',displayName:'职位',headerColumns:['Code','Name']}]}]);});
 await page.goto('/#/import-export/tasks');await expect(page.getByTestId('import-export-task-create')).toBeEnabled();await expect.poll(()=>counters.lists).toBe(1);
 return counters;
}
async function openAndUpload(page){
 await page.getByTestId('import-export-task-create').click();await expect(page.getByTestId('import-create-schema')).toContainText('租户职位');
 // 受控字节仅验证客户端归属与 multipart，不作为服务端工作簿解析证据。
 await page.getByTestId('import-create-file').setInputFiles({name:'positions.xlsx',mimeType:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',buffer:Buffer.from('controlled-upload')});
}

for(const field of ['schemaKey','worksheetKey']){
 test(`创建拒绝错配${field}且可重试，不误开详情`,async({page})=>{
  const counters=await boot(page);let uploads=0;
  await page.route('**/api/v1/import-export/tasks',route=>{
   expect(route.request().method()).toBe('POST');expect(route.request().headers()['content-type']).toMatch(/^multipart\/form-data; boundary=/u);
   const body=route.request().postData();expect(body).toContain(task.schemaKey);expect(body).toContain('name="worksheetKey"');
   uploads++;return json(route,uploads===1?{...task,[field]:'another'}:task);
  });
  await openAndUpload(page);await page.getByTestId('import-create-submit').click();
  await expect(page.locator('.el-message--error')).toBeVisible();await expect(page.getByTestId('import-create-submit')).toBeEnabled();
  await expect(page.getByTestId('import-export-task-execute')).toHaveCount(0);await expect(page.locator('.el-message--success')).toHaveCount(0);
  expect(counters.lists).toBe(1);await page.getByTestId('import-create-submit').click();
  await expect(page.getByTestId('import-export-task-execute')).toBeVisible();expect(uploads).toBe(2);await expect.poll(()=>counters.lists).toBe(2);
 });
}

test('详情拒绝其他任务ID，原任务重新读取后仍可操作',async({page})=>{
 await boot(page,true);let reads=0;
 await page.route(`**/api/v1/import-export/tasks/${id}`,route=>{reads++;return json(route,reads===1?{...task,id:otherId,schemaDisplayName:'不应显示的新任务'}:task);});
 await page.getByTestId('import-export-task-detail').click();await expect(page.locator('.el-message--error')).toBeVisible();
 await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByText('不应显示的新任务')).toHaveCount(0);
 await page.getByTestId('import-export-task-detail').click();await expect(page.getByTestId('import-export-task-execute')).toBeVisible();expect(reads).toBe(2);
});

test('执行拒绝其他任务ID，不刷新或显示成功，重试接入原任务',async({page})=>{
 const counters=await boot(page,true);let executes=0;
 await page.route(`**/api/v1/import-export/tasks/${id}`,route=>json(route,task));
 await page.route(`**/api/v1/import-export/tasks/${id}/execute`,route=>{
  executes++;return json(route,executes===1?{...task,id:otherId,schemaDisplayName:'不应显示的新任务'}:{...task,statusKey:'execution_succeeded',version:2});
 });
 await page.getByTestId('import-export-task-detail').click();await page.getByTestId('import-export-task-execute').click();
 await expect(page.locator('.el-message--error')).toBeVisible();await expect(page.getByTestId('import-export-task-execute')).toBeEnabled();
 await expect(page.getByText('不应显示的新任务')).toHaveCount(0);await expect(page.locator('.el-message--success')).toHaveCount(0);expect(counters.lists).toBe(1);
 await page.getByTestId('import-export-task-execute').click();await expect(page.locator('.el-message--success')).toBeVisible();
 await expect(page.getByTestId('import-export-task-execute')).toHaveCount(0);expect(executes).toBe(2);await expect.poll(()=>counters.lists).toBe(2);
});

test('关闭在途上传不打开旧详情或刷新，重开清空文件并重读目录',async({page})=>{
 const counters=await boot(page);let uploads=0;let release;let finished=false;
 const pending=new Promise(resolve=>{release=resolve;});
 await page.route('**/api/v1/import-export/tasks',async route=>{uploads++;await pending;await json(route,task).catch(()=>{});finished=true;});
 await openAndUpload(page);await page.getByTestId('import-create-submit').click();await expect.poll(()=>uploads).toBe(1);
 await page.getByRole('dialog').getByRole('button',{name:'取消',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);
 release();await expect.poll(()=>finished).toBe(true);expect(counters.lists).toBe(1);
 await expect(page.locator('.el-message--success')).toHaveCount(0);await expect(page.getByTestId('import-export-task-execute')).toHaveCount(0);
 await page.getByTestId('import-export-task-create').click();await expect(page.getByTestId('import-create-schema')).toContainText('租户职位');
 await expect(page.getByTestId('import-create-file')).toHaveValue('');await expect.poll(()=>counters.schemas).toBe(2);
});
