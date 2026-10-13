import assert from 'node:assert/strict';
import test from 'node:test';
import {injectBoundRuntimeFailure,assertBoundPending} from './support/bound-runtime-failure.mjs';
const id='0199babc-0123-7000-8000-000000000001';
const before={workflowInstanceId:id,requestVersion:2,submittedVersion:2,startedAtUtc:'2026-10-13T00:00:00Z',submittedAtUtc:'2026-10-13T00:00:00Z'};
test('暂停不能冒充业务终态，绑定版本与启动回执必须保持',()=>{
 const pending={...before,requestStatus:'Submitted',deliveryState:'started',completedAtUtc:null,finalNotification:null};
 assert.doesNotThrow(()=>assertBoundPending(pending,before));
 for(const patch of [{requestStatus:'Approved'},{requestVersion:3},{workflowInstanceId:'other'},{completedAtUtc:'now'},{finalNotification:{intentId:id}},{startedAtUtc:'changed'}])
  assert.throws(()=>assertBoundPending({...pending,...patch},before));
});
test('故障注入只接收严格身份与自建容器，命令失败不计注入成功',async()=>{
 const options={provider:'mysql',container:{exec:async()=>({exitCode:1,output:'error'})},ownedContainerId:'owned',instanceId:id,tenantId:id,requestId:id};
 options.container.getId=()=> 'owned';
 await assert.rejects(injectBoundRuntimeFailure({...options,instanceId:"x';DELETE"}),/UUID/);
 await assert.rejects(injectBoundRuntimeFailure({...options,ownedContainerId:'other'}),/owned/);
 await assert.rejects(injectBoundRuntimeFailure(options),/command/);
});
test('双库故障只关闭一项活动待办并过期租约，不伪造暂停状态',async()=>{
 for(const provider of ['mysql','sqlserver']){
  const commands=[]; const container={getId:()=> 'owned',exec:async args=>{commands.push(args);return {exitCode:0,output:'FN_FAULT:1:1\n'};}};
  const receipt=await injectBoundRuntimeFailure({provider,container,ownedContainerId:'owned',instanceId:id,tenantId:id,requestId:id});
  assert.equal(receipt.completed,true);assert.equal(receipt.updatedTodos,1);assert.equal(receipt.updatedInstances,1);
  const sql=commands.at(-1).at(-1);assert.match(sql,/demo_enterprise_request_approval_submission/);assert.match(sql,/TenantId/);assert.match(sql,/RequestId/);
  assert.doesNotMatch(sql,/SET StatusKey = 'suspended'/);
 }
});

test('零更新和取消不能计为故障成功，取消后不进入容器',async()=>{
 const options={provider:'mysql',ownedContainerId:'owned',instanceId:id,tenantId:id,requestId:id};
 for(const output of ['FN_FAULT:0:1','FN_FAULT:1:0','unrelated'])
  await assert.rejects(injectBoundRuntimeFailure({...options,container:{getId:()=> 'owned',exec:async()=>({exitCode:0,output})}}),/exactly one/);
 const abort=new AbortController();abort.abort();
 await assert.rejects(injectBoundRuntimeFailure({...options,signal:abort.signal,container:{getId:()=> 'owned',exec:()=>assert.fail('must not exec')}}),{name:'AbortError'});
});

test('公开长整型版本用精确十进制递增，不能字符串拼接或丢失精度',async()=>{
 const {advanceBoundRevision}=await import('./support/bound-runtime-failure.mjs');
 assert.equal(advanceBoundRevision('2'), '3');
 assert.equal(advanceBoundRevision('9007199254740993'), '9007199254740994');
 assert.equal(advanceBoundRevision('3',-1),'2');
 assert.equal(advanceBoundRevision(2),3);
 assert.throws(()=>advanceBoundRevision('broken'));
});
