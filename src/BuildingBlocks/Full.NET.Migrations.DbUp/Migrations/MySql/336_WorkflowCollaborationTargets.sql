-- 336：共享准入协调两方竞争；目标和后代均保存本地来源，不跨作用域写业务根。
CREATE TABLE IF NOT EXISTS fn_workflow_collaboration_admission (
 Id binary(16) NOT NULL COMMENT '调用UUID v7',
 SourceTenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '可信源作用域',
 TargetTenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '获准目标作用域',
 DeliveryId binary(16) NOT NULL COMMENT '源逻辑投递身份',
 GrantId binary(16) NOT NULL COMMENT '双边共享授权',
 BindingJson longtext NOT NULL COMMENT '固定调用绑定，不含源私有正文',
 BindingHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '固定绑定SHA256',
 ProjectionJson longtext NOT NULL COMMENT '仅含获准字段的最小投影',
 ProjectionHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '投影SHA256',
 StatusKey varchar(24) COLLATE utf8mb4_bin NOT NULL COMMENT '创建与关闭准入状态',
 TargetInstanceId binary(16) NULL COMMENT '目标提交的独立实例身份',
 Revision bigint NOT NULL COMMENT '共享准入CAS修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '源提交时间',
 UpdatedAtUtc datetime(6) NOT NULL COMMENT '最近推进时间',
 CONSTRAINT pk_fn_workflow_collaboration_admission PRIMARY KEY (Id)
) COMMENT='工作流双方共享调用准入事实，仅协调创建与关闭' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_workflow_collaboration_origin (
 Id binary(16) NOT NULL COMMENT '本地实例UUID v7',
 CallId binary(16) NOT NULL COMMENT '共享调用准入身份',
 RootInstanceId binary(16) NOT NULL COMMENT '本地独立目标根',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '可信目标作用域',
 Depth int NOT NULL COMMENT '实际全局调用深度',
 CONSTRAINT pk_fn_workflow_collaboration_origin PRIMARY KEY (Id)
) COMMENT='工作流目标和后代的真实协作来源' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
-- 自动 Relay 保留系统归因；人工动作仍必须有操作人。
DROP PROCEDURE IF EXISTS fn_workflow_collaboration_target_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_collaboration_target_ensure()
BEGIN
 IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS AS tc INNER JOIN information_schema.CHECK_CONSTRAINTS AS cc ON cc.CONSTRAINT_SCHEMA=tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME=tc.CONSTRAINT_NAME WHERE tc.CONSTRAINT_SCHEMA=DATABASE() AND tc.TABLE_NAME='fn_workflow_domain_audit' AND tc.CONSTRAINT_NAME='ck_fn_workflow_domain_audit_actor' AND LOCATE('collaboration_relay',cc.CHECK_CLAUSE)=0) THEN
  ALTER TABLE fn_workflow_domain_audit DROP CHECK ck_fn_workflow_domain_audit_actor;
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_domain_audit' AND CONSTRAINT_NAME='ck_fn_workflow_domain_audit_actor' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_domain_audit ADD CONSTRAINT ck_fn_workflow_domain_audit_actor CHECK
  (ActorUserId IS NOT NULL OR (OperationKey IN ('assignee.handle','activity.execute') AND ResourceTypeKey='step') OR
   (OperationKey='instance.cancel' AND ResourceTypeKey='instance' AND JSON_EXTRACT(DetailJson,'$.sourceKey') IS NOT NULL AND
    JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.sourceKey'))='parent_subprocess' AND JSON_EXTRACT(DetailJson,'$.parentInstanceId') IS NOT NULL AND JSON_EXTRACT(DetailJson,'$.parentStepId') IS NOT NULL AND
    JSON_TYPE(JSON_EXTRACT(DetailJson,'$.parentInstanceId'))='STRING' AND JSON_TYPE(JSON_EXTRACT(DetailJson,'$.parentStepId'))='STRING') OR
   (OperationKey='instance.start' AND ResourceTypeKey='instance' AND JSON_EXTRACT(DetailJson,'$.sourceKey') IS NOT NULL AND
    JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.sourceKey'))='collaboration_relay' AND JSON_EXTRACT(DetailJson,'$.callId') IS NOT NULL AND JSON_EXTRACT(DetailJson,'$.starterUserId') IS NOT NULL AND
    JSON_TYPE(JSON_EXTRACT(DetailJson,'$.callId'))='STRING' AND JSON_TYPE(JSON_EXTRACT(DetailJson,'$.starterUserId'))='STRING' AND
    REGEXP_LIKE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.callId')),'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') AND
    REGEXP_LIKE(JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.starterUserId')),'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_admission' AND INDEX_NAME='ix_fn_workflow_collaboration_admission_pending') THEN
  CREATE INDEX ix_fn_workflow_collaboration_admission_pending ON fn_workflow_collaboration_admission(StatusKey,UpdatedAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_origin' AND INDEX_NAME='ix_fn_workflow_collaboration_origin_call') THEN
  CREATE INDEX ix_fn_workflow_collaboration_origin_call ON fn_workflow_collaboration_origin(CallId,TenantScopeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_admission_grant') THEN
  ALTER TABLE fn_workflow_collaboration_admission ADD CONSTRAINT fk_fn_workflow_collaboration_admission_grant FOREIGN KEY (GrantId) REFERENCES fn_workflow_collaboration_grant(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_origin_instance') THEN
  ALTER TABLE fn_workflow_collaboration_origin ADD CONSTRAINT fk_fn_workflow_collaboration_origin_instance FOREIGN KEY (Id) REFERENCES fn_workflow_instance(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_origin_root') THEN
  ALTER TABLE fn_workflow_collaboration_origin ADD CONSTRAINT fk_fn_workflow_collaboration_origin_root FOREIGN KEY (RootInstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_origin_call') THEN
  ALTER TABLE fn_workflow_collaboration_origin ADD CONSTRAINT fk_fn_workflow_collaboration_origin_call FOREIGN KEY (CallId) REFERENCES fn_workflow_collaboration_admission(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_admission' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_admission_state' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_admission ADD CONSTRAINT ck_fn_workflow_collaboration_admission_state CHECK (Revision>0 AND SourceTenantScopeKey<>TargetTenantScopeKey AND StatusKey IN ('requested','created','active','cancel_requested','cancelled','completed') AND (StatusKey NOT IN ('created','active') OR TargetInstanceId IS NOT NULL));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_admission' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_admission_json' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_admission ADD CONSTRAINT ck_fn_workflow_collaboration_admission_json CHECK (JSON_VALID(BindingJson)=1 AND JSON_VALID(ProjectionJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_origin' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_origin_depth' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_origin ADD CONSTRAINT ck_fn_workflow_collaboration_origin_depth CHECK (Depth BETWEEN 1 AND 8);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_collaboration_target_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_collaboration_target_ensure;
