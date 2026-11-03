-- 337：目标独立事务发布创建/完成/关闭事实；源只接纳认证收件，不跨作用域写业务根。
CREATE TABLE IF NOT EXISTS fn_workflow_collaboration_control (
 Id binary(16) NOT NULL COMMENT '协议回执UUID v7',
 CallId binary(16) NOT NULL COMMENT '唯一共享调用准入',
 SourceTenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '可信原源作用域',
 TargetTenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '可信目标发布作用域',
 KindKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '创建、完成或关闭确认种类',
 BodyJson longtext NOT NULL COMMENT '固定绑定及最小结果正文',
 BodyHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '不可变正文SHA256',
 EnvelopeMessageId binary(16) NULL COMMENT '首次认证物理信封身份',
 StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '发布、收件或已接纳状态',
 Revision bigint NOT NULL COMMENT '协议状态CAS修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '唯一发布UTC时间',
 ReceivedAtUtc datetime(6) NULL COMMENT '首次收件UTC时间',
 CONSTRAINT pk_fn_workflow_collaboration_control PRIMARY KEY (Id)
) COMMENT='工作流创建结果取消的最小耐久协议事实' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_collaboration_control_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_collaboration_control_ensure()
BEGIN
 IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS AS tc INNER JOIN information_schema.CHECK_CONSTRAINTS AS cc ON cc.CONSTRAINT_SCHEMA=tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME=tc.CONSTRAINT_NAME WHERE tc.CONSTRAINT_SCHEMA=DATABASE() AND tc.TABLE_NAME='fn_workflow_domain_audit' AND tc.CONSTRAINT_NAME='ck_fn_workflow_domain_audit_actor' AND LOCATE('collaboration_cancel',cc.CHECK_CLAUSE)=0) THEN
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
    REGEXP_LIKE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.starterUserId')),'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_call' AND INDEX_NAME='ix_fn_workflow_collaboration_call_instance') THEN
  CREATE INDEX ix_fn_workflow_collaboration_call_instance ON fn_workflow_collaboration_call(InstanceId,TenantScopeKey,StatusKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_control' AND INDEX_NAME='ux_fn_workflow_collaboration_control_envelope') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_collaboration_control_envelope ON fn_workflow_collaboration_control(EnvelopeMessageId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_control' AND INDEX_NAME='ux_fn_workflow_collaboration_control_kind') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_collaboration_control_kind ON fn_workflow_collaboration_control(CallId,KindKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_control' AND INDEX_NAME='ix_fn_workflow_collaboration_control_pending') THEN
  CREATE INDEX ix_fn_workflow_collaboration_control_pending ON fn_workflow_collaboration_control(StatusKey,CreatedAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_origin' AND INDEX_NAME='ix_fn_workflow_collaboration_origin_root') THEN
  CREATE INDEX ix_fn_workflow_collaboration_origin_root ON fn_workflow_collaboration_origin(RootInstanceId,TenantScopeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_control_call') THEN
  ALTER TABLE fn_workflow_collaboration_control ADD CONSTRAINT fk_fn_workflow_collaboration_control_call FOREIGN KEY (CallId) REFERENCES fn_workflow_collaboration_admission(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_control' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_control_state' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_control ADD CONSTRAINT ck_fn_workflow_collaboration_control_state CHECK (Revision>0 AND SourceTenantScopeKey<>TargetTenantScopeKey AND KindKey IN ('created','completed','cancelled') AND StatusKey IN ('published','received','applied') AND ((StatusKey='published' AND EnvelopeMessageId IS NULL AND ReceivedAtUtc IS NULL) OR (StatusKey IN ('received','applied') AND EnvelopeMessageId IS NOT NULL AND ReceivedAtUtc IS NOT NULL)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_control' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_control_json' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_control ADD CONSTRAINT ck_fn_workflow_collaboration_control_json CHECK (JSON_VALID(BodyJson)=1);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_collaboration_control_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_collaboration_control_ensure;
