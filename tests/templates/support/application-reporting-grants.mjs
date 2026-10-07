import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';

// 仅通过生成应用的正式 HTTP 入口验收版本授权；数据源只保存配置，不执行外部连接。
export async function verifyReportingGrantManagementHttp(baseUrl, {hostAccessToken, tenantId, signal, request = fetch}) {
 const evidence = {completed:false,responses:[]};
 let hostToken = hostAccessToken;
 const send = async (stage, path, method = 'GET', body, status = 200, token = hostToken) => {
  const headers = {Origin:'http://localhost'};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await request(baseUrl + path, {method, headers, body:body === undefined ? undefined : JSON.stringify(body), redirect:'error',
   signal:signal ? AbortSignal.any([signal,AbortSignal.timeout(15_000)]) : AbortSignal.timeout(15_000)});
  evidence.responses.push({stage,status:response.status});
  // 失败正文可能回显凭据，验收证据只保存阶段和状态码。
  assert.equal(response.status,status,stage + ': unexpected HTTP ' + response.status);
  return response.json();
 };
 const group = await send('group','/api/v1/reporting/groups','POST',{parentId:null,name:'Grant acceptance',sortOrder:10,isEnabled:true},201);
 const source = await send('source','/api/v1/reporting/data-sources','POST',{tenantId:null,name:'Grant metadata fixture',providerKey:'sql_server',
  serverHost:'localhost',port:1433,databaseName:'grant_metadata_fixture',username:'grant_fixture',password:randomUUID(),trustServerCertificate:false,isEnabled:true},201);
 const definition = await send('definition','/api/v1/reporting/definitions','POST',{groupId:group.id,dataSourceId:source.id,
  definitionKey:'acceptance_' + randomUUID().replaceAll('-',''),name:'Grant acceptance',description:null,
  queryPortKey:'reporting.database_engine_version',parameterSchema:[],layoutConfigJson:'{}',isEnabled:true},201);
 const root = `/api/v1/reporting/definitions/${definition.id}`;
 const first = await send('publish-first',root + '/publish','POST',{changeNote:'First frozen version',version:definition.version});
 assert.equal(first.definitionId,definition.id);assert.equal(first.versionNumber,1);
 const current = await send('draft-after-publish',root);
 const second = await send('publish-second',root + '/publish','POST',{changeNote:'Second isolated version',version:current.version});
 assert.equal(second.versionNumber,2);
 const list = root + '/versions/1/tenant-grants'; const grant = list + '/' + tenantId;
 const empty = await send('empty',list + '?page=1&pageSize=1');assert.deepEqual(empty.items,[]);assert.equal(Number(empty.total),0);
 const switched = await send('tenant-context','/api/v1/tenancy/context','PUT',{tenantId});
 assert.equal(switched.context?.tenantId,tenantId);assert.ok(switched.accessToken);
  await send('tenant-denied',list,'GET',undefined,403,switched.accessToken);
 const returned = await send('return-host','/api/v1/tenancy/context','PUT',{tenantId:null},200,switched.accessToken);
 assert.equal(returned.context?.tenantId,null);assert.ok(returned.accessToken);
 // 上下文切换使旧 scope 令牌失效，后续 Host 操作只使用最新签发令牌。
 hostToken = returned.accessToken;
 await send('anonymous-denied',list,'GET',undefined,401,null);
 assert.equal(await send('grant',grant,'PUT'),true);assert.equal(await send('duplicate',grant,'PUT'),true);
 const firstPage = await send('granted-page',list + '?page=1&pageSize=1');assert.deepEqual(firstPage.items,[tenantId]);assert.equal(Number(firstPage.total),1);
 const nextPage = await send('next-page',list + '?page=2&pageSize=1');assert.deepEqual(nextPage.items,[]);assert.equal(Number(nextPage.total),1);
 const otherVersion = await send('version-isolation',root + '/versions/2/tenant-grants?page=1&pageSize=1');assert.deepEqual(otherVersion.items,[]);assert.equal(Number(otherVersion.total),0);
 assert.equal(await send('revoke',grant,'DELETE'),true);
 const revoked = await send('revoked-page',list + '?page=1&pageSize=1');assert.deepEqual(revoked.items,[]);assert.equal(Number(revoked.total),0);
 evidence.completed = true;evidence.definitionId = definition.id;evidence.versionNumbers = [1,2];
 return evidence;
}
