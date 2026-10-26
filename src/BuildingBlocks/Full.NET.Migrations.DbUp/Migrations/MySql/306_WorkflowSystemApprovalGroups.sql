-- 新约束先覆盖旧事实；DDL 在记账前中断时可以按结构继续收敛。
DROP PROCEDURE IF EXISTS fn_workflow_restore_system_groups_306;
DELIMITER $$
CREATE PROCEDURE fn_workflow_restore_system_groups_306()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_group' AND CONSTRAINT_NAME='ck_fn_workflow_approval_group_state_306' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_workflow_approval_group ADD CONSTRAINT ck_fn_workflow_approval_group_state_306 CHECK (
            SequenceNo BETWEEN 1 AND 8 AND Revision>0 AND (
                (ModeKey='system' AND SlotCount=0 AND RequiredApprovalCount=0 AND StatusKey='approved' AND CompletedAtUtc IS NOT NULL)
                OR (ModeKey IN ('single','all','any','nOfM','percent','sequential') AND SlotCount BETWEEN 1 AND 20
                    AND RequiredApprovalCount BETWEEN 1 AND SlotCount AND (ModeKey<>'single' OR SlotCount=1)
                    AND (ModeKey NOT IN ('all','sequential') OR RequiredApprovalCount=SlotCount) AND (ModeKey<>'any' OR RequiredApprovalCount=1)
                    AND ((StatusKey='active' AND CompletedAtUtc IS NULL) OR (StatusKey IN ('approved','rejected','cancelled') AND CompletedAtUtc IS NOT NULL)))
            ));
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_group' AND CONSTRAINT_NAME='ck_fn_workflow_approval_group_state' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_workflow_approval_group DROP CHECK ck_fn_workflow_approval_group_state;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND CONSTRAINT_NAME='ck_fn_workflow_step_approval_groups_306' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_workflow_step ADD CONSTRAINT ck_fn_workflow_step_approval_groups_306 CHECK (
            (ApprovalGroupCount IS NULL AND (ApprovalModeKey IS NULL OR ApprovalModeKey<>'group')) OR
            (ApprovalGroupCount IS NOT NULL AND ApprovalModeKey IS NOT NULL AND ApprovalModeKey='group'
                AND ApprovalGroupCount BETWEEN 2 AND 8 AND RequiredApprovalCount IS NOT NULL AND RequiredApprovalCount BETWEEN 1 AND ApprovalGroupCount
                AND ApprovalSlotCount IS NOT NULL AND ApprovalSlotCount BETWEEN 0 AND 20));
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND CONSTRAINT_NAME='ck_fn_workflow_step_approval_groups' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_workflow_step DROP CHECK ck_fn_workflow_step_approval_groups;
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_restore_system_groups_306();
DROP PROCEDURE IF EXISTS fn_workflow_restore_system_groups_306;
