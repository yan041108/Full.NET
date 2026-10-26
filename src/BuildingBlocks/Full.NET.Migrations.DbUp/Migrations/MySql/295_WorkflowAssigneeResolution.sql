-- 295：只添加可重跑结构，不用当前组织成员回填历史选人依据。
DROP PROCEDURE IF EXISTS fn_workflow_assignee_resolution_recover;
DELIMITER $$
CREATE PROCEDURE fn_workflow_assignee_resolution_recover()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND COLUMN_NAME='AssigneeSnapshotJson') THEN
        ALTER TABLE fn_workflow_step ADD COLUMN AssigneeSnapshotJson longtext NULL COMMENT '本次激活的来源配置、实际成员及解析依据';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND COLUMN_NAME='AssigneeSnapshotHash') THEN
        ALTER TABLE fn_workflow_step ADD COLUMN AssigneeSnapshotHash char(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '冻结选人依据 JSON 的 SHA256 摘要';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND CONSTRAINT_NAME='CK_fn_workflow_step_AssigneeSnapshot') THEN
        ALTER TABLE fn_workflow_step ADD CONSTRAINT CK_fn_workflow_step_AssigneeSnapshot CHECK (
            (AssigneeSnapshotJson IS NULL AND AssigneeSnapshotHash IS NULL) OR
            (AssigneeSnapshotJson IS NOT NULL AND AssigneeSnapshotHash IS NOT NULL AND JSON_VALID(AssigneeSnapshotJson)=1));
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_assignee_resolution_recover();
DROP PROCEDURE IF EXISTS fn_workflow_assignee_resolution_recover;
