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

function fixture({wrongVersion=false, emptyQuery=false, corruptWorkbook=false, wrongBinding=false, revokedAllowed=false, leakedCatalog=false, failedHttp=false} = {}) {
 const root = mkdtempSync(join(tmpdir(),'enterprise-output-http-'));
 const logPath = join(root,'result.json');
 let granted = false; let published = 0; let activeToken = 'Bearer HOST_SECRET'; let checkedWorkbook = false;
 const requests = [];
 const source = {providerKey:'sql_server',serverHost:'127.0.0.1',port:1433,databaseName:'master',username:'sa',password:'DATABASE_SECRET',trustServerCertificate:true};
 const request = async (url, options) => {
  const path = new URL(url).pathname; const method = options.method;
  const input = options.body ? JSON.parse(options.body) : undefined;
  requests.push({path,method,input});
  const token = options.headers.Authorization;
  if (!token) return Response.json({code:'authentication.required'}, {status:401});
  assert.equal(token, path.includes('tenant-grants') && method==='DELETE' ? 'Bearer REVOKER_SECRET' : activeToken);
  if (path.endsWith('/groups')) return Response.json({id:'group'}, {status:201});
  if (path.endsWith('/data-sources')) { assert.equal(input.password,source.password); return Response.json({id:'source'}, {status:201}); }
  if (path.endsWith('/definitions') && method==='POST') return Response.json({id:definitionId,version:1}, {status:201});
  if (path===`/api/v1/reporting/definitions/${definitionId}`) return Response.json({id:definitionId,version:2});
  if (path.endsWith('/publish') && path.includes('/reporting/')) return Response.json({definitionId,versionNumber:++published});
  if (path.includes('tenant-grants')) { granted=method==='PUT'; return Response.json(true); }
  if (path.endsWith('/tenancy/context')) {
   assert.equal(input.tenantId,tenantId); activeToken='Bearer TENANT_SECRET';
   return Response.json({accessToken:'TENANT_SECRET',context:{tenantId}});
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
   return Response.json({id:taskId,definitionId,tenantId,versionNumber:1,statusKey:'succeeded',rowCount:1}, {status:201});
  }
  if (path.endsWith('/download')) {
   if (!granted && !revokedAllowed) return Response.json({code:'authorization.permission_denied'}, {status:403});
   return new Response(new Uint8Array(corruptWorkbook?[80,75,0]:[80,75,1]), {headers:{'Content-Type':'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'}});
  }
  if (path.endsWith('/printing/templates') && method==='POST') return Response.json({id:templateId,version:1}, {status:201});
  if (path.endsWith('/publish') && path.includes('/printing/')) return Response.json({templateId,versionNumber:1});
  if (path.endsWith('/preview')) return Response.json({templateId,versionNumber:1,formSchemaKey:'printing.tenant_profile_card',
   html:'<article>Local tenant / local</article>',boundFields:{tenantName:wrongBinding?'Other tenant':'Local tenant',tenantCode:'local',tenantDomain:'localhost'}});
  throw new Error('Unexpected output request: '+method+' '+path);
 };
 return {
  run: () => verifyEnterpriseDataOutputHttp('http://localhost',{hostAccessToken:'HOST_SECRET',tenantId,externalDataSource:source,logPath,request,
   loginHost:async()=> 'REVOKER_SECRET',verifyWorkbook:async(bytes, expected)=>{
    assert.equal(expected,'16.0.4135.4'); assert.deepEqual([...bytes],[80,75,1],'invalid workbook'); checkedWorkbook=true;
    return {worksheets:1,dataRows:1};
   }}),
  report:()=>JSON.parse(readFileSync(logPath,'utf8')),
  checked:()=>checkedWorkbook,
  requests,
  cleanup:()=>rmSync(root,{recursive:true,force:true}),
 };
}

test('独立应用联合验证精确版本查询、真实工作簿校验、撤销后拒绝和可信租户打印绑定',async()=>{
 const f=fixture();
 try {
  const r=await f.run();assert.equal(r.completed,true);assert.equal(f.checked(),true);
  assert.equal(r.reporting.versionNumber,1);assert.equal(r.reporting.downloadVerified,true);assert.equal(r.reporting.revokedAccessDenied,true);
  assert.equal(r.printing.completed,true);assert.equal(r.printing.bindingVerified,true);
  assert.doesNotMatch(JSON.stringify(f.report()),/HOST_SECRET|TENANT_SECRET|REVOKER_SECRET|DATABASE_SECRET|16\.0\.4135\.4/u);
  assert.equal(f.requests.filter(r=>r.path.endsWith('/download')).length,2);
 } finally { f.cleanup(); }
});

for (const [name,options,pattern] of [
 ['拒绝报表执行串用未获授版本',{wrongVersion:true},/version/iu],
 ['拒绝空查询冒充实际外部查询',{emptyQuery:true},/row/iu],
 ['拒绝仅有 ZIP 前缀的损坏工作簿',{corruptWorkbook:true},/workbook/iu],
 ['拒绝其他租户的打印绑定',{wrongBinding:true},/tenant/iu],
 ['拒绝撤销后仍可访问旧结果',{revokedAllowed:true},/HTTP 200/u],
 ['拒绝发布目录泄露连接配置',{leakedCatalog:true},/catalog/iu],
 ['失败证据不保存响应中的未知密码',{failedHttp:true},/HTTP 500/u],
]) test(name,async()=>{
 const f=fixture(options);
 try {
  await assert.rejects(f.run(),pattern);assert.equal(f.report().completed,false);
  assert.doesNotMatch(JSON.stringify(f.report()),/HOST_SECRET|TENANT_SECRET|REVOKER_SECRET|DATABASE_SECRET|unknown-password/u);
 } finally { f.cleanup(); }
});
