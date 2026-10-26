-- 309：逐项检查并先建立新约束，失败后重跑不能留下无限制的交接授权窗口。
DROP PROCEDURE IF EXISTS fn_workflow_ensure_todo_delegation;
DELIMITER $$
CREATE PROCEDURE fn_workflow_ensure_todo_delegation()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_authority') THEN
        ALTER TABLE fn_workflow_todo_transfer ADD CONSTRAINT ck_fn_workflow_todo_transfer_authority CHECK (
            Depth BETWEEN 1 AND 32 AND FormRevision>0 AND FromTodoId<>ToTodoId AND FromAssigneeUserId<>ToAssigneeUserId AND
            (AuthorizationKey='workflow.instances.recover' OR
             (AuthorizationKey IN ('workflow.todos.transfer','workflow.todos.delegate') AND ActorUserId=FromAssigneeUserId) OR
             (AuthorizationKey='workflow.todos.recall_delegation' AND ActorUserId=ToAssigneeUserId)));
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_handoff') THEN
        ALTER TABLE fn_workflow_todo_transfer DROP CHECK ck_fn_workflow_todo_transfer_handoff;
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_ensure_todo_delegation();
DROP PROCEDURE IF EXISTS fn_workflow_ensure_todo_delegation;
