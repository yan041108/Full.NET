-- 123：为已办历史分页查询补充完成时间索引。
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_todo')
      AND name = N'IX_fn_workflow_todo_Assignee_Completed')
    CREATE INDEX IX_fn_workflow_todo_Assignee_Completed
        ON dbo.fn_workflow_todo(AssigneeUserId, StatusKey, CompletedAtUtc DESC, Id ASC);
