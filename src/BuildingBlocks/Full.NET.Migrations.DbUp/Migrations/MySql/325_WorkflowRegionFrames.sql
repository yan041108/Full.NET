-- 325：固定区域边界与真实外层分支；DDL逐项探测，不猜测存量非根代次的区域。
CREATE TABLE IF NOT EXISTS fn_workflow_region_frame (
 ExecutionEpochId binary(16) NOT NULL COMMENT '区域实际执行代次UUID v7',
 InstanceId binary(16) NOT NULL COMMENT '区域所属真实根实例',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '从真实根绑定的可信作用域',
 DefinitionVersionId binary(16) NOT NULL COMMENT '与所属根相同的固定定义版本',
 EntryNodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '编译验证的区域唯一入口节点引用',
 ExitNodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '编译验证的区域唯一出口节点引用',
 ParentForkActivationId binary(16) NULL COMMENT '区域外层真实分叉激活；根区域为空',
 ParentBranchKey varchar(128) COLLATE utf8mb4_bin NULL COMMENT '外层分叉固化的实际分支键',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '区域帧创建UTC时间',
 CONSTRAINT pk_fn_workflow_region_frame PRIMARY KEY (ExecutionEpochId)
) COMMENT='工作流区域代次固定边界及真实外层分支身份' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_region_frame_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_region_frame_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_instance' AND INDEX_NAME='ux_fn_workflow_instance_version') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_instance_version ON fn_workflow_instance(Id,DefinitionVersionId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_join' AND INDEX_NAME='ux_fn_workflow_parallel_join_instance') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_parallel_join_instance ON fn_workflow_parallel_join(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_execution_epoch' AND INDEX_NAME='ux_fn_workflow_execution_epoch_iteration') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_execution_epoch_iteration ON fn_workflow_execution_epoch(ParentEpochId,RegionKey,IterationNumber);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_execution_epoch' AND INDEX_NAME='ux_fn_workflow_execution_epoch_cause') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_execution_epoch_cause ON fn_workflow_execution_epoch(CauseStepId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_region_frame' AND CONSTRAINT_NAME='ck_fn_workflow_region_frame_state') THEN
  ALTER TABLE fn_workflow_region_frame ADD CONSTRAINT ck_fn_workflow_region_frame_state CHECK (
   CHAR_LENGTH(EntryNodeKey)>0 AND CHAR_LENGTH(ExitNodeKey)>0 AND EntryNodeKey<>ExitNodeKey AND
   ((ParentForkActivationId IS NULL AND ParentBranchKey IS NULL) OR
    (ParentForkActivationId IS NOT NULL AND ParentBranchKey IS NOT NULL AND CHAR_LENGTH(ParentBranchKey)>0)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_region_frame' AND CONSTRAINT_NAME='fk_fn_workflow_region_frame_epoch') THEN
  ALTER TABLE fn_workflow_region_frame ADD CONSTRAINT fk_fn_workflow_region_frame_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_region_frame' AND CONSTRAINT_NAME='fk_fn_workflow_region_frame_version') THEN
  ALTER TABLE fn_workflow_region_frame ADD CONSTRAINT fk_fn_workflow_region_frame_version FOREIGN KEY (InstanceId,DefinitionVersionId) REFERENCES fn_workflow_instance(Id,DefinitionVersionId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_region_frame' AND CONSTRAINT_NAME='fk_fn_workflow_region_frame_fork') THEN
  ALTER TABLE fn_workflow_region_frame ADD CONSTRAINT fk_fn_workflow_region_frame_fork FOREIGN KEY (ParentForkActivationId,InstanceId) REFERENCES fn_workflow_parallel_join(Id,InstanceId);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_region_frame_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_region_frame_ensure;
