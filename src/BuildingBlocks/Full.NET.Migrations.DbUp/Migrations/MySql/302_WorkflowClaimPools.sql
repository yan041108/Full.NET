-- 抢单只在真实领取后创建Todo；冻结候选不替代领取时的当前授权来源。
CREATE TABLE IF NOT EXISTS fn_workflow_claim (
    Id binary(16) NOT NULL COMMENT '活动步骤及候选池标识',
    InstanceId binary(16) NOT NULL COMMENT '所属流程实例标识',
    StatusKey varchar(24) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '候选池状态：可领取、已领取、已完成或已取消',
    Revision bigint NOT NULL COMMENT '候选池并发修订号',
    ClaimCycle int NOT NULL COMMENT '实际领取代次，释放不能重置预算',
    AllowRelease tinyint(1) NOT NULL COMMENT '发布策略是否允许本人释放',
    CurrentTodoId binary(16) NULL COMMENT '当前实际领取待办标识',
    CurrentAssigneeUserId binary(16) NULL COMMENT '当前真实持有人标识',
    DueAtUtc datetime(6) NULL COMMENT '到达时确定且释放不重置的绝对截止时间',
    NextReminderAtUtc datetime(6) NULL COMMENT '剩余提醒时间，未领取不发个人提醒',
    EscalateAtUtc datetime(6) NULL COMMENT '绝对升级时间',
    MaxReminderCount int NOT NULL COMMENT '最大提醒次数',
    ReminderIntervalMinutes int NOT NULL COMMENT '提醒间隔分钟数',
    ReminderCount int NOT NULL COMMENT '已经发送的提醒次数',
    EscalationRecipientUserId binary(16) NULL COMMENT '升级接收人标识',
    EscalatedAtUtc datetime(6) NULL COMMENT '实际升级时间',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '候选池到达时间',
    CompletedAtUtc datetime(6) NULL COMMENT '候选池终结时间',
    CONSTRAINT pk_fn_workflow_claim PRIMARY KEY (Id),
    CONSTRAINT fk_fn_workflow_claim_step FOREIGN KEY (Id) REFERENCES fn_workflow_step(Id),
    CONSTRAINT fk_fn_workflow_claim_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_claim_todo FOREIGN KEY (CurrentTodoId) REFERENCES fn_workflow_todo(Id)
) COMMENT='工作流候选领取池；到达时没有个人待办' ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS fn_workflow_claim_candidate (
    Id binary(16) NOT NULL COMMENT '固定候选事实标识',
    InstanceId binary(16) NOT NULL COMMENT '所属流程实例标识',
    PoolId binary(16) NOT NULL COMMENT '所属候选池标识',
    UserId binary(16) NOT NULL COMMENT '实际解析且去重的候选人标识',
    SequenceNo int NOT NULL COMMENT '到达时稳定顺序',
    CONSTRAINT pk_fn_workflow_claim_candidate PRIMARY KEY (Id),
    CONSTRAINT fk_fn_workflow_claim_candidate_pool FOREIGN KEY (PoolId) REFERENCES fn_workflow_claim(Id),
    CONSTRAINT fk_fn_workflow_claim_candidate_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id)
) COMMENT='工作流到达时固定的候选人，不授予后来领取时的权限' ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS fn_workflow_claim_attempt (
    Id binary(16) NOT NULL COMMENT '实际领取事实标识',
    InstanceId binary(16) NOT NULL COMMENT '所属流程实例标识',
    PoolId binary(16) NOT NULL COMMENT '所属候选池标识',
    TodoId binary(16) NOT NULL COMMENT '当前实际待办标识',
    AssigneeUserId binary(16) NOT NULL COMMENT '当前真实持有人标识',
    OriginalAssigneeUserId binary(16) NOT NULL COMMENT '本次领取的原始持有人标识',
    ClaimedByUserId binary(16) NOT NULL COMMENT '实际执行领取或指派的用户标识',
    AuthorizationKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '本次领取或指派的精确授权依据',
    ClaimCycle int NOT NULL COMMENT '本次领取代次',
    StatusKey varchar(24) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '领取事实状态：活动、释放、完成或取消',
    Revision bigint NOT NULL COMMENT '领取事实并发修订号',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '真实领取时间',
    CompletedAtUtc datetime(6) NULL COMMENT '本次领取关闭时间',
    CONSTRAINT pk_fn_workflow_claim_attempt PRIMARY KEY (Id),
    CONSTRAINT fk_fn_workflow_claim_attempt_pool FOREIGN KEY (PoolId) REFERENCES fn_workflow_claim(Id),
    CONSTRAINT fk_fn_workflow_claim_attempt_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_claim_attempt_todo FOREIGN KEY (TodoId) REFERENCES fn_workflow_todo(Id)
) COMMENT='工作流每次真实领取历史；交接重绑同一领取事实' ENGINE=InnoDB;

