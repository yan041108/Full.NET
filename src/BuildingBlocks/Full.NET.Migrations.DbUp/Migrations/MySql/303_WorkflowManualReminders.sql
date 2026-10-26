-- 手动催办固定请求、真实收件人数量和步骤预算；不冒充办理动作或自动超时信号。
CREATE TABLE IF NOT EXISTS fn_workflow_manual_reminder (
    Id binary(16) NOT NULL COMMENT '催办请求及实际动作标识',
    InstanceId binary(16) NOT NULL COMMENT '可信流程实例标识',
    StepId binary(16) NOT NULL COMMENT '实际活动步骤标识，交接不能重置催办预算',
    RequestedByUserId binary(16) NOT NULL COMMENT '实际发起催办的流程发起人',
    InstanceRevision bigint NOT NULL COMMENT '受理时的实例修订号，知会不推进流程',
    StepRevision bigint NOT NULL COMMENT '受理时的实际步骤修订号',
    ManualSequence int NOT NULL COMMENT '同一步骤从一开始的独立人工催办序号',
    RecipientCount int NOT NULL COMMENT '本次真实活动待办收件人数',
    IdempotencyKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '实例内固定请求身份',
    RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '规范请求的SHA256摘要',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '服务端UTC受理时间，不修改自动SLA计数',
    CONSTRAINT pk_fn_workflow_manual_reminder PRIMARY KEY (Id),
    CONSTRAINT fk_fn_workflow_manual_reminder_action FOREIGN KEY (Id) REFERENCES fn_workflow_action_record(Id),
    CONSTRAINT fk_fn_workflow_manual_reminder_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_manual_reminder_step FOREIGN KEY (StepId) REFERENCES fn_workflow_step(Id)
) COMMENT='工作流手动催办请求与首次回执；独立于自动SLA提醒预算' ENGINE=InnoDB;
DROP PROCEDURE IF EXISTS fn_workflow_restore_manual_reminders_303;
DELIMITER $$
CREATE PROCEDURE fn_workflow_restore_manual_reminders_303()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_manual_reminder' AND CONSTRAINT_NAME='ck_fn_workflow_manual_reminder_bounds' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_workflow_manual_reminder ADD CONSTRAINT ck_fn_workflow_manual_reminder_bounds CHECK (InstanceRevision>=1 AND StepRevision>=1 AND ManualSequence BETWEEN 1 AND 20 AND RecipientCount BETWEEN 1 AND 20);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_manual_reminder' AND CONSTRAINT_NAME='ck_fn_workflow_manual_reminder_request' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_workflow_manual_reminder ADD CONSTRAINT ck_fn_workflow_manual_reminder_request CHECK (CHAR_LENGTH(TRIM(IdempotencyKey)) BETWEEN 1 AND 128 AND CHAR_LENGTH(RequestHash)=64);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_manual_reminder' AND INDEX_NAME='uq_fn_workflow_manual_reminder_request') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_manual_reminder_request ON fn_workflow_manual_reminder(InstanceId, IdempotencyKey);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_manual_reminder' AND INDEX_NAME='uq_fn_workflow_manual_reminder_sequence') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_manual_reminder_sequence ON fn_workflow_manual_reminder(StepId, ManualSequence);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_manual_reminder' AND INDEX_NAME='ix_fn_workflow_manual_reminder_step_time') THEN
        CREATE INDEX ix_fn_workflow_manual_reminder_step_time ON fn_workflow_manual_reminder(InstanceId, StepId, CreatedAtUtc);
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_restore_manual_reminders_303();
DROP PROCEDURE IF EXISTS fn_workflow_restore_manual_reminders_303;
