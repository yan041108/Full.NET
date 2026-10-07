import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verifyEnterpriseDataOutputHttp } from './support/application-enterprise-data-output.mjs';

const tenantId = '01980000-0000-7000-8000-000000000001';
const definitionId = '01980000-0000-7000-8000-000000000002';
const taskId = '01980000-0000-7000-8000-000000000003';
const templateId = '01980000-0000-7000-8000-000000000004';

function fixture({wrongVersion=false, emptyQuery=false, corruptWorkbook=false, printingAllowed=false, revokedAllowed=false, leakedCatalog=false, failedHttp=false, invalidId=false, wrongTaskDefinition=false, revokedSession=false, wrongPrintingVersion=false, wrongPrintingTenant=false, revokedPrintingAllowed=false} = {}) {
 const root = mkdtempSync(join(tmpdir(),'enterprise-output-http-'));
 const logPath = join(root,'result.json');
 let printGranted = false; let granted = false; let published = 0; let activeToken = 'Bearer HOST_SECRET'; let checkedWorkbook = false;
 const requests = [];
 let revokerCredentials;
 const roleId='01980000-0000-7000-8000-000000000007';
 const userId='01980000-0000-7000-8000-000000000008';
 const source = {providerKey:'sql_server',serverHost:'127.0.0.1',port:1433,databaseName:'master',username:'sa',password:'DATABASE_SECRET',trustServerCertificate:true};
 const request = async (url, options) => {
  const path = new URL(url).pathname; const method = options.method;
  const input = options.body ? JSON.parse(options.body) : undefined;
  requests.push({path,method,input});
  const token = options.headers.Authorization;
  if (!token) return Response.json({code:'authentication.required'}, {status:401});
  assert.equal(token, (path.includes('tenant-grants') && method==='DELETE') || path.endsWith('/me/password') ? 'Bearer REVOKER_SECRET' : activeToken);
  if (revokedSession && !granted && path.endsWith('/download')) return Response.json({code:'authentication.required'}, {status:401});
  if (path==='/api/v1/identity/roles' && method==='POST') { assert.match(input.code,/^[a-z][a-z0-9-]{2,63}$/u); return Response.json({id:roleId,version:1},{status:201}); }
  if (path===`/api/v1/identity/roles/${roleId}/permissions`) {
   assert.deepEqual(input.permissionCodes,['reporting.definitions.grant_tenants','printing.templates.read','printing.templates.grant_tenants']); assert.equal(input.version,1);
   return Response.json({id:roleId,version:2,permissionCodes:input.permissionCodes});
  }
  if (path==='/api/v1/identity/users' && method==='POST') { revokerCredentials={username:input.username,password:input.password}; return Response.json({id:userId,version:1},{status:201}); }
  if (path===`/api/v1/identity/users/${userId}/roles`) {
   if(method==='PUT') assert.deepEqual(input,{roleIds:[roleId],version:1});
   return Response.json({userId,roleIds:method==='PUT'?[roleId]:[],version:1});
  }
  if (path==='/api/v1/me/password') { assert.equal(options.headers.Cookie,'fullnet-csrf=REVOKER_CSRF_SECRET'); assert.equal(options.headers['X-CSRF-Token'],'REVOKER_CSRF_SECRET'); assert.equal(input.currentPassword,revokerCredentials.password); assert.notEqual(input.newPassword,input.currentPassword); return Response.json({accessToken:'REVOKER_SECRET'}); }
  if (path.endsWith('/groups')) return Response.json({id:'01980000-0000-7000-8000-000000000005'}, {status:201});
  if (path.endsWith('/data-sources')) { assert.equal(input.password,source.password); return Response.json({id:'01980000-0000-7000-8000-000000000006'}, {status:201}); }
  if (path.endsWith('/definitions') && method==='POST') return Response.json({id:definitionId,version:1}, {status:201});
  if (path===`/api/v1/reporting/definitions/${definitionId}`) return Response.json({id:definitionId,version:2});
  if (path.endsWith('/publish') && path.includes('/reporting/')) return Response.json({definitionId,versionNumber:++published});
  if (path.includes('/printing/') && path.includes('tenant-grants')) { printGranted=method==='PUT'; return Response.json(true); }
  if (path.includes('tenant-grants')) { granted=method==='PUT'; return Response.json(true); }
  if (path.endsWith('/tenancy/context')) {
   assert.equal(input.tenantId,tenantId); activeToken='Bearer TENANT_SECRET';
   return Response.json({accessToken:'TENANT_SECRET',context:{tenantId,identifier:'local',name:'Local tenant'}});
  }
  if (path.endsWith('/tenancy/available')) return Response.json([{id:tenantId,identifier:'local',name:'Local tenant'}]);
  if (path.endsWith('/published-definitions')) return Response.json(granted ? [{definitionId,versionNumber:1,
   ...(leakedCatalog ? {serverHost:source.serverHost,passwordProtected:'secret'} : {})}] : []);
  if (path.endsWith('/definitions') && method==='GET') return Response.json({code:'authorization.permission_denied'}, {status:403});
  if (path.endsWith('/execute')) {
   if (input.versionNumber===2 || (!granted && !revokedAllowed)) return Response.json({code:'authorization.permission_denied'}, {status:403});
   if (failedHttp) return Response.json({detail:'DATABASE_SECRET TENANT_SECRET unknown-password'}, {status:500});
   return Response.json({definitionId,versionNumber:wrongVersion?2:1,columns:[{columnKey:'EngineVersion'}],
    rows:emptyQuery?[]:[{values:{EngineVersion:'16.0.4135.4'}}]});
  }
  if (path.endsWith('/export-tasks') && method==='POST') {
   if (!granted && !revokedAllowed) return Response.json({code:'authorization.permission_denied'}, {status:403});
   return Response.json({id:invalidId?'DATABASE_SECRET':taskId,definitionId,versionNumber:1,statusKey:'succeeded',rowCount:1}, {status:201});
  }
  if(path===`/api/v1/reporting/export-tasks/${taskId}` && method==='GET') return Response.json({id:taskId,definitionId:wrongTaskDefinition?templateId:definitionId,versionNumber:1,statusKey:'succeeded',rowCount:1});
  if (path.endsWith('/download')) {
   if (!granted && !revokedAllowed) return Response.json({code:'authorization.permission_denied'}, {status:403});
   return new Response(new Uint8Array(corruptWorkbook?[80,75,0]:[80,75,1]), {headers:{'Content-Type':'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'}});
  }
  if (path.endsWith('/printing/templates') && method==='POST') return Response.json({id:templateId,version:1}, {status:201});
  if (path.endsWith('/publish') && path.includes('/printing/')) return Response.json({templateId,versionNumber:1,layoutHtml:'<article>{{tenantName}} / {{tenantCode}}</article>'});
  if (path.endsWith('/published-templates')) return Response.json(printGranted?[{templateId,versionNumber:1}]:[]);
  if (path.includes('/published-templates/') && path.endsWith('/preview')) {
   if(input.versionNumber===2 || (!printGranted && !revokedPrintingAllowed)) return Response.json({code:'authorization.permission_denied'},{status:403});
   return Response.json({templateId,versionNumber:wrongPrintingVersion?2:1,formSchemaKey:'printing.tenant_profile_card',
    boundFields:{tenantName:'Local tenant',tenantCode:wrongPrintingTenant?'other':'local'},html:'<article>Local tenant / local</article>'});
  }
  if (path.endsWith('/preview')) return Response.json({code:'authorization.permission_denied'}, {status:printingAllowed?200:403});
  throw new Error('Unexpected output request: '+method+' '+path);
 };
 return {
  run: () => verifyEnterpriseDataOutputHttp('http://localhost',{hostAccessToken:'HOST_SECRET',tenantId,externalDataSource:source,logPath,request,
   loginHost:async(credentials)=> { assert.deepEqual(credentials,revokerCredentials,'revoker must log in as a distinct owned account'); assert.ok(credentials?.username && credentials.username!=='admin'); return {accessToken:'REVOKER_SECRET',csrfToken:'REVOKER_CSRF_SECRET'}; },verifyWorkbook:async(bytes, expected)=>{
    assert.equal(expected,'16.0.4135.4'); assert.deepEqual([...bytes],[80,75,1],'invalid workbook'); checkedWorkbook=true;
    return {worksheets:1,dataRows:1};
   }}),
  report:()=>JSON.parse(readFileSync(logPath,'utf8')),
  checked:()=>checkedWorkbook,
  requests,
  cleanup:()=>rmSync(root,{recursive:true,force:true}),
 };
}

