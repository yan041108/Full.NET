import assert from 'node:assert/strict';
import {createHash,randomUUID} from 'node:crypto';
import {writeFileSync} from 'node:fs';
import {setTimeout as sleep} from 'node:timers/promises';
import {assertImportProgress,assertReceiptIdentity} from './application-import-recovery.mjs';

// 取消只改变终态、版本和完成时间；逐行结果、错误码与来源快照必须原样保留。
export function assertCancelledResultRetained(before,after) {
 assert(['execution_partial','execution_failed'].includes(before.statusKey));
 assert.equal(after.statusKey,'cancelled');
 assert.equal(before.hasErrorReceipt,true);assert(before.executionFailedRowCount>0);
 const revision=value=>{
  assert(typeof value==='string'&&/^[1-9]\d*$/.test(value)||typeof value==='number'&&Number.isSafeInteger(value)&&value>0,'exact task version required');
  return BigInt(value);
 };
 assert.equal(revision(after.version),revision(before.version)+1n);
 assert(typeof after.executionCompletedAtUtc==='string'&&Number.isFinite(Date.parse(after.executionCompletedAtUtc)));
 const immutable=value=>Object.fromEntries(Object.entries(value).filter(([key])=>!['statusKey','version','executionCompletedAtUtc'].includes(key)));
 assert.deepEqual(immutable(after),immutable(before));
}

// 使用实际下载字节校验回执保留，不以 hasErrorReceipt 布尔值替代文件内容。
export function assertReceiptDownloadRetained(before,after) {
 assert(before.bytes instanceof Uint8Array&&before.bytes.length>0);
 assert(after.bytes instanceof Uint8Array&&after.bytes.length>0);
 assert(typeof before.disposition==='string'&&before.disposition.length>0);
 assert.equal(after.disposition,before.disposition);assert.deepEqual(after.bytes,before.bytes);
}

