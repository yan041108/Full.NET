-- 294：MySQL 以生成的活动占用键表达过滤唯一，审批席位的一人一票约束保持独立。
DROP PROCEDURE IF EXISTS fn_workflow_todo_activation_recover;
DELIMITER $$
CREATE PROCEDURE fn_workflow_todo_activation_recover()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo' AND COLUMN_NAME='ActiveAssigneeUserId') THEN
        ALTER TABLE fn_workflow_todo ADD COLUMN ActiveAssigneeUserId BINARY(16)
            GENERATED ALWAYS AS (CASE WHEN StatusKey = 'active' THEN AssigneeUserId ELSE NULL END) STORED
            COMMENT '仅活动待办占用的办理人标识，历史待办为空';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo' AND INDEX_NAME='UX_fn_workflow_todo_Active_Assignee') THEN
        CREATE UNIQUE INDEX UX_fn_workflow_todo_Active_Assignee ON fn_workflow_todo(StepId, ActiveAssigneeUserId);
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo' AND INDEX_NAME='UX_fn_workflow_todo_Step_Assignee') THEN
        ALTER TABLE fn_workflow_todo DROP INDEX UX_fn_workflow_todo_Step_Assignee;
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_todo_activation_recover();
DROP PROCEDURE IF EXISTS fn_workflow_todo_activation_recover;
