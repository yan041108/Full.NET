import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {assertBoundPending,advanceBoundRevision} from './bound-runtime-failure.mjs';
const base='/api/v1/enterprise_request/enterprise-requests';
// 成功和故障共享已构建的独立宿主；只使用公开生命周期操作，不伪造业务回写。
export async function verifyEnterpriseBoundRuntime({create,submit,send,poll,stopWorker,startWorker,injectFailure,assertDeadLetter,drain,onStage=()=>{},evidence={completed:false}}){
 assert.equal(evidence.completed,false);
 const progress=id=>send(`${base}/${id}/approval-progress`);
 const instance=id=>send(`/api/v1/workflow/instances/${id}`);
 const begin=async outcome=>{
  const request=await create(outcome);await submit(request);
  const before=await poll(()=>progress(request.id),p=>p.deliveryState==='started','bound runtime startup');
  assertBoundPending(before,before);const running=await instance(before.workflowInstanceId);
  assert.equal(running.statusKey,'active');assert.equal(running.businessId,request.id);assert.ok(running.activeTodoId);
  return {request,before,running};
 };
 const lifecycle=(id,action,revision,key,expected=200)=>send(`/api/v1/workflow/instances/${id}/${action}`,'POST',
  {expectedRevision:revision,reason:'独立应用绑定运行验收',idempotencyKey:key},expected);
 const finish=async ({request,before},status)=>{
  const final=await poll(()=>progress(request.id),p=>p.deliveryState==='finalized'&&p.finalNotification?.pendingDeliveryCount===0,'bound runtime business final');
  assert.equal(final.requestStatus,status);assert.equal(final.requestVersion,advanceBoundRevision(before.requestVersion));
  for(const key of ['workflowInstanceId','submittedVersion','submittedAtUtc','startedAtUtc'])assert.equal(final[key],before[key],key);
  assert.ok(final.completedAtUtc);assert.ok(final.finalNotification.intentId);
  assert.equal(final.finalNotification.failedDeliveryCount,0);assert.equal(final.finalNotification.deadLetteredDeliveryCount,0);
  return {requestId:request.id,instanceId:before.workflowInstanceId,requestVersion:final.requestVersion,intentId:final.finalNotification.intentId};
 };
 const paused=await begin('Approved'),pausedId=paused.running.id;
 onStage('pause-recovery');
 const pauseKey=randomUUID();await lifecycle(pausedId,'pause',paused.running.revision,pauseKey);
 const suspended=await instance(pausedId);assert.equal(suspended.statusKey,'suspended');assertBoundPending(await progress(paused.request.id),paused.before);
 const approveKey=randomUUID(),approve=(expected,key=approveKey)=>send(`/api/v1/workflow/todos/${paused.running.activeTodoId}/approve`,'POST',
  {expectedRevision:1,fieldPatch:{},comment:'恢复后审批',idempotencyKey:key},expected);
 await approve(409,randomUUID());await lifecycle(pausedId,'recover',paused.running.revision,randomUUID(),409);
 assertBoundPending(await progress(paused.request.id),paused.before);
 const recoverKey=randomUUID();for(let i=0;i<2;i++)await lifecycle(pausedId,'recover',suspended.revision,recoverKey);
 const recovered=await instance(pausedId);assert.equal(recovered.statusKey,'active');assert.equal(recovered.revision,advanceBoundRevision(suspended.revision));
 assert.equal(recovered.activeTodoId,paused.running.activeTodoId);assertBoundPending(await progress(paused.request.id),paused.before);
 for(let i=0;i<2;i++)await approve(200);
 evidence.pausedRecovery=await finish(paused,'Approved');
 const failed=await begin('Cancelled'),failedId=failed.running.id;
 // 停止本轮 Worker 后构造残留，再让新进程扫描；不与正在执行的恢复任务竞争。
 onStage('fault-injection');
 await stopWorker();evidence.injection=await injectFailure({instanceId:failedId,requestId:failed.request.id});await startWorker();
 onStage('runtime-suspension');
 const exhausted=await poll(()=>instance(failedId),p=>p.statusKey==='suspended','actual Worker runtime suspension');
 evidence.deadLetter=await assertDeadLetter({instanceId:failedId,requestId:failed.request.id});
 assert.equal(exhausted.activeTodoId,null);assertBoundPending(await progress(failed.request.id),failed.before);
 onStage('failed-cancellation');
 await lifecycle(failedId,'recover',exhausted.revision,randomUUID(),409);
 await lifecycle(failedId,'cancel',advanceBoundRevision(exhausted.revision,-1),randomUUID(),409);
 assertBoundPending(await progress(failed.request.id),failed.before);
 const cancelKey=randomUUID();for(let i=0;i<2;i++)await lifecycle(failedId,'cancel',exhausted.revision,cancelKey);
 evidence.failedCancellation=await finish(failed,'Cancelled');
 const cancelled=await instance(failedId);assert.equal(cancelled.statusKey,'cancelled');assert.equal(cancelled.revision,advanceBoundRevision(exhausted.revision));
 await drain('bound-runtime');
 for(const final of [evidence.pausedRecovery,evidence.failedCancellation]){
  const current=await progress(final.requestId);assert.equal(current.requestVersion,final.requestVersion);assert.equal(current.finalNotification.intentId,final.intentId);
  const inbox=await send('/api/v1/notifications/my-inbox-messages?page=1&pageSize=100');
  const title=final===evidence.pausedRecovery?'审批已完成':'审批已取消';
  assert.equal(inbox.items.filter(message=>message.title===title&&message.content?.includes(final.requestId)).length,1);
 }
 evidence.completed=true;return evidence;
}
