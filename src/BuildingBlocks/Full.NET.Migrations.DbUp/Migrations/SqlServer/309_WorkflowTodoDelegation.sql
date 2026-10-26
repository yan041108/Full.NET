-- 309：先验证新的精确代理/收回授权约束，再移除旧约束；任一批中断均可安全重跑。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ck_fn_workflow_todo_transfer_authority')
    ALTER TABLE dbo.fn_workflow_todo_transfer WITH CHECK ADD CONSTRAINT ck_fn_workflow_todo_transfer_authority CHECK (
        Depth BETWEEN 1 AND 32 AND FormRevision>0 AND FromTodoId<>ToTodoId AND FromAssigneeUserId<>ToAssigneeUserId AND
        (AuthorizationKey='workflow.instances.recover' OR
         (AuthorizationKey IN ('workflow.todos.transfer','workflow.todos.delegate') AND ActorUserId=FromAssigneeUserId) OR
         (AuthorizationKey='workflow.todos.recall_delegation' AND ActorUserId=ToAssigneeUserId)));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ck_fn_workflow_todo_transfer_handoff')
    ALTER TABLE dbo.fn_workflow_todo_transfer DROP CONSTRAINT ck_fn_workflow_todo_transfer_handoff;
GO
