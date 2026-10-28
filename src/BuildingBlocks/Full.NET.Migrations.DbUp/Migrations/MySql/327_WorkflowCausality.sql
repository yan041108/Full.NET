-- 327：按实际身份建立因果；旧来源保持未知，结构步骤可恢复重跑。
CREATE TABLE IF NOT EXISTS fn_workflow_step_cause (
 Id binary(16) NOT NULL COMMENT '因果边UUID v7身份',
 InstanceId binary(16) NOT NULL COMMENT '所属实际根实例',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '根实例可信作用域',
 StepId binary(16) NOT NULL COMMENT '本次新建实际步骤',
 ParentStepId binary(16) NOT NULL COMMENT '明确因果父步骤；不得按时间推断',
 KindKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '顺序分叉汇合区域或退回因果类型',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '因果事实创建UTC时间',
 CONSTRAINT pk_fn_workflow_step_cause PRIMARY KEY (Id)
) COMMENT='工作流不可变实际步骤因果事实' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_causality_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_causality_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND COLUMN_NAME='CauseKindKey') THEN
  ALTER TABLE fn_workflow_activity_work ADD COLUMN CauseKindKey varchar(32) COLLATE utf8mb4_bin NULL COMMENT '显式推进因果类型；旧工作未知时为空，不按来源状态推断';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND CONSTRAINT_NAME='ck_fn_workflow_activity_work_cause_kind') THEN
  ALTER TABLE fn_workflow_activity_work ADD CONSTRAINT ck_fn_workflow_activity_work_cause_kind CHECK (CauseKindKey IS NULL OR CauseKindKey IN ('sequence','fork','join','region','return'));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND COLUMN_NAME='SourceStepId') THEN
  ALTER TABLE fn_workflow_activity_work ADD COLUMN SourceStepId binary(16) NULL COMMENT '续办输入的真实来源步骤；旧记录缺少证明时为空';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_join' AND COLUMN_NAME='ForkStepId') THEN
  ALTER TABLE fn_workflow_parallel_join ADD COLUMN ForkStepId binary(16) NULL COMMENT '该次实际分叉步骤；旧记录缺少证明时为空';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_branch_arrival' AND COLUMN_NAME='SourceStepId') THEN
  ALTER TABLE fn_workflow_parallel_branch_arrival ADD COLUMN SourceStepId binary(16) NULL COMMENT '该次分支到达的真实来源步骤；旧记录缺少证明时为空';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_branch_arrival' AND COLUMN_NAME='InstanceId') THEN
  ALTER TABLE fn_workflow_parallel_branch_arrival ADD COLUMN InstanceId binary(16) NULL COMMENT '从真实分叉回填的根实例身份';
 END IF;
 UPDATE fn_workflow_parallel_branch_arrival AS arrival INNER JOIN fn_workflow_parallel_join AS activation ON activation.Id=arrival.ParallelJoinId SET arrival.InstanceId=activation.InstanceId WHERE arrival.InstanceId IS NULL;
 IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_branch_arrival' AND COLUMN_NAME='InstanceId' AND IS_NULLABLE='YES') THEN
  ALTER TABLE fn_workflow_parallel_branch_arrival MODIFY COLUMN InstanceId binary(16) NOT NULL COMMENT '从真实分叉回填的根实例身份';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step_cause' AND INDEX_NAME='ux_fn_workflow_step_cause_edge') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_step_cause_edge ON fn_workflow_step_cause(StepId,ParentStepId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step_cause' AND CONSTRAINT_NAME='fk_fn_workflow_step_cause_child') THEN
  ALTER TABLE fn_workflow_step_cause ADD CONSTRAINT fk_fn_workflow_step_cause_child FOREIGN KEY (StepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step_cause' AND CONSTRAINT_NAME='fk_fn_workflow_step_cause_parent') THEN
  ALTER TABLE fn_workflow_step_cause ADD CONSTRAINT fk_fn_workflow_step_cause_parent FOREIGN KEY (ParentStepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND CONSTRAINT_NAME='fk_fn_workflow_activity_work_source') THEN
  ALTER TABLE fn_workflow_activity_work ADD CONSTRAINT fk_fn_workflow_activity_work_source FOREIGN KEY (SourceStepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_join' AND CONSTRAINT_NAME='fk_fn_workflow_parallel_join_fork_step') THEN
  ALTER TABLE fn_workflow_parallel_join ADD CONSTRAINT fk_fn_workflow_parallel_join_fork_step FOREIGN KEY (ForkStepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_branch_arrival' AND CONSTRAINT_NAME='fk_fn_workflow_parallel_branch_arrival_source') THEN
  ALTER TABLE fn_workflow_parallel_branch_arrival ADD CONSTRAINT fk_fn_workflow_parallel_branch_arrival_source FOREIGN KEY (SourceStepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_branch_arrival' AND CONSTRAINT_NAME='fk_fn_workflow_parallel_branch_arrival_root_join') THEN
  ALTER TABLE fn_workflow_parallel_branch_arrival ADD CONSTRAINT fk_fn_workflow_parallel_branch_arrival_root_join FOREIGN KEY (ParallelJoinId,InstanceId) REFERENCES fn_workflow_parallel_join(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step_cause' AND CONSTRAINT_NAME='ck_fn_workflow_step_cause_kind') THEN
  ALTER TABLE fn_workflow_step_cause ADD CONSTRAINT ck_fn_workflow_step_cause_kind CHECK (StepId<>ParentStepId AND KindKey IN ('sequence','fork','join','region','return'));
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_causality_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_causality_ensure;