// 只通过正式 HTTP 制造领域失败，屏障与强退限于本次自有 Worker，不写业务表或任务终态。
export async function verifyApplicationImportCancellationResults(baseUrl,{
 hostAccessToken,fillWorkbook,inspectReceipt,startWorker,killWorker,stopWorker,waitProbe,logPath,signal,request=fetch
}) {
 const evidence={completed:false,responses:[],workers:[],forcedExits:[],scenarios:[],receipts:[]};
 const secrets=[hostAccessToken];let token=hostAccessToken;
 const redact=value=>secrets.reduce((text,key)=>text.replaceAll(key,'[REDACTED]'),String(value));
 const send=async(stage,path,method='GET',body,status=200,binary=false)=>{
  const response=await request(baseUrl+path,{method,headers:{Authorization:'Bearer '+token,Origin:'http://localhost',...(body!==undefined&&!(body instanceof FormData)?{'Content-Type':'application/json'}:{})},body:body===undefined||body instanceof FormData?body:JSON.stringify(body),redirect:'error',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(15000)]):AbortSignal.timeout(15000)});
  evidence.responses.push({stage,status:response.status});assert.equal(response.status,status,stage);
  return binary?{bytes:new Uint8Array(await response.arrayBuffer()),disposition:response.headers.get('content-disposition')}:response.json();
 };
 const task=id=>send('task','/api/v1/import-export/tasks/'+id);
 const poll=async(id,status)=>{
  const deadline=Date.now()+150000;
  while(true){const current=await task(id);if(current.statusKey===status)return current;
   assert(['queued','executing'].includes(current.statusKey),'unexpected terminal '+current.statusKey);
   assert(Date.now()<deadline,'task completion timeout');await sleep(250,undefined,{signal});}
 };
 const start=async(mode,id)=>{
  const item=await startWorker(mode,id);assert(Number.isInteger(item.pid));evidence.workers.push({mode,pid:item.pid,taskId:id});
  assert.equal(new Set(evidence.workers.map(worker=>worker.pid)).size,evidence.workers.length);return item;
 };
 try {
  const available=await send('tenants','/api/v1/tenancy/available');const tenant=available.find(item=>item.identifier==='local');assert(tenant);
  const context=await send('tenant-context','/api/v1/tenancy/context','PUT',{tenantId:tenant.id});assert(context.accessToken);token=context.accessToken;secrets.push(token);
  const actor=await send('actor','/api/v1/me');
  const unit=async label=>{
   const code='retain-'+label+'-'+randomUUID().replaceAll('-','');
   const created=await send('unit-'+label,'/api/v1/organization/units','POST',{parentId:null,code,name:'Retention '+label,displayOrder:10},201);
   await send('membership-'+label,'/api/v1/organization/user-units','POST',{userId:actor.id,unitId:created.id,isPrimary:false},201);
   return {...created,code};
  };
  const partialUnit=await unit('partial'),failedUnit=await unit('failed');
  const template=await send('template','/api/v1/import-export/schemas/organization.tenant_positions/worksheets/positions/template','GET',undefined,200,true);
  const create=async(label,units)=>{
   const codes=[1,2,3].map(index=>'retain-'+label+'-'+index+'-'+randomUUID().replaceAll('-',''));
   const bytes=await fillWorkbook(template.bytes,codes.map((code,index)=>[code,'Retention row '+(index+1),'10',units[index],'']));
   const form=new FormData();form.set('schemaKey','organization.tenant_positions');form.set('worksheetKey','positions');form.set('file',new Blob([bytes]),'retention.xlsx');
   const preview=await send('preview-'+label,'/api/v1/import-export/tasks','POST',form,201);
   assert.equal(preview.validRowCount,3);assert.equal(preview.invalidRowCount,0);assert.equal(preview.tenantId,tenant.id);
   await send('queue-'+label,'/api/v1/import-export/tasks/'+preview.id+'/execute','POST');return {id:preview.id,codes};
  };
  const positions=async()=> (await send('positions','/api/v1/organization/positions?page=1&pageSize=100')).items;
  const download=async(current,failedLines)=>{
   const file=await send('receipt','/api/v1/import-export/tasks/'+current.id+'/error-receipt','GET',undefined,200,true);
   assert(file.disposition?.includes('retention-errors.xlsx'));
   const rows=await inspectReceipt(file.bytes);assert.equal(rows.length,failedLines.length);
   assert.deepEqual(rows.map(row=>row[0]),failedLines.map(String));
   for(const row of rows)assert.equal(row[1],'organization.units.not_found');return file;
  };
  const cancel=async(current,before)=>{
   const result=await send('cancel','/api/v1/import-export/tasks/'+current.id+'/cancel','POST');assertCancelledResultRetained(before,result);
   assert.deepEqual(await send('cancel-repeated','/api/v1/import-export/tasks/'+current.id+'/cancel','POST'),result);
   for(const action of ['execute','resume','retry']){
    const failure=await send('cancelled-'+action,'/api/v1/import-export/tasks/'+current.id+'/'+action,'POST',undefined,422);
    assert.equal(failure.code,'import_export.task.status_invalid');
   }
   assert.deepEqual(await task(current.id),result);return result;
  };
  const retained=[];
  const partial=await create('partial',['',partialUnit.code,'']);
  await send('disable-partial','/api/v1/organization/units/'+partialUnit.id+'/disable','POST');
  const held=await start('receipt',partial.id),uploaded=await waitProbe('receipt',held.pid,partial.id);
  assert.equal(uploaded.next,3);assertReceiptIdentity(uploaded.receiptId,uploaded.receiptId,true);
  const running=await task(partial.id);assertImportProgress(running,{status:'executing',next:2,succeeded:1,failed:1,receipt:false});
  const conflict=await send('running-cancel-conflict','/api/v1/import-export/tasks/'+partial.id+'/cancel','POST',undefined,409);
  assert.equal(conflict.code,'import_export.task.status_invalid');assert.deepEqual(await task(partial.id),running);
  evidence.scenarios.push('in-flight-cancellation-conflict-preserves-running-task');
  const forced=await killWorker(held);assert.equal(forced.pid,held.pid);assert.equal(forced.forced,true);assert.equal(forced.closed,true);evidence.forcedExits.push(forced);
  const recovered=await start('observe',partial.id),completed=await poll(partial.id,'execution_partial');
  assertImportProgress(completed,{status:'execution_partial',next:3,succeeded:2,failed:1,receipt:true});
  const probe=await waitProbe('observe',recovered.pid,partial.id);assertReceiptIdentity(uploaded.receiptId,probe.receiptId,true);await stopWorker(recovered);
  const originalRows=await positions(),ids=partial.codes.filter((_,index)=>index!==1).map(code=>{
   const matches=originalRows.filter(row=>row.code===code);assert.equal(matches.length,1);return {code,id:matches[0].id};
  });
  assert.equal(originalRows.filter(row=>row.code===partial.codes[1]).length,0);
  const partialReceipt=await download(partial,[2]),cancelledPartial=await cancel(partial,completed);
  assertReceiptDownloadRetained(partialReceipt,await download(partial,[2]));retained.push({current:partial,result:cancelledPartial,receipt:partialReceipt,lines:[2]});
  evidence.scenarios.push('partial-cancellation-preserves-receipt-and-committed-row-ids');

  const failed=await create('failed',[failedUnit.code,failedUnit.code,failedUnit.code]);
  await send('disable-failed','/api/v1/organization/units/'+failedUnit.id+'/disable','POST');
  const failedWorker=await start('observe',failed.id),failedResult=await poll(failed.id,'execution_failed');
  assertImportProgress(failedResult,{status:'execution_failed',next:3,succeeded:0,failed:3,receipt:true});
  await waitProbe('observe',failedWorker.pid,failed.id);await stopWorker(failedWorker);
  const failedReceipt=await download(failed,[1,2,3]),cancelledFailed=await cancel(failed,failedResult);
  assertReceiptDownloadRetained(failedReceipt,await download(failed,[1,2,3]));retained.push({current:failed,result:cancelledFailed,receipt:failedReceipt,lines:[1,2,3]});
  evidence.scenarios.push('failed-cancellation-preserves-original-error-receipt');

  const control=await create('control',['','','']),controlWorker=await start('observe',control.id);
  assertImportProgress(await poll(control.id,'execution_succeeded'),{status:'execution_succeeded',next:3,succeeded:3,failed:0,receipt:false});
  await waitProbe('observe',controlWorker.pid,control.id);await stopWorker(controlWorker);
  for(const item of retained){assert.deepEqual(await task(item.current.id),item.result);assertReceiptDownloadRetained(item.receipt,await download(item.current,item.lines));
   evidence.receipts.push({taskId:item.current.id,sha256:createHash('sha256').update(item.receipt.bytes).digest('hex'),errorRows:item.lines.length});}
  const finalRows=await positions();for(const row of ids){const matches=finalRows.filter(item=>item.code===row.code);assert.equal(matches.length,1);assert.equal(matches[0].id,row.id);}
  for(const code of [partial.codes[1],...failed.codes])assert.equal(finalRows.filter(row=>row.code===code).length,0);
  for(const code of control.codes)assert.equal(finalRows.filter(row=>row.code===code).length,1);
  evidence.scenarios.push('fresh-worker-excludes-cancelled-partial-and-failed');evidence.completed=true;return evidence;
 } catch(error) {evidence.error=redact(error.message??error);throw new Error(evidence.error);}
 finally {writeFileSync(logPath,redact(JSON.stringify(evidence,null,2)));}
}
