-- 297：原待办唯一索引允许多个 NULL，未激活席位不预造待办。
DROP PROCEDURE IF EXISTS fn_workflow_sequential_approval_recover;
DELIMITER $$
CREATE PROCEDURE fn_workflow_sequential_approval_recover()
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_slot' AND COLUMN_NAME='TodoId' AND IS_NULLABLE='NO') THEN
        ALTER TABLE fn_workflow_approval_slot MODIFY COLUMN TodoId binary(16) NULL COMMENT '一对一待办标识';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_slot' AND COLUMN_NAME='SequenceNo') THEN
        ALTER TABLE fn_workflow_approval_slot ADD COLUMN SequenceNo int NULL COMMENT '本次激活固定的审批顺序；旧席位为空';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_slot' AND INDEX_NAME='UX_fn_workflow_approval_slot_Sequence') THEN
        CREATE UNIQUE INDEX UX_fn_workflow_approval_slot_Sequence ON fn_workflow_approval_slot(StepId, SequenceNo);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_approval_slot' AND CONSTRAINT_NAME='CK_fn_workflow_approval_slot_Activation') THEN
        ALTER TABLE fn_workflow_approval_slot ADD CONSTRAINT CK_fn_workflow_approval_slot_Activation CHECK (
            (SequenceNo IS NULL OR SequenceNo > 0) AND
            (TodoId IS NOT NULL OR (SequenceNo IS NOT NULL AND (DecisionKey IS NULL OR DecisionKey='cancelled'))));
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_sequential_approval_recover();
DROP PROCEDURE IF EXISTS fn_workflow_sequential_approval_recover;
