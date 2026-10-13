import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {writeFileSync} from 'node:fs';
import {setTimeout as sleep} from 'node:timers/promises';
import {assertImportProgress} from './application-import-recovery.mjs';

// 取消验收只调用正式 HTTP；检查点屏障只暂停自有 Worker，不写任务终态或业务行。
export async function verifyApplicationImportCancellation(baseUrl,{hostAccessToken,fillWorkbook,startWorker,stopWorker,waitProbe,logPath,signal,request=fetch}){
 const evidence={completed:false,responses:[],workers:[],scenarios:[],tasks:[]};
 const secrets=[hostAccessToken];let token=hostAccessToken;
 const send=async(stage,path,method='GET',body,status=200,{binary=false,anonymous=false}={})=>{
  const response=await request(baseUrl+path,{method,headers:{...(!anonymous?{Authorization:'Bearer '+token}:{}),Origin:'http://localhost',...(body!==undefined&&!(body instanceof FormData)?{'Content-Type':'application/json'}:{})},body:body===undefined||body instanceof FormData?body:JSON.stringify(body),redirect:'error',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(15000)]):AbortSignal.timeout(15000)});
  evidence.responses.push({stage,status:response.status});assert.equal(response.status,status,stage+': HTTP '+response.status);
  if(status===204)return;return binary?new Uint8Array(await response.arrayBuffer()):response.json();
 };
 const task=id=>send('task','/api/v1/import-export/tasks/'+id);
 const poll=async id=>{const end=Date.now()+150000;while(true){const current=await task(id);if(current.statusKey==='execution_succeeded')return current;assert(['queued','executing'].includes(current.statusKey));assert(Date.now()<end,'control task timeout');await sleep(250,undefined,{signal});}};
 const worker=async(mode,id)=>{const item=await startWorker(mode,id);evidence.workers.push({mode,pid:item.pid,taskId:id});return item;};
 try{
  const available=await send('tenants','/api/v1/tenancy/available');const tenant=available.find(x=>x.identifier==='local');assert(tenant);
  const context=await send('context','/api/v1/tenancy/context','PUT',{tenantId:tenant.id});assert(context.accessToken);token=context.accessToken;secrets.push(token);
  const template=await send('template','/api/v1/import-export/schemas/organization.tenant_positions/worksheets/positions/template','GET',undefined,200,{binary:true});
  const create=async label=>{
   const codes=[1,2,3].map(n=>'cancel-'+label+'-'+n+'-'+randomUUID().replaceAll('-',''));
   const bytes=await fillWorkbook(template,codes.map((code,n)=>[code,'Cancellation row '+(n+1),'10','','']));
   const form=new FormData();form.set('schemaKey','organization.tenant_positions');form.set('worksheetKey','positions');form.set('file',new Blob([bytes]),'cancel.xlsx');
   const result=await send('preview','/api/v1/import-export/tasks','POST',form,201);assert.equal(result.validRowCount,3);assert.equal(result.tenantId,tenant.id);return{id:result.id,codes,preview:result};
  };
  const queue=async current=>send('queue','/api/v1/import-export/tasks/'+current.id+'/execute','POST');
  const cancel=async(current,next)=>{
   const before=await task(current.id),result=await send('cancel','/api/v1/import-export/tasks/'+current.id+'/cancel','POST');
   assertImportProgress(result,{status:'cancelled',next,succeeded:next,failed:0,receipt:false});
   assert.equal(result.version,before.version+1);assert(result.executionCompletedAtUtc);
   const repeated=await send('cancel-repeated','/api/v1/import-export/tasks/'+current.id+'/cancel','POST');assert.deepEqual(repeated,result);
   for(const action of ['execute','resume','retry']){
    const error=await send('cancelled-'+action,'/api/v1/import-export/tasks/'+current.id+'/'+action,'POST',undefined,422);assert.equal(error.code,'import_export.task.status_invalid');
   }
   return result;
  };
  const assertRows=async(current,count,firstId)=>{
   const list=await send('positions','/api/v1/organization/positions?page=1&pageSize=100');
   for(let index=0;index<3;index++){const rows=list.items.filter(x=>x.code===current.codes[index]);assert.equal(rows.length,index<count?1:0);if(index===0&&firstId)assert.equal(rows[0].id,firstId);}
   return list.items.find(x=>x.code===current.codes[0])?.id;
  };
  const preview=await create('preview');await cancel(preview,0);await assertRows(preview,0);evidence.scenarios.push('preview-cancellation-terminal-and-idempotent');
  const queued=await create('queued');await queue(queued);await cancel(queued,0);evidence.scenarios.push('queued-cancellation-prevents-claim');
  const checkpoint=await create('checkpoint');await queue(checkpoint);const held=await worker('checkpoint',checkpoint.id);
  const probe=await waitProbe('checkpoint',held.pid,checkpoint.id);assert.equal(probe.next,1);const firstId=await assertRows(checkpoint,1);
  await cancel(checkpoint,1);await stopWorker(held);evidence.scenarios.push('checkpoint-cancellation-preserves-committed-row');
  const control=await create('control');await queue(control);const running=await worker('observe',control.id);
  assertImportProgress(await poll(control.id),{status:'execution_succeeded',next:3,succeeded:3,failed:0,receipt:false});await waitProbe('observe',running.pid,control.id);
  for(const[current,count]of [[preview,0],[queued,0],[checkpoint,1]]){assert.equal((await task(current.id)).statusKey,'cancelled');await assertRows(current,count,current===checkpoint?firstId:undefined);}
  const conflict=await send('completed-cancel-conflict','/api/v1/import-export/tasks/'+control.id+'/cancel','POST',undefined,409);assert.equal(conflict.code,'import_export.task.status_invalid');await stopWorker(running);
  evidence.scenarios.push('new-worker-excludes-cancelled-and-continues-control');
  await send('anonymous-cancel-rejected','/api/v1/import-export/tasks/'+checkpoint.id+'/cancel','POST',undefined,401,{anonymous:true});
  const host=await send('host-login','/api/v1/auth/login','POST',{username:'admin',password:'FullNet!2026Secure'});token=host.accessToken;secrets.push(token);
  await send('host-cancel-isolation','/api/v1/import-export/tasks/'+checkpoint.id+'/cancel','POST',undefined,403);evidence.scenarios.push('host-and-anonymous-cancellation-isolation');
  evidence.tasks=[{id:preview.id,codes:preview.codes},{id:queued.id,codes:queued.codes},{id:checkpoint.id,codes:checkpoint.codes,firstId},{id:control.id,codes:control.codes}];evidence.tenantId=tenant.id;evidence.completed=true;return evidence;
 }catch(error){evidence.error=secrets.reduce((text,key)=>text.replaceAll(key,'[REDACTED]'),String(error.message??error));throw new Error(evidence.error);}
 finally{writeFileSync(logPath,secrets.reduce((text,key)=>text.replaceAll(key,'[REDACTED]'),JSON.stringify(evidence,null,2)));}
}
