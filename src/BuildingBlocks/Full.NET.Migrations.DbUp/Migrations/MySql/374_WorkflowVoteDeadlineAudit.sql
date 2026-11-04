-- 374：仅投票截止的明确系统来源允许无人工操作者；保留原恢复、子流程和协作审计约束。
DROP PROCEDURE IF EXISTS fn_workflow_vote_audit_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_vote_audit_ensure()
BEGIN
 IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS AS tc INNER JOIN information_schema.CHECK_CONSTRAINTS AS cc ON cc.CONSTRAINT_SCHEMA=tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME=tc.CONSTRAINT_NAME WHERE tc.CONSTRAINT_SCHEMA=DATABASE() AND tc.TABLE_NAME='fn_workflow_domain_audit' AND tc.CONSTRAINT_NAME='ck_fn_workflow_domain_audit_actor' AND LOCATE('server-deadline',cc.CHECK_CLAUSE)=0) THEN
  ALTER TABLE fn_workflow_domain_audit DROP CHECK ck_fn_workflow_domain_audit_actor;
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_domain_audit' AND CONSTRAINT_NAME='ck_fn_workflow_domain_audit_actor' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_domain_audit ADD CONSTRAINT ck_fn_workflow_domain_audit_actor CHECK
  (ActorUserId IS NOT NULL OR (OperationKey IN ('assignee.handle','activity.execute') AND ResourceTypeKey='step') OR
   (OperationKey='instance.cancel' AND ResourceTypeKey='instance' AND JSON_EXTRACT(DetailJson,'$.sourceKey') IS NOT NULL AND
    JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.sourceKey'))='parent_subprocess' AND JSON_EXTRACT(DetailJson,'$.parentInstanceId') IS NOT NULL AND JSON_EXTRACT(DetailJson,'$.parentStepId') IS NOT NULL AND
    JSON_TYPE(JSON_EXTRACT(DetailJson,'$.parentInstanceId'))='STRING' AND JSON_TYPE(JSON_EXTRACT(DetailJson,'$.parentStepId'))='STRING') OR
   (OperationKey IN ('instance.start','instance.cancel') AND ResourceTypeKey='instance' AND JSON_EXTRACT(DetailJson,'$.sourceKey') IS NOT NULL AND
    ((OperationKey='instance.start' AND JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.sourceKey'))='collaboration_relay') OR
     (OperationKey='instance.cancel' AND JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.sourceKey'))='collaboration_cancel')) AND
    JSON_EXTRACT(DetailJson,'$.callId') IS NOT NULL AND JSON_EXTRACT(DetailJson,'$.starterUserId') IS NOT NULL AND
    JSON_TYPE(JSON_EXTRACT(DetailJson,'$.callId'))='STRING' AND JSON_TYPE(JSON_EXTRACT(DetailJson,'$.starterUserId'))='STRING' AND
    REGEXP_LIKE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.callId')),'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') AND
    REGEXP_LIKE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.starterUserId')),'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')) OR
   (ResourceTypeKey='recovery_task' AND InstanceId IS NOT NULL AND OperationKey IN ('recovery.expired_lease','recovery.stuck_instance','recovery.incomplete_step') AND JSON_VALID(DetailJson)=1 AND
    JSON_EXTRACT(DetailJson,'$.sourceKey') IS NOT NULL AND JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.sourceKey'))='recovery_worker' AND
    JSON_EXTRACT(DetailJson,'$.taskId') IS NOT NULL AND JSON_TYPE(JSON_EXTRACT(DetailJson,'$.taskId'))='STRING' AND
    UNHEX(REPLACE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.taskId')),'-','')) IS NOT NULL AND
    UNHEX(REPLACE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.taskId')),'-',''))=ResourceId AND
    JSON_EXTRACT(DetailJson,'$.instanceId') IS NOT NULL AND JSON_TYPE(JSON_EXTRACT(DetailJson,'$.instanceId'))='STRING' AND
    UNHEX(REPLACE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.instanceId')),'-','')) IS NOT NULL AND
    UNHEX(REPLACE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.instanceId')),'-',''))=InstanceId AND
    JSON_EXTRACT(DetailJson,'$.kindKey') IS NOT NULL AND OperationKey=CONCAT('recovery.',JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.kindKey'))) AND
    JSON_EXTRACT(DetailJson,'$.leaseGeneration') IS NOT NULL AND CHAR_LENGTH(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.leaseGeneration')))<=10 AND
    REGEXP_LIKE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.leaseGeneration')),'^[0-9]+$') AND CAST(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.leaseGeneration')) AS UNSIGNED) BETWEEN 1 AND 2147483647 AND
    JSON_EXTRACT(DetailJson,'$.attempt') IS NOT NULL AND CHAR_LENGTH(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.attempt')))<=10 AND
    REGEXP_LIKE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.attempt')),'^[0-9]+$') AND CAST(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.attempt')) AS UNSIGNED) BETWEEN 1 AND 2147483647) OR
   (OperationKey='vote.deadline' AND ResourceTypeKey='step' AND InstanceId IS NOT NULL AND JSON_VALID(DetailJson)=1 AND
    JSON_EXTRACT(DetailJson,'$.sourceKey') IS NOT NULL AND JSON_TYPE(JSON_EXTRACT(DetailJson,'$.sourceKey'))='STRING' AND
    JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.sourceKey'))='server-deadline'));
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_vote_audit_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_vote_audit_ensure;