test('独立应用联合验证精确版本查询、工作簿校验、撤销后拒绝及既有打印权限边界',async()=>{
 const f=fixture();
 try {
  const r=await f.run();assert.equal(r.completed,true);assert.equal(f.checked(),true);
  assert.equal(r.reporting.versionNumber,1);assert.equal(r.reporting.downloadVerified,true);assert.equal(r.reporting.revokedAccessDenied,true);
  assert.equal(r.printing.hostPublished,true);assert.equal(r.printing.tenantPreviewDenied,true);assert.equal(r.printing.status,'tenant-published-preview-verified');assert.equal(r.printing.completed,true);assert.equal(r.printing.revokedAccessDenied,true);
  assert.doesNotMatch(JSON.stringify(f.report()),/HOST_SECRET|TENANT_SECRET|REVOKER_SECRET|REVOKER_CSRF_SECRET|DATABASE_SECRET|16\.0\.4135\.4/u);
  assert.equal(f.requests.filter(r=>r.path.endsWith('/download')).length,2);
 } finally { f.cleanup(); }
});

for (const [name,options,pattern] of [
 ['拒绝打印串用未获授版本',{wrongPrintingVersion:true},/printing version/iu],
 ['拒绝打印绑定其他租户',{wrongPrintingTenant:true},/printing binding/iu],
 ['拒绝打印撤权后仍可预览',{revokedPrintingAllowed:true},/HTTP 200/u],
 ['拒绝报表执行串用未获授版本',{wrongVersion:true},/version/iu],
 ['拒绝空查询冒充实际外部查询',{emptyQuery:true},/row/iu],
 ['拒绝仅有 ZIP 前缀的损坏工作簿',{corruptWorkbook:true},/workbook/iu],
 ['租户打印预览不得被错误放行',{printingAllowed:true},/HTTP 200/u],
 ['会话失效的 401 不得冒充撤权 403',{revokedSession:true},/HTTP 401/u],
 ['拒绝撤销后仍可访问旧结果',{revokedAllowed:true},/HTTP 200/u],
 ['拒绝发布目录泄露连接配置',{leakedCatalog:true},/catalog/iu],
 ['失败证据不保存响应中的未知密码',{failedHttp:true},/HTTP 500/u],
 ['错误对象 ID 不得带入报告',{invalidId:true},/identifier/iu],
 ['当前租户的任务读回不得串用其他报表',{wrongTaskDefinition:true},/task identity/iu],
]) test(name,async()=>{
 const f=fixture(options);
 try {
  await assert.rejects(f.run(),pattern);assert.equal(f.report().completed,false);
  assert.doesNotMatch(JSON.stringify(f.report()),/HOST_SECRET|TENANT_SECRET|REVOKER_SECRET|REVOKER_CSRF_SECRET|DATABASE_SECRET|unknown-password/u);
 } finally { f.cleanup(); }
});
