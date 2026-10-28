-- 323：真实执行代次；辅助过程只探测自有约束。
CREATE TABLE IF NOT EXISTS fn_workflow_execution_epoch (
 Id binary(16) NOT NULL COMMENT '执行代次UUID v7；初始代次复用根UUID',
 InstanceId binary(16) NOT NULL COMMENT '实际根实例身份',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '根实例可信作用域',
 ParentEpochId binary(16) NULL COMMENT '外层真实代次；初始代次为空',
 CauseStepId binary(16) NULL COMMENT '产生新代次的实际因果步骤',
 RegionKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '编译固定的执行区域键；初始为root',
 IterationNumber int NOT NULL COMMENT '区域内固化迭代序号；初始为零',
 Depth int NOT NULL COMMENT '有界嵌套深度；初始为零',
 StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '活动或已关闭；暂停不关闭',
 Revision bigint NOT NULL COMMENT '乐观并发修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '代次创建UTC时间',
 ClosedAtUtc datetime(6) NULL COMMENT '代次关闭UTC时间；存量缺时间时为迁移观察时间',
 CONSTRAINT pk_fn_workflow_execution_epoch PRIMARY KEY (Id)
) COMMENT='工作流真实执行代次与区域因果身份；租约不能替代代次' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_execution_epoch_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_execution_epoch_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_execution_epoch' AND INDEX_NAME='ux_fn_workflow_execution_epoch_instance') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_execution_epoch_instance ON fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND INDEX_NAME='ux_fn_workflow_step_instance') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_step_instance ON fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_execution_epoch' AND INDEX_NAME='ix_fn_workflow_execution_epoch_instance') THEN
  CREATE INDEX ix_fn_workflow_execution_epoch_instance ON fn_workflow_execution_epoch(InstanceId,TenantScopeKey,StatusKey,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_execution_epoch' AND CONSTRAINT_NAME='ck_fn_workflow_execution_epoch_state') THEN
  ALTER TABLE fn_workflow_execution_epoch ADD CONSTRAINT ck_fn_workflow_execution_epoch_state CHECK (Revision>0 AND ((StatusKey='active' AND ClosedAtUtc IS NULL) OR (StatusKey='closed' AND ClosedAtUtc IS NOT NULL)) AND ((Id=InstanceId AND ParentEpochId IS NULL AND CauseStepId IS NULL AND RegionKey='root' AND IterationNumber=0 AND Depth=0) OR (Id<>InstanceId AND ParentEpochId IS NOT NULL AND CauseStepId IS NOT NULL AND IterationNumber BETWEEN 1 AND 32 AND Depth BETWEEN 1 AND 8)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_execution_epoch' AND CONSTRAINT_NAME='fk_fn_workflow_execution_epoch_instance') THEN
  ALTER TABLE fn_workflow_execution_epoch ADD CONSTRAINT fk_fn_workflow_execution_epoch_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_execution_epoch' AND CONSTRAINT_NAME='fk_fn_workflow_execution_epoch_parent') THEN
  ALTER TABLE fn_workflow_execution_epoch ADD CONSTRAINT fk_fn_workflow_execution_epoch_parent FOREIGN KEY (ParentEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_execution_epoch' AND CONSTRAINT_NAME='fk_fn_workflow_execution_epoch_cause') THEN
  ALTER TABLE fn_workflow_execution_epoch ADD CONSTRAINT fk_fn_workflow_execution_epoch_cause FOREIGN KEY (CauseStepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_execution_epoch_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_execution_epoch_ensure;
INSERT INTO fn_workflow_execution_epoch (Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc)
SELECT source.Id,source.Id,source.TenantScopeKey,NULL,NULL,'root',0,0,
 CASE WHEN source.StatusKey IN ('active','suspended') THEN 'active' ELSE 'closed' END,1,source.StartedAtUtc,
 CASE WHEN source.StatusKey IN ('active','suspended') THEN NULL ELSE COALESCE(source.CompletedAtUtc,source.CancelledAtUtc,UTC_TIMESTAMP(6)) END
FROM fn_workflow_instance AS source WHERE NOT EXISTS (SELECT 1 FROM fn_workflow_execution_epoch AS existing WHERE existing.Id=source.Id);

-- 活动与耐久工作代次的幂等回填和外键收紧。
DROP PROCEDURE IF EXISTS fn_workflow_execution_epoch_bindings_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_execution_epoch_bindings_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_execution_epoch' AND INDEX_NAME='ux_fn_workflow_execution_epoch_instance') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_execution_epoch_instance ON fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND COLUMN_NAME='ExecutionEpochId') THEN
  ALTER TABLE fn_workflow_step ADD COLUMN ExecutionEpochId binary(16) NULL COMMENT '真实执行代次；续办沿来源传递，重入不能复用旧同名活动';
 END IF;
 UPDATE fn_workflow_step SET ExecutionEpochId=InstanceId WHERE ExecutionEpochId IS NULL;
 IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND COLUMN_NAME='ExecutionEpochId' AND IS_NULLABLE='YES') THEN
  ALTER TABLE fn_workflow_step MODIFY COLUMN ExecutionEpochId binary(16) NOT NULL COMMENT '真实执行代次；续办沿来源传递，重入不能复用旧同名活动';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND INDEX_NAME='ix_fn_workflow_step_epoch') THEN
  CREATE INDEX ix_fn_workflow_step_epoch ON fn_workflow_step(InstanceId,ExecutionEpochId,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND CONSTRAINT_NAME='fk_fn_workflow_step_epoch') THEN
  ALTER TABLE fn_workflow_step ADD CONSTRAINT fk_fn_workflow_step_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND COLUMN_NAME='ExecutionEpochId') THEN
  ALTER TABLE fn_workflow_activity_work ADD COLUMN ExecutionEpochId binary(16) NULL COMMENT '真实执行代次；续办沿来源传递，重入不能复用旧同名活动';
 END IF;
 UPDATE fn_workflow_activity_work SET ExecutionEpochId=InstanceId WHERE ExecutionEpochId IS NULL;
 IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND COLUMN_NAME='ExecutionEpochId' AND IS_NULLABLE='YES') THEN
  ALTER TABLE fn_workflow_activity_work MODIFY COLUMN ExecutionEpochId binary(16) NOT NULL COMMENT '真实执行代次；续办沿来源传递，重入不能复用旧同名活动';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND INDEX_NAME='ix_fn_workflow_activity_work_epoch') THEN
  CREATE INDEX ix_fn_workflow_activity_work_epoch ON fn_workflow_activity_work(InstanceId,ExecutionEpochId,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND CONSTRAINT_NAME='fk_fn_workflow_activity_work_epoch') THEN
  ALTER TABLE fn_workflow_activity_work ADD CONSTRAINT fk_fn_workflow_activity_work_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_execution_epoch_bindings_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_execution_epoch_bindings_ensure;

