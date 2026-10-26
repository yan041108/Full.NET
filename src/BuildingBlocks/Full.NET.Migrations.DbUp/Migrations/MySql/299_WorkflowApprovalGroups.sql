-- 299：创建表、扩展列、唯一索引及约束可在未记账的部分DDL后恢复。
CREATE TABLE IF NOT EXISTS fn_workflow_approval_group (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    StepId BINARY(16) NOT NULL COMMENT '所属分组审批步骤标识',
    GroupKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '发布版本固定组键',
    GroupName varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT '发布版本固定组名',
    SequenceNo int NOT NULL COMMENT '发布版本固定组顺序',
    ModeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定组内审批模式',
    RequiredApprovalCount int NOT NULL COMMENT '固定组内同意门槛',
    SlotCount int NOT NULL COMMENT '本次实际组内人数',
    StatusKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '组内收敛状态',
    Revision bigint NOT NULL COMMENT '组状态乐观锁修订',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '组激活时间UTC',
    CompletedAtUtc datetime(6) NULL COMMENT '组收敛时间UTC',
    CONSTRAINT pk_fn_workflow_approval_group PRIMARY KEY (Id),
    CONSTRAINT fk_fn_workflow_approval_group_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_approval_group_step FOREIGN KEY (StepId) REFERENCES fn_workflow_step(Id)
) COMMENT='工作流独立审批组事实' ENGINE=InnoDB;
DROP PROCEDURE IF EXISTS fn_workflow_approval_groups_recover;
DELIMITER $$
CREATE PROCEDURE fn_workflow_approval_groups_recover()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND COLUMN_NAME='ApprovalGroupCount') THEN
        ALTER TABLE fn_workflow_step ADD COLUMN ApprovalGroupCount int NULL COMMENT '本次实际审批组数；旧步骤为空';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_slot' AND COLUMN_NAME='GroupId') THEN
        ALTER TABLE fn_workflow_approval_slot ADD COLUMN GroupId binary(16) NULL COMMENT '本次审批组标识；普通席位为空';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_group' AND INDEX_NAME='uq_fn_workflow_approval_group_key') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_approval_group_key ON fn_workflow_approval_group (StepId, GroupKey);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_group' AND INDEX_NAME='uq_fn_workflow_approval_group_sequence') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_approval_group_sequence ON fn_workflow_approval_group (StepId, SequenceNo);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_group' AND INDEX_NAME='ix_fn_workflow_approval_group_instance_status') THEN
        CREATE INDEX ix_fn_workflow_approval_group_instance_status ON fn_workflow_approval_group (InstanceId, StatusKey, StepId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_slot' AND INDEX_NAME='ix_fn_workflow_approval_slot_group_decision') THEN
        CREATE INDEX ix_fn_workflow_approval_slot_group_decision ON fn_workflow_approval_slot(GroupId, DecisionKey, SequenceNo);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_slot' AND CONSTRAINT_NAME='fk_fn_workflow_approval_slot_group') THEN
        ALTER TABLE fn_workflow_approval_slot ADD CONSTRAINT fk_fn_workflow_approval_slot_group FOREIGN KEY(GroupId) REFERENCES fn_workflow_approval_group(Id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_group' AND CONSTRAINT_NAME='ck_fn_workflow_approval_group_state') THEN
        ALTER TABLE fn_workflow_approval_group ADD CONSTRAINT ck_fn_workflow_approval_group_state CHECK (SequenceNo BETWEEN 1 AND 8 AND SlotCount BETWEEN 1 AND 20 AND RequiredApprovalCount BETWEEN 1 AND SlotCount AND Revision>0 AND ModeKey IN ('single','all','any','nOfM','percent','sequential') AND (ModeKey<>'single' OR SlotCount=1) AND (ModeKey NOT IN ('all','sequential') OR RequiredApprovalCount=SlotCount) AND (ModeKey<>'any' OR RequiredApprovalCount=1) AND ((StatusKey='active' AND CompletedAtUtc IS NULL) OR (StatusKey IN ('approved','rejected','cancelled') AND CompletedAtUtc IS NOT NULL)));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND CONSTRAINT_NAME='ck_fn_workflow_step_approval_groups') THEN
        ALTER TABLE fn_workflow_step ADD CONSTRAINT ck_fn_workflow_step_approval_groups CHECK ((ApprovalGroupCount IS NULL AND (ApprovalModeKey IS NULL OR ApprovalModeKey<>'group')) OR (ApprovalGroupCount IS NOT NULL AND ApprovalModeKey IS NOT NULL AND ApprovalModeKey='group' AND ApprovalGroupCount BETWEEN 2 AND 8 AND RequiredApprovalCount IS NOT NULL AND RequiredApprovalCount BETWEEN 1 AND ApprovalGroupCount AND ApprovalSlotCount IS NOT NULL AND ApprovalSlotCount BETWEEN ApprovalGroupCount AND 20));
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_approval_groups_recover();
DROP PROCEDURE IF EXISTS fn_workflow_approval_groups_recover;
