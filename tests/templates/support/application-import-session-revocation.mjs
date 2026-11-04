import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {writeFileSync} from 'node:fs';
import {setTimeout as sleep} from 'node:timers/promises';
import {assertImportProgress} from './application-import-recovery.mjs';

// 登出以刷新 Cookie 撤销真实会话族；切租户轮换后必须使用最新 Cookie。
export function importSessionCookies(){
 const cookies=new Map();
 return {
  accept(headers){for(const line of headers.getSetCookie()){
   const pair=line.split(';')[0],index=pair.indexOf('=');if(index<1)continue;
   const key=pair.slice(0,index),value=pair.slice(index+1);if(value)cookies.set(key,value);else cookies.delete(key);
  }},
  logoutHeaders(){assert((cookies.get('__Host-fullnet-refresh')||cookies.get('fullnet-refresh'))&&cookies.get('fullnet-csrf'),'session cookies required for actual logout');return{Cookie:[...cookies].map(([key,value])=>key+'='+value).join('; '),'X-CSRF-Token':decodeURIComponent(cookies.get('fullnet-csrf'))};},
 };
}

// 验收只走正式 HTTP；真实 Worker 通过已有应用屏障暂停，不直接修改会话或业务表。
export async function verifyApplicationImportSessionRevocation(baseUrl,{fillWorkbook,startWorker,killWorker,stopWorker,waitProbe,logPath,signal,request=fetch}){
 const evidence={completed:false,responses:[],workers:[],forcedExits:[],scenarios:[],tasks:[]};
 const secrets=[];let token,jar;
 const redact=value=>secrets.reduce((text,key)=>text.replaceAll(key,'[REDACTED]'),String(value));
 const send=async(stage,path,method='GET',body,status=200,{binary=false,headers={},anonymous=false}={})=>{
  const response=await request(baseUrl+path,{method,headers:{...(!anonymous&&token?{Authorization:'Bearer '+token}:{}),Origin:'http://localhost',...(body!==undefined&&!(body instanceof FormData)?{'Content-Type':'application/json'}:{}),...headers},body:body===undefined||body instanceof FormData?body:JSON.stringify(body),redirect:'error',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(15000)]):AbortSignal.timeout(15000)});
  evidence.responses.push({stage,status:response.status});assert.equal(response.status,status,stage+': HTTP '+response.status);
  if(stage==='login'||stage==='context'){jar.accept(response.headers);for(const line of response.headers.getSetCookie()){const value=line.split(';')[0].split('=').slice(1).join('=');if(value)secrets.push(value);}}
  if(status===204)return;
  return binary?new Uint8Array(await response.arrayBuffer()):response.json();
 };
 const login=async tenantId=>{
  jar=importSessionCookies();token=undefined;
  const session=await send('login','/api/v1/auth/login','POST',{username:'admin',password:'FullNet!2026Secure'});assert(session.accessToken);token=session.accessToken;secrets.push(token);
  if(tenantId){const context=await send('context','/api/v1/tenancy/context','PUT',{tenantId});assert(context.accessToken);token=context.accessToken;secrets.push(token);}
 };
 // 与正式 SDK 清除本地 accessToken 后登出一致；会话族来自 Cookie，不能把租户 Bearer 注入匿名会话控制端点。
 const logout=()=>send('logout','/api/v1/auth/logout','POST',undefined,204,{headers:jar.logoutHeaders(),anonymous:true});
 const task=id=>send('task','/api/v1/import-export/tasks/'+id);
 const poll=async(id,status)=>{const end=Date.now()+150000;while(true){const current=await task(id);if(current.statusKey===status)return current;assert(['queued','executing'].includes(current.statusKey),'unexpected task state '+current.statusKey);assert(Date.now()<end,'session revocation timeout');await sleep(250,undefined,{signal});}};
 const workers=async(mode,id)=>{const worker=await startWorker(mode,id);evidence.workers.push({mode,pid:worker.pid,taskId:id});return worker;};
 const positions=()=>send('positions','/api/v1/organization/positions?page=1&pageSize=100');
 try{
  await login();const available=await send('tenants','/api/v1/tenancy/available');const tenant=available.find(item=>item.identifier==='local');assert(tenant);await login(tenant.id);
  const template=await send('template','/api/v1/import-export/schemas/organization.tenant_positions/worksheets/positions/template','GET',undefined,200,{binary:true});
  const create=async label=>{
   const codes=[1,2,3].map(index=>'session-'+label+'-'+index+'-'+randomUUID().replaceAll('-',''));
   const bytes=await fillWorkbook(template,codes.map((code,index)=>[code,'Session row '+(index+1),'10','','']));
   const form=new FormData();form.set('schemaKey','organization.tenant_positions');form.set('worksheetKey','positions');form.set('file',new Blob([bytes]),'session.xlsx');
   const preview=await send('preview','/api/v1/import-export/tasks','POST',form,201);assert.equal(preview.validRowCount,3);assert.equal(preview.invalidRowCount,0);assert.equal(preview.tenantId,tenant.id);
   const queued=await send('queue','/api/v1/import-export/tasks/'+preview.id+'/execute','POST');assertImportProgress(queued,{status:'queued',next:0,succeeded:0,failed:0,receipt:false});return{id:preview.id,codes};
  };
  const assertRows=async(current,count,firstId)=>{const list=await positions();for(let index=0;index<3;index++){const rows=list.items.filter(item=>item.code===current.codes[index]);assert.equal(rows.length,index<count?1:0,'business rows must match committed checkpoint');if(index===0&&firstId)assert.equal(rows[0].id,firstId);}return list.items.find(item=>item.code===current.codes[0])?.id;};
  const denied=async(id,next,succeeded)=>{const failed=await poll(id,'execution_failed');assertImportProgress(failed,{status:'execution_failed',next,succeeded,failed:0,receipt:false});assert.equal(failed.errorCode,'authorization.permission_denied');return failed;};
  const queued=await create('queued');await logout();await send('old-token-rejected','/api/v1/me','GET',undefined,401);await login(tenant.id);
  const first=await workers('observe',queued.id);await denied(queued.id,0,0);await assertRows(queued,0);await stopWorker(first);evidence.scenarios.push('queued-session-revocation-no-business-write');
  // 显式重试必须绑定新会话；旧会话撤销不能被后台自动重领绕过。
  await send('retry','/api/v1/import-export/tasks/'+queued.id+'/retry','POST');const retried=await workers('retry',queued.id);
  const completed=await poll(queued.id,'execution_succeeded');assertImportProgress(completed,{status:'execution_succeeded',next:3,succeeded:3,failed:0,receipt:false});await waitProbe('retry',retried.pid,queued.id);await assertRows(queued,3);await stopWorker(retried);evidence.scenarios.push('new-session-explicit-retry');

  const partial=await create('checkpoint');const held=await workers('checkpoint',partial.id);const checkpoint=await waitProbe('checkpoint',held.pid,partial.id);assert.equal(checkpoint.next,1);
  assertImportProgress(await task(partial.id),{status:'queued',next:1,succeeded:1,failed:0,receipt:false});const firstId=await assertRows(partial,1);
  await logout();await send('checkpoint-old-token-rejected','/api/v1/me','GET',undefined,401);
  const exit=await killWorker(held);assert(exit.forced&&exit.closed);assert.equal(exit.pid,held.pid);evidence.forcedExits.push(exit);
  await login(tenant.id);const resumed=await workers('observe',partial.id);await denied(partial.id,1,1);await assertRows(partial,1,firstId);await stopWorker(resumed);evidence.scenarios.push('checkpoint-session-revocation-preserves-committed-row');
  await send('retry','/api/v1/import-export/tasks/'+partial.id+'/retry','POST');const finalWorker=await workers('retry',partial.id);
  assertImportProgress(await poll(partial.id,'execution_succeeded'),{status:'execution_succeeded',next:3,succeeded:3,failed:0,receipt:false});await waitProbe('retry',finalWorker.pid,partial.id);await assertRows(partial,3,firstId);await stopWorker(finalWorker);evidence.scenarios.push('checkpoint-retry-keeps-row-idempotency');
  evidence.tasks=[{id:queued.id,codes:queued.codes},{id:partial.id,codes:partial.codes,firstId}];
  await login();await send('host-task-isolation','/api/v1/import-export/tasks/'+partial.id,'GET',undefined,403);
  await send('anonymous-task-rejected','/api/v1/import-export/tasks/'+partial.id,'GET',undefined,401,{anonymous:true});evidence.scenarios.push('host-and-anonymous-task-isolation');
  evidence.tenantId=tenant.id;evidence.completed=true;return evidence;
 }catch(error){evidence.error=redact(error.message??error);throw new Error(evidence.error);}
 finally{writeFileSync(logPath,redact(JSON.stringify(evidence,null,2)));}
}
