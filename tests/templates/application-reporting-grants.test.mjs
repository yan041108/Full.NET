import assert from 'node:assert/strict';
import test from 'node:test';
import { verifyReportingGrantManagementHttp } from './support/application-reporting-grants.mjs';

const tenantId = '01980000-0000-7000-8000-000000000001';
test('独立应用授权验收拒绝版本串用，且重复授权只形成一条记录', async () => {
 let granted = false; let published = 0; let activeToken = 'Bearer HOST';
 const request = async (url, options) => {
  const path = new URL(url).pathname; const token = options.headers.Authorization;
  if (path.includes('tenant-grants') && token !== activeToken) return Response.json({}, {status:token ? 401 : 401});
  if (path.includes('tenant-grants') && activeToken === 'Bearer TENANT') return Response.json({}, {status:403});
  if (path.endsWith('/tenancy/context')) {
   assert.equal(token,activeToken);
   const tenant = JSON.parse(options.body).tenantId;
   activeToken = tenant === null ? 'Bearer HOST2' : 'Bearer TENANT';
   return Response.json({accessToken:tenant === null ? 'HOST2' : 'TENANT',context:{tenantId:tenant}});
  }
  if (path.endsWith('/publish')) return Response.json({definitionId:'definition',versionNumber:++published});
  if (path.endsWith('/definitions/definition')) return Response.json({id:'definition',version:2});
  if (path.endsWith('/groups')) return Response.json({id:'group'}, {status:201});
  if (path.endsWith('/data-sources')) return Response.json({id:'source'}, {status:201});
  if (path.endsWith('/definitions')) return Response.json({id:'definition',version:1}, {status:201});
  if (path.endsWith('/tenant-grants/' + tenantId)) { granted = options.method === 'PUT'; return Response.json(true); }
  if (path.endsWith('/tenant-grants')) {
   const active = granted && path.includes('/versions/1/');
   const secondPage = new URL(url).searchParams.get('page') === '2';
   return Response.json({items:active && !secondPage ? [tenantId] : [],page:secondPage?2:1,pageSize:1,total:active?'1':'0'});
  }
  throw new Error('Unexpected acceptance path: '+path);
 };
 const evidence = await verifyReportingGrantManagementHttp('http://localhost', {hostAccessToken:'HOST',tenantId,request});
 assert.equal(evidence.completed,true);assert.equal(granted,false);
 assert.doesNotMatch(JSON.stringify(evidence), /HOST|TENANT/u);
});
