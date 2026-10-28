-- 328：固定真实路由决策；结构逐项探测，未知选择不以普通Resume重算。
CREATE TABLE IF NOT EXISTS fn_workflow_route_decision (
 Id binary(16) NOT NULL COMMENT '真实路由步骤及决策UUID v7身份',
 InstanceId binary(16) NOT NULL COMMENT '所属实际根实例',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '根实例可信作用域',
 ExecutionEpochId binary(16) NOT NULL COMMENT '实际来源执行代次',
 DefinitionVersionId binary(16) NOT NULL COMMENT '原根固定发布版本',
 NodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '发布路由节点引用',
 InputJson longtext NOT NULL COMMENT '原分支或主表单固定输入',
 InputHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '固定输入SHA256摘要',
 InputFormRevision bigint NOT NULL COMMENT '原输入修订',
 PolicyHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '固定发布路由策略SHA256摘要',
 StatusKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT 'decision_unknown或ready或applied或cancelled',
 SelectedTargetKey varchar(64) COLLATE utf8mb4_bin NULL COMMENT '已发布目标机器键；正常后继为空',
 NextNodeKey varchar(128) COLLATE utf8mb4_bin NULL COMMENT '固定实际后继节点引用',
 TargetEpochId binary(16) NULL COMMENT '应用决策后实际后继代次',
 NextWorkId binary(16) NULL COMMENT '应用事务预留的唯一后继Work身份',
 ParallelJoinId binary(16) NULL COMMENT '来源真实分叉身份',
 ParallelBranchKey varchar(128) COLLATE utf8mb4_bin NULL COMMENT '来源真实分支键',
 TargetParallelJoinId binary(16) NULL COMMENT '后继恢复的真实分叉身份',
 TargetParallelBranchKey varchar(128) COLLATE utf8mb4_bin NULL COMMENT '后继真实分支键',
 Revision bigint NOT NULL COMMENT '决策乐观并发修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '原路由激活UTC时间',
 AppliedAtUtc datetime(6) NULL COMMENT '决策与后继同事务应用UTC时间',
 CONSTRAINT pk_fn_workflow_route_decision PRIMARY KEY (Id)
) COMMENT='工作流耐久路由固定输入、选择与唯一后继工作身份' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_workflow_route_resolution (
 Id binary(16) NOT NULL COMMENT '修复收据UUID v7身份',
 RouteId binary(16) NOT NULL COMMENT '原真实路由决策身份',
 InstanceId binary(16) NOT NULL COMMENT '所属实际根实例',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '根实例可信作用域',
 ActorUserId binary(16) NOT NULL COMMENT '实际授权修复操作者；不跨模块外键',
 IdempotencyKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '操作者修复幂等键',
 RequestHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '目标及原因固定请求摘要',
 Reason varchar(512) COLLATE utf8mb4_bin NOT NULL COMMENT '实际修复理由',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '首次修复UTC时间',
 CONSTRAINT pk_fn_workflow_route_resolution PRIMARY KEY (Id)
) COMMENT='工作流不确定路由的精确授权修复幂等收据' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_route_decision_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_route_decision_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND COLUMN_NAME='RouteDecisionId') THEN
  ALTER TABLE fn_workflow_activity_work ADD COLUMN RouteDecisionId binary(16) NULL COMMENT '实际路由应用收据；普通工作为空';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_decision' AND INDEX_NAME='ux_fn_workflow_route_decision_instance') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_route_decision_instance ON fn_workflow_route_decision(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_decision' AND INDEX_NAME='ux_fn_workflow_route_decision_work') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_route_decision_work ON fn_workflow_route_decision(NextWorkId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_resolution' AND INDEX_NAME='ux_fn_workflow_route_resolution_receipt') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_route_resolution_receipt ON fn_workflow_route_resolution(RouteId,ActorUserId,IdempotencyKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_decision' AND CONSTRAINT_NAME='fk_fn_workflow_route_decision_step') THEN
  ALTER TABLE fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_step FOREIGN KEY (Id,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_decision' AND CONSTRAINT_NAME='fk_fn_workflow_route_decision_source_epoch') THEN
  ALTER TABLE fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_source_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_decision' AND CONSTRAINT_NAME='fk_fn_workflow_route_decision_target_epoch') THEN
  ALTER TABLE fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_target_epoch FOREIGN KEY (TargetEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_decision' AND CONSTRAINT_NAME='fk_fn_workflow_route_decision_version') THEN
  ALTER TABLE fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_version FOREIGN KEY (InstanceId,DefinitionVersionId) REFERENCES fn_workflow_instance(Id,DefinitionVersionId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_decision' AND CONSTRAINT_NAME='fk_fn_workflow_route_decision_source_fork') THEN
  ALTER TABLE fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_source_fork FOREIGN KEY (ParallelJoinId,InstanceId) REFERENCES fn_workflow_parallel_join(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_decision' AND CONSTRAINT_NAME='fk_fn_workflow_route_decision_target_fork') THEN
  ALTER TABLE fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_target_fork FOREIGN KEY (TargetParallelJoinId,InstanceId) REFERENCES fn_workflow_parallel_join(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_resolution' AND CONSTRAINT_NAME='fk_fn_workflow_route_resolution_route') THEN
  ALTER TABLE fn_workflow_route_resolution ADD CONSTRAINT fk_fn_workflow_route_resolution_route FOREIGN KEY (RouteId,InstanceId) REFERENCES fn_workflow_route_decision(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND CONSTRAINT_NAME='fk_fn_workflow_activity_work_route') THEN
  ALTER TABLE fn_workflow_activity_work ADD CONSTRAINT fk_fn_workflow_activity_work_route FOREIGN KEY (RouteDecisionId,InstanceId) REFERENCES fn_workflow_route_decision(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_route_decision' AND CONSTRAINT_NAME='ck_fn_workflow_route_decision_state') THEN
  ALTER TABLE fn_workflow_route_decision ADD CONSTRAINT ck_fn_workflow_route_decision_state CHECK (Revision>0 AND InputFormRevision>0 AND StatusKey IN ('decision_unknown','ready','applied','cancelled') AND ((ParallelJoinId IS NULL AND ParallelBranchKey IS NULL) OR (ParallelJoinId IS NOT NULL AND ParallelBranchKey IS NOT NULL)) AND ((TargetParallelJoinId IS NULL AND TargetParallelBranchKey IS NULL) OR (TargetParallelJoinId IS NOT NULL AND TargetParallelBranchKey IS NOT NULL)) AND (StatusKey<>'decision_unknown' OR (SelectedTargetKey IS NULL AND NextNodeKey IS NULL AND TargetEpochId IS NULL AND NextWorkId IS NULL AND AppliedAtUtc IS NULL)) AND (StatusKey NOT IN ('ready','applied') OR (NextNodeKey IS NOT NULL AND NextWorkId IS NOT NULL)) AND (StatusKey<>'applied' OR (TargetEpochId IS NOT NULL AND AppliedAtUtc IS NOT NULL)));
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_route_decision_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_route_decision_ensure;