-- 子调用代次按真实步骤回填；约束收紧与旧索引移除均可独立重跑。
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_epoch_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_subprocess_epoch_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND COLUMN_NAME='ExecutionEpochId') THEN
  ALTER TABLE fn_workflow_subprocess_call ADD COLUMN ExecutionEpochId binary(16) NULL COMMENT '真实父活动代次；同名节点只有同代次才能复用调用';
 END IF;
 UPDATE fn_workflow_subprocess_call AS callState INNER JOIN fn_workflow_step AS step ON step.Id=callState.Id AND step.InstanceId=callState.InstanceId SET callState.ExecutionEpochId=step.ExecutionEpochId WHERE callState.ExecutionEpochId IS NULL;
 IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND COLUMN_NAME='ExecutionEpochId' AND IS_NULLABLE='YES') THEN
  ALTER TABLE fn_workflow_subprocess_call MODIFY COLUMN ExecutionEpochId binary(16) NOT NULL COMMENT '真实父活动代次；同名节点只有同代次才能复用调用';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND INDEX_NAME='ux_fn_workflow_step_epoch') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_step_epoch ON fn_workflow_step(Id,ExecutionEpochId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_call_epoch') THEN
  ALTER TABLE fn_workflow_subprocess_call ADD CONSTRAINT fk_fn_workflow_subprocess_call_epoch FOREIGN KEY (Id,ExecutionEpochId) REFERENCES fn_workflow_step(Id,ExecutionEpochId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND INDEX_NAME='ux_fn_workflow_subprocess_call_epoch_node') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_subprocess_call_epoch_node ON fn_workflow_subprocess_call(InstanceId,ExecutionEpochId,NodeKey);
 END IF;
 IF EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND INDEX_NAME='ux_fn_workflow_subprocess_call_node') THEN
  DROP INDEX ux_fn_workflow_subprocess_call_node ON fn_workflow_subprocess_call;
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_subprocess_epoch_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_epoch_ensure;
