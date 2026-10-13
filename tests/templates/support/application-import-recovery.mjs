import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {writeFileSync} from 'node:fs';
import {setTimeout as sleep} from 'node:timers/promises';

// 检查点与回执身份必须独立验真，不能只依赖终态字符串。
export function assertImportProgress(task, expected) {
 assert.equal(task.statusKey,expected.status);
 assert.equal(task.nextLineNumber,expected.next);
 assert.equal(task.processedRowCount,expected.next);
 assert.equal(task.succeededRowCount,expected.succeeded);
 assert.equal(task.executionFailedRowCount,expected.failed);
 assert.equal(task.hasErrorReceipt,expected.receipt);
}
export function assertReceiptIdentity(before, after, reused) {
 for(const id of [before,after])assert.match(id??'',/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu);
 if(reused)assert.equal(before,after,'same-result recovery must reuse receipt');
 else assert.notEqual(before,after,'changed-result retry must use a new receipt');
}

// 应用验收消费者拥有检查点屏障；所有业务判断仍使用正式 HTTP 与真实文件下载。
export async function verifyApplicationImportRecovery(baseUrl,{hostAccessToken,fillWorkbook,inspectReceipt,startWorker,killWorker,stopWorker,waitProbe,logPath,signal,request=fetch}) {
 const evidence={completed:false,responses:[],workers:[],forcedExits:[],scenarios:[]};
 const secrets=[hostAccessToken];let token=hostAccessToken;
 const redact=text=>secrets.reduce((s,key)=>s.replaceAll(key,'[REDACTED]'),String(text));
 const send=async(stage,path,method='GET',body,status=200,binary=false)=>{
  const response=await request(baseUrl+path,{method,headers:{Authorization:'Bearer '+token,Origin:'http://localhost',...(body!==undefined&&!(body instanceof FormData)?{'Content-Type':'application/json'}:{})},body:body===undefined||body instanceof FormData?body:JSON.stringify(body),redirect:'error',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(15000)]):AbortSignal.timeout(15000)});
  evidence.responses.push({stage,status:response.status});assert.equal(response.status,status,stage+': HTTP '+response.status);
  if(binary)return {bytes:new Uint8Array(await response.arrayBuffer()),disposition:response.headers.get('content-disposition')};
  return response.json();
 };
 let taskId;
 const task=()=>send('task','/api/v1/import-export/tasks/'+taskId);
 const poll=async(predicate)=>{
  const deadline=Date.now()+150000;
  while(true){const current=await task();if(predicate(current))return current;assert(Date.now()<deadline,'import recovery timeout');await sleep(250,undefined,{signal});}
 };
 const start=async mode=>{const worker=await startWorker(mode,taskId);assert(Number.isInteger(worker.pid));evidence.workers.push({mode,pid:worker.pid});assert.equal(new Set(evidence.workers.map(x=>x.pid)).size,evidence.workers.length);return worker;};
 const force=async worker=>{const exit=await killWorker(worker);assert.equal(exit.pid,worker.pid);assert.equal(exit.forced,true);assert.equal(exit.closed,true);evidence.forcedExits.push(exit);};
 const verifyDownload=async code=>{
  const file=await send('receipt','/api/v1/import-export/tasks/'+taskId+'/error-receipt','GET',undefined,200,true);
  assert(file.disposition?.includes('recovery-errors.xlsx'),'friendly receipt filename');
  const rows=await inspectReceipt(file.bytes);assert.equal(rows.length,1);assert.equal(rows[0][0],'2');assert.equal(rows[0][1],code);
  return rows;
 };
 try{
  const available=await send('tenants','/api/v1/tenancy/available');const tenant=available.find(x=>x.identifier==='local');assert(tenant);
  const context=await send('context','/api/v1/tenancy/context','PUT',{tenantId:tenant.id});token=context.accessToken;assert(typeof token==='string'&&token.trim());secrets.push(token);
  const actor=await send('actor','/api/v1/me');
  const units=[];
  for(const label of ['good','disabled']){
   const code='recovery-'+label+'-'+randomUUID().replaceAll('-','');
   const unit=await send('unit-'+label,'/api/v1/organization/units','POST',{parentId:null,code,name:'Recovery '+label,displayOrder:10},201);
   units.push({...unit,code});await send('membership-'+label,'/api/v1/organization/user-units','POST',{userId:actor.id,unitId:unit.id,isPrimary:false},201);
  }
  const levelCode='recovery-level-'+randomUUID().replaceAll('-','');
  await send('level','/api/v1/organization/position-levels','POST',{code:levelCode,name:'Recovery level',displayOrder:10},201);
  const codes=[1,2,3].map(i=>'recovery-'+i+'-'+randomUUID().replaceAll('-',''));
  const template=await send('template','/api/v1/import-export/schemas/organization.tenant_positions/worksheets/positions/template','GET',undefined,200,true);
  const rows=codes.map((code,i)=>[code,'Recovery row '+(i+1),'10',units[i===1?1:0].code,levelCode]);
  const workbook=await fillWorkbook(template.bytes,rows);
  const form=new FormData();form.set('schemaKey','organization.tenant_positions');form.set('worksheetKey','positions');form.set('file',new Blob([workbook]),'recovery.xlsx');
  const preview=await send('preview','/api/v1/import-export/tasks','POST',form,201);taskId=preview.id;
  assert.equal(preview.tenantId,tenant.id);assert.equal(preview.validRowCount,3);assert.equal(preview.invalidRowCount,0);
  assert.deepEqual(preview.previewRows.map(x=>x.lineNumber),[1,2,3]);
  const queued=await send('queue','/api/v1/import-export/tasks/'+taskId+'/execute','POST');assert.equal(queued.statusKey,'queued');
  await send('disable','/api/v1/organization/units/'+units[1].id+'/disable','POST');

  const first=await start('checkpoint');const checkpoint=await waitProbe('checkpoint',first.pid,taskId);assert.equal(checkpoint.next,1);
  const firstList=await send('checkpoint-positions','/api/v1/organization/positions?page=1&pageSize=100');
  const firstMatches=firstList.items.filter(x=>x.code===codes[0]);assert.equal(firstMatches.length,1);const firstId=firstMatches[0].id;
  assertImportProgress(await task(),{status:'queued',next:1,succeeded:1,failed:0,receipt:false});await force(first);
  assertImportProgress(await task(),{status:'queued',next:1,succeeded:1,failed:0,receipt:false});evidence.scenarios.push('committed-checkpoint-hard-exit');

  const second=await start('receipt');const uploaded=await waitProbe('receipt',second.pid,taskId);assert.equal(uploaded.next,3);assertReceiptIdentity(uploaded.receiptId,uploaded.receiptId,true);
  const beforeCommit=await send('before-progress-positions','/api/v1/organization/positions?page=1&pageSize=100');
  const thirdMatches=beforeCommit.items.filter(x=>x.code===codes[2]);assert.equal(thirdMatches.length,1);const thirdId=thirdMatches[0].id;
  assertImportProgress(await task(),{status:'executing',next:2,succeeded:1,failed:1,receipt:false});await force(second);
  assertImportProgress(await task(),{status:'executing',next:2,succeeded:1,failed:1,receipt:false});evidence.scenarios.push('receipt-upload-before-progress-hard-exit');

  const third=await start('observe');const completed=await poll(x=>x.statusKey==='execution_partial');
  assertImportProgress(completed,{status:'execution_partial',next:3,succeeded:2,failed:1,receipt:true});
  const recovered=await waitProbe('observe',third.pid,taskId);assertReceiptIdentity(uploaded.receiptId,recovered.receiptId,true);
  await verifyDownload('organization.units.not_found');await stopWorker(third);
  const list=await send('positions','/api/v1/organization/positions?page=1&pageSize=100');
  for(const code of [codes[0],codes[2]])assert.equal(list.items.filter(x=>x.code===code).length,1,'successful row must be written once');
  assert.equal(list.items.find(x=>x.code===codes[0]).id,firstId);assert.equal(list.items.find(x=>x.code===codes[2]).id,thirdId);
  assert.equal(list.items.filter(x=>x.code===codes[1]).length,0);evidence.scenarios.push('recovery-reuses-receipt-and-row-idempotency');

  await send('competing-position','/api/v1/organization/positions','POST',{code:codes[1],name:'Competing recovery row',displayOrder:10},201);
  const retry=await send('retry','/api/v1/import-export/tasks/'+taskId+'/retry','POST');
  assertImportProgress(retry,{status:'queued',next:0,succeeded:0,failed:0,receipt:false});
  const fourth=await start('retry');const retried=await poll(x=>x.statusKey==='execution_partial');
  assertImportProgress(retried,{status:'execution_partial',next:3,succeeded:2,failed:1,receipt:true});
  const newReceipt=await waitProbe('retry',fourth.pid,taskId);assertReceiptIdentity(recovered.receiptId,newReceipt.receiptId,false);
  await verifyDownload('organization.positions.code_exists');await stopWorker(fourth);
  const finalList=await send('final-positions','/api/v1/organization/positions?page=1&pageSize=100');
  for(const code of codes)assert.equal(finalList.items.filter(x=>x.code===code).length,1);
  assert.equal(finalList.items.find(x=>x.code===codes[0]).id,firstId);assert.equal(finalList.items.find(x=>x.code===codes[2]).id,thirdId);
  evidence.scenarios.push('explicit-retry-new-failure-receipt');evidence.taskId=taskId;evidence.tenantId=tenant.id;
  evidence.receiptIds={uploaded:uploaded.receiptId,recovered:recovered.receiptId,retried:newReceipt.receiptId};evidence.completed=true;return evidence;
 }catch(error){evidence.error=redact(error.message??error);throw new Error(evidence.error);}
 finally{writeFileSync(logPath,redact(JSON.stringify(evidence,null,2)));}
}
