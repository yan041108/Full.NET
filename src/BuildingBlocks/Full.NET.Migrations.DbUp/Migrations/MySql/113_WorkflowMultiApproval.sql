-- 多人审批采用追加式席位事实；步骤快照列保持可空，以支持旧 API 滚动升级和存量单人步骤。
DROP PROCEDURE IF EXISTS fullnet_migrate_113_workflow_multi_approval;
DELIMITER $$
CREATE PROCEDURE fullnet_migrate_113_workflow_multi_approval()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_step' AND COLUMN_NAME = 'ApprovalModeKey') THEN
        ALTER TABLE fn_workflow_step ADD COLUMN ApprovalModeKey varchar(16) NULL COMMENT '审批模式键';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_step' AND COLUMN_NAME = 'RequiredApprovalCount') THEN
        ALTER TABLE fn_workflow_step ADD COLUMN RequiredApprovalCount int NULL COMMENT '法定同意票数';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_step' AND COLUMN_NAME = 'ApprovalSlotCount') THEN
        ALTER TABLE fn_workflow_step ADD COLUMN ApprovalSlotCount int NULL COMMENT '审批席位总数';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_action_record' AND COLUMN_NAME = 'ResultStatusKey') THEN
        ALTER TABLE fn_workflow_action_record ADD COLUMN ResultStatusKey varchar(16) NULL COMMENT '动作确定性结果状态键';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_action_record' AND COLUMN_NAME = 'ResultTodoId') THEN
        ALTER TABLE fn_workflow_action_record ADD COLUMN ResultTodoId binary(16) NULL COMMENT '动作确定性结果待办标识';
    END IF;

    CREATE TABLE IF NOT EXISTS fn_workflow_approval_slot (
        Id binary(16) NOT NULL COMMENT '逻辑主键',
        InstanceId binary(16) NOT NULL COMMENT '流程实例标识',
        StepId binary(16) NOT NULL COMMENT '审批步骤标识',
        TodoId binary(16) NOT NULL COMMENT '一对一待办标识',
        AssigneeUserId binary(16) NOT NULL COMMENT '席位办理人标识',
        DecisionKey varchar(16) NULL COMMENT '审批决定机器键',
        Revision bigint NOT NULL DEFAULT 1 COMMENT '席位修订号',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '席位创建时间(UTC)',
        DecidedAtUtc datetime(6) NULL COMMENT '决定提交时间(UTC)',
        CONSTRAINT PK_fn_workflow_approval_slot PRIMARY KEY (Id),
        CONSTRAINT FK_fn_workflow_approval_slot_Instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
        CONSTRAINT FK_fn_workflow_approval_slot_Step FOREIGN KEY (StepId) REFERENCES fn_workflow_step(Id),
        CONSTRAINT FK_fn_workflow_approval_slot_Todo FOREIGN KEY (TodoId) REFERENCES fn_workflow_todo(Id),
        CONSTRAINT UQ_fn_workflow_approval_slot_Step_Assignee UNIQUE (StepId, AssigneeUserId),
        CONSTRAINT UQ_fn_workflow_approval_slot_Todo UNIQUE (TodoId),
        CONSTRAINT CK_fn_workflow_approval_slot_Revision CHECK (Revision > 0),
        CONSTRAINT CK_fn_workflow_approval_slot_Decision CHECK (DecisionKey IS NULL OR DecisionKey IN ('approve', 'reject', 'cancelled'))
    ) COMMENT='工作流多人审批席位表';

    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_approval_slot' AND INDEX_NAME = 'IX_fn_workflow_approval_slot_Step_Decision') THEN
        ALTER TABLE fn_workflow_approval_slot ADD INDEX IX_fn_workflow_approval_slot_Step_Decision (StepId, DecisionKey, Id);
    END IF;
END$$
DELIMITER ;
CALL fullnet_migrate_113_workflow_multi_approval();
DROP PROCEDURE IF EXISTS fullnet_migrate_113_workflow_multi_approval;
