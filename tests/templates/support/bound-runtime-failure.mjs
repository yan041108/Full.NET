import assert from 'node:assert/strict';

const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
function identity(value){assert.match(value,uuid,'fault identity must be UUID');return value.toLowerCase();}
function context(options){
 options.signal?.throwIfAborted();
 const {provider,container,ownedContainerId}=options;
 assert.ok(['mysql','sqlserver'].includes(provider));
 assert.ok(typeof ownedContainerId==='string'&&ownedContainerId.length>0&&container.getId()===ownedContainerId,'fault container must be owned');
 const ids=Object.fromEntries(['instanceId','tenantId','requestId'].map(key=>[key,identity(options[key])]));
 const literal=value=>provider==='mysql'?`UNHEX('${value.replaceAll('-','')}')`:`'${value}'`;
 return {...ids,provider,container,literal,signal:options.signal};
}
async function execute(ctx,sql){
 const args=ctx.provider==='mysql'
  ? ['mysql','-uroot','-D','fullnet_app','-N','-B','-e',sql]
  : ['/bin/sh','-c','if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then exec /opt/mssql-tools18/bin/sqlcmd "$@"; else exec /opt/mssql-tools/bin/sqlcmd "$@"; fi',
    'fullnet-owned-fault','-S','localhost','-U','sa','-I','-d','master','-C','-b','-h','-1','-W','-Q',sql];
 ctx.signal?.throwIfAborted();
 const result=await ctx.container.exec(args,{env:{[ctx.provider==='mysql'?'MYSQL_PWD':'SQLCMDPASSWORD']:'FullNet_Test!123'}});ctx.signal?.throwIfAborted();
 if(result.exitCode!==0)throw Object.assign(new Error('owned fault command failed'),{code:'fault.sql_command_failed',commandExitCode:result.exitCode});return result.output;
}
// 仅在本轮自建容器中构造崩溃残留；暂停和死信必须由正式 Worker 产生。
export async function injectBoundRuntimeFailure(options){
 const ctx=context(options),{provider,literal}=ctx;
 const instance=literal(ctx.instanceId),tenant=literal(ctx.tenantId),request=literal(ctx.requestId);
 const bound=`EXISTS (SELECT 1 FROM demo_enterprise_request_approval_submission WHERE WorkflowInstanceId = ${instance} AND TenantId = ${tenant} AND RequestId = ${request})`;
 const now=provider==='mysql'?'UTC_TIMESTAMP(6)':'SYSUTCDATETIME()';
 const expired=provider==='mysql'?"DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 1 MINUTE)":"DATEADD(minute, -1, SYSUTCDATETIME())";
 const sql=provider==='mysql'
  ? `START TRANSACTION; UPDATE fn_workflow_todo SET StatusKey = 'completed', CompletedAtUtc = ${now}, ResultActionKey = 'system' WHERE InstanceId = ${instance} AND StatusKey = 'active' AND ${bound}; SET @todos=ROW_COUNT(); UPDATE fn_workflow_instance SET LeaseOwnerKey = 'owned-fault', LeaseExpiresAtUtc = ${expired} WHERE Id = ${instance} AND StatusKey = 'active' AND ${bound}; SET @instances=ROW_COUNT(); COMMIT; SELECT CONCAT('FN_FAULT:',@todos,':',@instances);`
  : `SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF; SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRANSACTION; DECLARE @todos int,@instances int; UPDATE fn_workflow_todo SET StatusKey = 'completed', CompletedAtUtc = ${now}, ResultActionKey = 'system' WHERE InstanceId = ${instance} AND StatusKey = 'active' AND ${bound}; SET @todos=@@ROWCOUNT; UPDATE fn_workflow_instance SET LeaseOwnerKey = 'owned-fault', LeaseExpiresAtUtc = ${expired} WHERE Id = ${instance} AND StatusKey = 'active' AND ${bound}; SET @instances=@@ROWCOUNT; COMMIT; SELECT CONCAT('FN_FAULT:',@todos,':',@instances);`;
 const output=await execute(ctx,sql);assert.match(output,/^\s*FN_FAULT:1:1\s*$/mu,'fault must update exactly one todo and instance');
 return {completed:true,instanceId:ctx.instanceId,updatedTodos:1,updatedInstances:1};
}
export async function assertBoundRuntimeDeadLetter(options){
 const ctx=context(options);
 const output=await execute(ctx,`SELECT CONCAT('FN_DEAD:',COUNT(*)) FROM fn_workflow_recovery_task WHERE InstanceId = ${ctx.literal(ctx.instanceId)} AND StatusKey = 'dead_lettered' AND AttemptCount = 1;`);
 assert.match(output,/^\s*FN_DEAD:1\s*$/mu,'actual Worker must exhaust one recovery task');return {completed:true,attemptCount:1,deadLetteredTasks:1};
}
// 暂停不属于业务终态；验证回执的稳定身份，避免仅看页面状态就误认证。
export function assertBoundPending(current,before){
 assert.equal(current.requestStatus,'Submitted');assert.equal(current.deliveryState,'started');
 for(const key of ['workflowInstanceId','requestVersion','submittedVersion','submittedAtUtc','startedAtUtc'])assert.equal(current[key],before[key],key);
 assert.equal(current.completedAtUtc,null);assert.equal(current.finalNotification,null);
}
// 长整型在线协议中可以是字符串；保留类型并使用整数运算，避免拼接或精度丢失。
export function advanceBoundRevision(value,delta=1){
 assert.ok(Number.isSafeInteger(delta));
 if(typeof value==='string'){assert.match(value,/^[0-9]+$/u);const next=BigInt(value)+BigInt(delta);assert.ok(next>=0n);return next.toString();}
 assert.ok(Number.isSafeInteger(value));const next=value+delta;assert.ok(Number.isSafeInteger(next)&&next>=0);return next;
}
