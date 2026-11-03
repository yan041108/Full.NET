-- 333：源请求与本地可靠收件；不跨作用域事务创建目标实例，不开放公共节点20。
CREATE TABLE IF NOT EXISTS fn_workflow_collaboration_call (
 Id binary(16) NOT NULL COMMENT '真实协作Step及调用UUID v7',
 InstanceId binary(16) NOT NULL COMMENT '本地源实例',
 ExecutionEpochId binary(16) NOT NULL COMMENT '实际源执行代次',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '可信源作用域',
 NodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '固定源节点键',
 ContinuationNodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '固定唯一后继',
 GrantId binary(16) NOT NULL COMMENT '已接受的双边授权',
 DeliveryId binary(16) NOT NULL COMMENT '独立逻辑请求投递UUID v7',
 BindingJson longtext NOT NULL COMMENT '真实源活动及固定授权版本证明',
 BindingHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '绑定证明SHA256',
 InputJson longtext NOT NULL COMMENT '不可变源输入',
 InputHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '原输入SHA256',
 InputFormRevision bigint NOT NULL COMMENT '原输入修订',
 ProjectionJson longtext NOT NULL COMMENT '仅含获准映射的固定投影',
 ProjectionHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '固定投影SHA256',
 ReservedActivities int NOT NULL COMMENT '从源全局预算预留的目标切片',
 StatusKey varchar(24) COLLATE utf8mb4_bin NOT NULL COMMENT '请求生命周期',
 Revision bigint NOT NULL COMMENT '本地CAS修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '源事务提交意图时间',
 UpdatedAtUtc datetime(6) NOT NULL COMMENT '本地状态推进时间',
 CONSTRAINT pk_fn_workflow_collaboration_call PRIMARY KEY (Id)
) COMMENT='工作流源协作调用及固定输入证明' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_workflow_collaboration_delivery (
 Id binary(16) NOT NULL COMMENT '逻辑请求投递身份',
 CallId binary(16) NOT NULL COMMENT '原本地源调用',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '由可信消息信封确定的作用域',
 EnvelopeMessageId binary(16) NOT NULL COMMENT '首次接收的物理消息身份',
 BindingHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '已核对的持久绑定摘要',
 StatusKey varchar(24) COLLATE utf8mb4_bin NOT NULL COMMENT '收件与Relay生命周期',
 Revision bigint NOT NULL COMMENT '本地收件CAS修订',
 ReceivedAtUtc datetime(6) NOT NULL COMMENT '首次收件时间',
 CONSTRAINT pk_fn_workflow_collaboration_delivery PRIMARY KEY (Id)
) COMMENT='工作流源信封本地收件事实；目标Relay另建作用域' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_collaboration_request_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_collaboration_request_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_grant' AND INDEX_NAME='ux_fn_workflow_collaboration_grant_source_id') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_collaboration_grant_source_id ON fn_workflow_collaboration_grant(Id,SourceTenantScopeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_call' AND INDEX_NAME='ux_fn_workflow_collaboration_call_scope') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_collaboration_call_scope ON fn_workflow_collaboration_call(Id,TenantScopeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_call' AND INDEX_NAME='ux_fn_workflow_collaboration_call_epoch_node') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_collaboration_call_epoch_node ON fn_workflow_collaboration_call(InstanceId,ExecutionEpochId,NodeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_call' AND INDEX_NAME='ux_fn_workflow_collaboration_call_delivery') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_collaboration_call_delivery ON fn_workflow_collaboration_call(DeliveryId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_delivery' AND INDEX_NAME='ux_fn_workflow_collaboration_delivery_message') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_collaboration_delivery_message ON fn_workflow_collaboration_delivery(TenantScopeKey,EnvelopeMessageId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_delivery' AND INDEX_NAME='ix_fn_workflow_collaboration_delivery_pending') THEN
  CREATE INDEX ix_fn_workflow_collaboration_delivery_pending ON fn_workflow_collaboration_delivery(StatusKey,ReceivedAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_call_instance') THEN
  ALTER TABLE fn_workflow_collaboration_call ADD CONSTRAINT fk_fn_workflow_collaboration_call_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_call_step') THEN
  ALTER TABLE fn_workflow_collaboration_call ADD CONSTRAINT fk_fn_workflow_collaboration_call_step FOREIGN KEY (Id,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_call_epoch') THEN
  ALTER TABLE fn_workflow_collaboration_call ADD CONSTRAINT fk_fn_workflow_collaboration_call_epoch FOREIGN KEY (Id,ExecutionEpochId) REFERENCES fn_workflow_step(Id,ExecutionEpochId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_call_grant') THEN
  ALTER TABLE fn_workflow_collaboration_call ADD CONSTRAINT fk_fn_workflow_collaboration_call_grant FOREIGN KEY (GrantId,TenantScopeKey) REFERENCES fn_workflow_collaboration_grant(Id,SourceTenantScopeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_delivery_call') THEN
  ALTER TABLE fn_workflow_collaboration_delivery ADD CONSTRAINT fk_fn_workflow_collaboration_delivery_call FOREIGN KEY (CallId,TenantScopeKey) REFERENCES fn_workflow_collaboration_call(Id,TenantScopeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_call' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_call_state' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_call ADD CONSTRAINT ck_fn_workflow_collaboration_call_state CHECK (Revision>0 AND InputFormRevision>0 AND ReservedActivities BETWEEN 2 AND 4096 AND StatusKey IN ('requested','accepted','result_ready','cancelling','cancelled','denied','completed'));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_call' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_call_json' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_call ADD CONSTRAINT ck_fn_workflow_collaboration_call_json CHECK (JSON_VALID(BindingJson) AND JSON_VALID(InputJson) AND JSON_VALID(ProjectionJson));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_delivery' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_delivery_state' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_delivery ADD CONSTRAINT ck_fn_workflow_collaboration_delivery_state CHECK (Revision>0 AND StatusKey IN ('received','dispatching','dispatched','rejected'));
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_collaboration_request_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_collaboration_request_ensure;
