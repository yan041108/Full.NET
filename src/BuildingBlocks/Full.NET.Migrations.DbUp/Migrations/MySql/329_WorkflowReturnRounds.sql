-- 329：退回冻结原目标并新建代次；只有原入/出工作收据可以授权跨代次来源。
CREATE TABLE IF NOT EXISTS fn_workflow_return_round (
 Id binary(16) NOT NULL COMMENT '退回轮次与新子代次UUID v7',
 InstanceId binary(16) NOT NULL COMMENT '所属真实根实例',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '根实例可信作用域',
 DefinitionVersionId binary(16) NOT NULL COMMENT '根固定定义版本',
 ParentEpochId binary(16) NOT NULL COMMENT '退回进入前的实际外层代次',
 EntrySourceEpochId binary(16) NOT NULL COMMENT '原退回操作来源的真实代次；可随旧fork关闭，与存活父代次分离',
 SourceStepId binary(16) NOT NULL COMMENT '原已退回来源步骤',
 TargetStepId binary(16) NOT NULL COMMENT '原已完成目标及其不可变人员快照',
 EntryNodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '固定重入入口节点引用',
 ExitNodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '固定本轮唯一正常出口',
 NextNodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '正常出口后的真实后继引用',
 RegionNodesJson longtext NOT NULL COMMENT '有界结构区域固定节点集合',
 RegionHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '结构区域节点集合SHA256摘要',
 TargetSnapshotHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '原目标人员及分组快照SHA256摘要',
 SourceParallelJoinId binary(16) NULL COMMENT '原已退回来源步骤的真实分叉身份',
 SourceParallelBranchKey varchar(128) COLLATE utf8mb4_bin NULL COMMENT '原已退回来源步骤的真实分支键',
 ParallelJoinId binary(16) NULL COMMENT '本轮真实外层分叉身份',
 ParallelBranchKey varchar(128) COLLATE utf8mb4_bin NULL COMMENT '本轮真实外层分支键',
 EntryWorkId binary(16) NOT NULL COMMENT '进入新代次的唯一工作身份',
 InputHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '原进入工作固定输入SHA256摘要',
 InputFormRevision bigint NOT NULL COMMENT '原进入工作固定输入修订',
 ExitWorkId binary(16) NULL COMMENT '退出到外层代次的唯一工作身份',
 ExitSourceEpochId binary(16) NULL COMMENT '真实完成出口来源代次；级联退出保留原子代次',
 ExitStepId binary(16) NULL COMMENT '本轮真实完成出口步骤',
 ExitInputHash varchar(64) COLLATE utf8mb4_bin NULL COMMENT '退出工作固定输入SHA256摘要',
 ExitInputFormRevision bigint NULL COMMENT '退出工作固定输入修订',
 Revision bigint NOT NULL COMMENT '轮次唯一退出CAS修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '进入本轮UTC时间',
 ExitedAtUtc datetime(6) NULL COMMENT '本轮正常退出UTC时间',
 CONSTRAINT pk_fn_workflow_return_round PRIMARY KEY (Id)
) COMMENT='工作流退回新代次及不可变目标、区域和唯一入出工作收据' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_return_round_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_return_round_ensure()
BEGIN
 -- 已建表但未记账时依实际来源回填；无法恢复的来源令NOT NULL收紧失败。
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_return_round' AND COLUMN_NAME='EntrySourceEpochId') THEN
  ALTER TABLE fn_workflow_return_round ADD COLUMN EntrySourceEpochId binary(16) NULL COMMENT '原退回操作来源的真实代次；可随旧fork关闭，与存活父代次分离';
 END IF;
 UPDATE fn_workflow_return_round AS round INNER JOIN fn_workflow_step AS step ON step.Id=round.SourceStepId AND step.InstanceId=round.InstanceId
  SET round.EntrySourceEpochId=step.ExecutionEpochId WHERE round.EntrySourceEpochId IS NULL;
 IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_return_round' AND COLUMN_NAME='EntrySourceEpochId' AND IS_NULLABLE='YES') THEN
  ALTER TABLE fn_workflow_return_round MODIFY COLUMN EntrySourceEpochId binary(16) NOT NULL COMMENT '原退回操作来源的真实代次；可随旧fork关闭，与存活父代次分离';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_return_round_entry_epoch') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_entry_epoch FOREIGN KEY (EntrySourceEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND COLUMN_NAME='ReturnRoundId') THEN
  ALTER TABLE fn_workflow_activity_work ADD COLUMN ReturnRoundId binary(16) NULL COMMENT '原退回轮次入出收据；普通工作为空';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_return_round' AND INDEX_NAME='ux_fn_workflow_return_round_instance') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_return_round_instance ON fn_workflow_return_round(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_return_round' AND INDEX_NAME='ux_fn_workflow_return_round_entry_work') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_return_round_entry_work ON fn_workflow_return_round(EntryWorkId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_return_round' AND INDEX_NAME='ux_fn_workflow_return_round_exit_work') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_return_round_exit_work ON fn_workflow_return_round(ExitWorkId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_return_round_epoch') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_epoch FOREIGN KEY (Id,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_return_round_parent_epoch') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_parent_epoch FOREIGN KEY (ParentEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_return_round_source') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_source FOREIGN KEY (SourceStepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_return_round_target') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_target FOREIGN KEY (TargetStepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_return_round_exit') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_exit FOREIGN KEY (ExitStepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_return_round_version') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_version FOREIGN KEY (InstanceId,DefinitionVersionId) REFERENCES fn_workflow_instance(Id,DefinitionVersionId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_return_round_fork') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_fork FOREIGN KEY (ParallelJoinId,InstanceId) REFERENCES fn_workflow_parallel_join(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_activity_work_return') THEN
  ALTER TABLE fn_workflow_activity_work ADD CONSTRAINT fk_fn_workflow_activity_work_return FOREIGN KEY (ReturnRoundId,InstanceId) REFERENCES fn_workflow_return_round(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_return_round' AND CONSTRAINT_NAME='ck_fn_workflow_return_round_state' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT ck_fn_workflow_return_round_state CHECK (((SourceParallelJoinId IS NULL AND SourceParallelBranchKey IS NULL) OR (SourceParallelJoinId IS NOT NULL AND SourceParallelBranchKey IS NOT NULL)) AND Revision IN (1,2) AND InputFormRevision>0 AND EntryNodeKey<>ExitNodeKey AND ((ParallelJoinId IS NULL AND ParallelBranchKey IS NULL) OR (ParallelJoinId IS NOT NULL AND ParallelBranchKey IS NOT NULL)) AND ((Revision=1 AND ExitWorkId IS NULL AND ExitStepId IS NULL AND ExitSourceEpochId IS NULL AND ExitInputHash IS NULL AND ExitInputFormRevision IS NULL AND ExitedAtUtc IS NULL) OR (Revision=2 AND ExitWorkId IS NOT NULL AND ExitStepId IS NOT NULL AND ExitSourceEpochId IS NOT NULL AND ExitInputHash IS NOT NULL AND ExitInputFormRevision>0 AND ExitedAtUtc IS NOT NULL AND ExitWorkId<>EntryWorkId)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND CONSTRAINT_NAME='ck_fn_workflow_activity_work_receipt' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_activity_work ADD CONSTRAINT ck_fn_workflow_activity_work_receipt CHECK ((CASE WHEN ReturnRoundId IS NULL THEN 0 ELSE 1 END + CASE WHEN RouteDecisionId IS NULL THEN 0 ELSE 1 END + CASE WHEN ResumeBarrierId IS NULL THEN 0 ELSE 1 END)<=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_return_round_source_fork') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_source_fork FOREIGN KEY (SourceParallelJoinId,InstanceId) REFERENCES fn_workflow_parallel_join(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_return_round_exit_epoch') THEN
  ALTER TABLE fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_exit_epoch FOREIGN KEY (ExitSourceEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_return_round_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_return_round_ensure;
