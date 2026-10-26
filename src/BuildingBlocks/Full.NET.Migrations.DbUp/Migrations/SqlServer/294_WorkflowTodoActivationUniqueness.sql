-- 294：待办是人工激活历史；同一席位恢复时允许新待办，活动办理身份仍唯一。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_todo') AND name = N'UX_fn_workflow_todo_Active_Assignee')
BEGIN
    CREATE UNIQUE INDEX UX_fn_workflow_todo_Active_Assignee
        ON dbo.fn_workflow_todo(StepId, AssigneeUserId) WHERE StatusKey = 'active';
END;
-- 先建立活动约束，再移除历史唯一约束，半完成重跑不会出现无保护窗口。
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_todo') AND name = N'UX_fn_workflow_todo_Step_Assignee')
    DROP INDEX UX_fn_workflow_todo_Step_Assignee ON dbo.fn_workflow_todo;
