-- 315：活动级SLA固定系统处置与显式暂停策略；独立恢复约束和索引。
CREATE TABLE IF NOT EXISTS fn_workflow_sla (
 Id binary(16) NOT NULL COMMENT '真实步骤标识；复用应用端UUID v7激活身份',
 InstanceId binary(16) NOT NULL COMMENT '所属Workflow实例标识',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '来自可信父实例的作用域键',
 ActionKey varchar(24) COLLATE utf8mb4_bin NOT NULL COMMENT '发布固定的系统处置；不代表个人审批票',
 PauseModeKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT 'elapsed继续计时或freeze冻结剩余时间',
 DueAtUtc datetime(6) NOT NULL COMMENT '该次活动绝对截止UTC微秒时间',
 RemainingMilliseconds bigint NULL COMMENT '显式冻结时向上取整的剩余墙钟毫秒',
 PausedAtUtc datetime(6) NULL COMMENT '显式冻结时间；继续计时模式为空',
 StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT 'waiting待处理、fired已处置、cancelled已失效',
 Revision bigint NOT NULL COMMENT '截止事实乐观锁修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '活动截止创建UTC时间',
 CompletedAtUtc datetime(6) NULL COMMENT '处置或失效确认UTC时间',
 CONSTRAINT pk_fn_workflow_sla PRIMARY KEY (Id)
) COMMENT='Workflow活动截止及系统处置事实' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_ensure_sla;
DELIMITER $$
CREATE PROCEDURE fn_workflow_ensure_sla()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_sla' AND CONSTRAINT_NAME='ck_fn_workflow_sla_state') THEN
  ALTER TABLE fn_workflow_sla ADD CONSTRAINT ck_fn_workflow_sla_state CHECK (StatusKey IN ('waiting','fired','cancelled') AND Revision>0 AND ActionKey IN ('suspend','auto_reject','auto_pass') AND PauseModeKey IN ('elapsed','freeze') AND ((StatusKey='waiting' AND CompletedAtUtc IS NULL) OR (StatusKey<>'waiting' AND CompletedAtUtc IS NOT NULL)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_sla' AND CONSTRAINT_NAME='ck_fn_workflow_sla_pause') THEN
  ALTER TABLE fn_workflow_sla ADD CONSTRAINT ck_fn_workflow_sla_pause CHECK ((PausedAtUtc IS NULL AND RemainingMilliseconds IS NULL) OR (PauseModeKey='freeze' AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds IS NOT NULL AND RemainingMilliseconds>=0 AND RemainingMilliseconds<=31536000000));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_sla' AND INDEX_NAME='ix_fn_workflow_sla_due') THEN
  CREATE INDEX ix_fn_workflow_sla_due ON fn_workflow_sla(StatusKey,DueAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_sla' AND INDEX_NAME='ix_fn_workflow_sla_instance') THEN
  CREATE INDEX ix_fn_workflow_sla_instance ON fn_workflow_sla(InstanceId,StatusKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_sla' AND CONSTRAINT_NAME='fk_fn_workflow_sla_step') THEN
  ALTER TABLE fn_workflow_sla ADD CONSTRAINT fk_fn_workflow_sla_step FOREIGN KEY (Id) REFERENCES fn_workflow_step(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_sla' AND CONSTRAINT_NAME='fk_fn_workflow_sla_instance') THEN
  ALTER TABLE fn_workflow_sla ADD CONSTRAINT fk_fn_workflow_sla_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_ensure_sla();
DROP PROCEDURE IF EXISTS fn_workflow_ensure_sla;
