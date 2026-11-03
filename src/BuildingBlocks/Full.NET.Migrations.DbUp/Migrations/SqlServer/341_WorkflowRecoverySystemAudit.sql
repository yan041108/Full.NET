-- 341：真实恢复任务以受约束的系统来源审计；人工操作继续要求实际操作者。
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor' AND CHARINDEX(N'recovery_worker',definition)=0)
 ALTER TABLE dbo.fn_workflow_domain_audit DROP CONSTRAINT ck_fn_workflow_domain_audit_actor;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor')
 ALTER TABLE dbo.fn_workflow_domain_audit WITH CHECK ADD CONSTRAINT ck_fn_workflow_domain_audit_actor CHECK
 (ActorUserId IS NOT NULL OR (OperationKey IN ('assignee.handle','activity.execute') AND ResourceTypeKey='step') OR
  (OperationKey='instance.cancel' AND ResourceTypeKey='instance' AND JSON_VALUE(DetailJson,'$.sourceKey') IS NOT NULL AND
   JSON_VALUE(DetailJson,'$.sourceKey')='parent_subprocess' AND JSON_VALUE(DetailJson,'$.parentInstanceId') IS NOT NULL AND JSON_VALUE(DetailJson,'$.parentStepId') IS NOT NULL) OR
  (OperationKey IN ('instance.start','instance.cancel') AND ResourceTypeKey='instance' AND JSON_VALUE(DetailJson,'$.sourceKey') IS NOT NULL AND
   ((OperationKey='instance.start' AND JSON_VALUE(DetailJson,'$.sourceKey')='collaboration_relay') OR
    (OperationKey='instance.cancel' AND JSON_VALUE(DetailJson,'$.sourceKey')='collaboration_cancel')) AND
   TRY_CONVERT(uniqueidentifier,JSON_VALUE(DetailJson,'$.callId')) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(DetailJson,'$.starterUserId')) IS NOT NULL) OR
  (ResourceTypeKey='recovery_task' AND InstanceId IS NOT NULL AND OperationKey IN ('recovery.expired_lease','recovery.stuck_instance','recovery.incomplete_step') AND ISJSON(DetailJson)=1 AND
   JSON_VALUE(DetailJson,'$.sourceKey') IS NOT NULL AND JSON_VALUE(DetailJson,'$.sourceKey')='recovery_worker' AND
   TRY_CONVERT(uniqueidentifier,JSON_VALUE(DetailJson,'$.taskId')) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(DetailJson,'$.taskId'))=ResourceId AND
   TRY_CONVERT(uniqueidentifier,JSON_VALUE(DetailJson,'$.instanceId')) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(DetailJson,'$.instanceId'))=InstanceId AND
   JSON_VALUE(DetailJson,'$.kindKey') IS NOT NULL AND OperationKey='recovery.'+JSON_VALUE(DetailJson,'$.kindKey') AND
   TRY_CONVERT(int,JSON_VALUE(DetailJson,'$.leaseGeneration')) IS NOT NULL AND TRY_CONVERT(int,JSON_VALUE(DetailJson,'$.leaseGeneration'))>0 AND
   LEN(JSON_VALUE(DetailJson,'$.leaseGeneration'))<=10 AND JSON_VALUE(DetailJson,'$.leaseGeneration') NOT LIKE '%[^0-9]%' AND
   TRY_CONVERT(int,JSON_VALUE(DetailJson,'$.attempt')) IS NOT NULL AND TRY_CONVERT(int,JSON_VALUE(DetailJson,'$.attempt'))>0 AND
   LEN(JSON_VALUE(DetailJson,'$.attempt'))<=10 AND JSON_VALUE(DetailJson,'$.attempt') NOT LIKE '%[^0-9]%'));
GO