DROP PROCEDURE IF EXISTS fn_workflow_ensure_claim_facts;
DELIMITER $$
CREATE PROCEDURE fn_workflow_ensure_claim_facts()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND COLUMN_NAME='ClaimAttemptId') THEN
    ALTER TABLE fn_workflow_todo_transfer ADD COLUMN ClaimAttemptId binary(16) NULL COMMENT '绑定本次实际领取事实，不能同时绑定其他人工成员';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='fk_fn_workflow_todo_transfer_claim_attempt') THEN
    ALTER TABLE fn_workflow_todo_transfer ADD CONSTRAINT fk_fn_workflow_todo_transfer_claim_attempt FOREIGN KEY (ClaimAttemptId) REFERENCES fn_workflow_claim_attempt(Id);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim' AND CONSTRAINT_NAME='ck_fn_workflow_claim_policy') THEN
    ALTER TABLE fn_workflow_claim ADD CONSTRAINT ck_fn_workflow_claim_policy CHECK (Revision>=1 AND ClaimCycle BETWEEN 0 AND 32 AND MaxReminderCount>=0 AND ReminderCount BETWEEN 0 AND MaxReminderCount AND ReminderIntervalMinutes>=0 AND (MaxReminderCount=0 OR ReminderIntervalMinutes>=1));
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim' AND CONSTRAINT_NAME='ck_fn_workflow_claim_state') THEN
    ALTER TABLE fn_workflow_claim ADD CONSTRAINT ck_fn_workflow_claim_state CHECK (((StatusKey='available' AND ClaimCycle<32 AND CurrentTodoId IS NULL AND CurrentAssigneeUserId IS NULL AND CompletedAtUtc IS NULL) OR (StatusKey='claimed' AND CurrentTodoId IS NOT NULL AND CurrentAssigneeUserId IS NOT NULL AND ClaimCycle>=1 AND CompletedAtUtc IS NULL) OR (StatusKey='completed' AND ClaimCycle>=1 AND CurrentTodoId IS NOT NULL AND CurrentAssigneeUserId IS NOT NULL AND CompletedAtUtc IS NOT NULL) OR (StatusKey='cancelled' AND CompletedAtUtc IS NOT NULL AND ((CurrentTodoId IS NULL AND CurrentAssigneeUserId IS NULL) OR (CurrentTodoId IS NOT NULL AND CurrentAssigneeUserId IS NOT NULL)))));
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim_candidate' AND CONSTRAINT_NAME='ck_fn_workflow_claim_candidate_sequence') THEN
    ALTER TABLE fn_workflow_claim_candidate ADD CONSTRAINT ck_fn_workflow_claim_candidate_sequence CHECK (SequenceNo BETWEEN 1 AND 20);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim_attempt' AND CONSTRAINT_NAME='ck_fn_workflow_claim_attempt_policy') THEN
    ALTER TABLE fn_workflow_claim_attempt ADD CONSTRAINT ck_fn_workflow_claim_attempt_policy CHECK (Revision>=1 AND ClaimCycle BETWEEN 1 AND 32 AND (AuthorizationKey='workflow.claim_pools.assign' OR (AuthorizationKey='workflow.claim_pools.claim' AND ClaimedByUserId=OriginalAssigneeUserId)));
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim_attempt' AND CONSTRAINT_NAME='ck_fn_workflow_claim_attempt_state') THEN
    ALTER TABLE fn_workflow_claim_attempt ADD CONSTRAINT ck_fn_workflow_claim_attempt_state CHECK (((StatusKey='active' AND CompletedAtUtc IS NULL) OR (StatusKey IN ('released','completed','cancelled') AND CompletedAtUtc IS NOT NULL)));
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_claim_member') THEN
    ALTER TABLE fn_workflow_todo_transfer ADD CONSTRAINT ck_fn_workflow_todo_transfer_claim_member CHECK (((CASE WHEN SlotId IS NULL THEN 0 ELSE 1 END)+(CASE WHEN TaskMemberId IS NULL THEN 0 ELSE 1 END)+(CASE WHEN VoteBallotId IS NULL THEN 0 ELSE 1 END)+(CASE WHEN ClaimAttemptId IS NULL THEN 0 ELSE 1 END))<=1);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim' AND INDEX_NAME='ix_fn_workflow_claim_instance_status') THEN
    CREATE INDEX ix_fn_workflow_claim_instance_status ON fn_workflow_claim(InstanceId, StatusKey);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim' AND INDEX_NAME='ix_fn_workflow_claim_due') THEN
    CREATE INDEX ix_fn_workflow_claim_due ON fn_workflow_claim(StatusKey, DueAtUtc, Id);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim' AND INDEX_NAME='uq_fn_workflow_claim_todo') THEN
    CREATE UNIQUE INDEX uq_fn_workflow_claim_todo ON fn_workflow_claim(CurrentTodoId);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim_candidate' AND INDEX_NAME='uq_fn_workflow_claim_candidate_user') THEN
    CREATE UNIQUE INDEX uq_fn_workflow_claim_candidate_user ON fn_workflow_claim_candidate(PoolId, UserId);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim_candidate' AND INDEX_NAME='uq_fn_workflow_claim_candidate_sequence') THEN
    CREATE UNIQUE INDEX uq_fn_workflow_claim_candidate_sequence ON fn_workflow_claim_candidate(PoolId, SequenceNo);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim_candidate' AND INDEX_NAME='ix_fn_workflow_claim_candidate_user_pool') THEN
    CREATE INDEX ix_fn_workflow_claim_candidate_user_pool ON fn_workflow_claim_candidate(UserId, PoolId);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim_attempt' AND INDEX_NAME='uq_fn_workflow_claim_attempt_todo') THEN
    CREATE UNIQUE INDEX uq_fn_workflow_claim_attempt_todo ON fn_workflow_claim_attempt(TodoId);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim_attempt' AND INDEX_NAME='uq_fn_workflow_claim_attempt_cycle') THEN
    CREATE UNIQUE INDEX uq_fn_workflow_claim_attempt_cycle ON fn_workflow_claim_attempt(PoolId, ClaimCycle);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim_attempt' AND INDEX_NAME='ix_fn_workflow_claim_attempt_instance') THEN
    CREATE INDEX ix_fn_workflow_claim_attempt_instance ON fn_workflow_claim_attempt(InstanceId, StatusKey);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND INDEX_NAME='ix_fn_workflow_todo_transfer_claim_attempt') THEN
    CREATE INDEX ix_fn_workflow_todo_transfer_claim_attempt ON fn_workflow_todo_transfer(ClaimAttemptId);
  END IF;
  IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_human_member') THEN
    ALTER TABLE fn_workflow_todo_transfer DROP CHECK ck_fn_workflow_todo_transfer_human_member;
  END IF;
END$$
DELIMITER ;
CALL fn_workflow_ensure_claim_facts();
DROP PROCEDURE IF EXISTS fn_workflow_ensure_claim_facts;
