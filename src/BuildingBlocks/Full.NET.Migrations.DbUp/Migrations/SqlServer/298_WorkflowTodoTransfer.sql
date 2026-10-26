-- 298：本人转办保存独立交接事实，不改变原步骤的一票分母。
IF OBJECT_ID(N'dbo.fn_workflow_todo_transfer',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_todo_transfer (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        StepId uniqueidentifier NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SlotId uniqueidentifier NULL,
        OriginTodoId uniqueidentifier NOT NULL,
        FromTodoId uniqueidentifier NOT NULL,
        ToTodoId uniqueidentifier NOT NULL,
        OriginalAssigneeUserId uniqueidentifier NOT NULL,
        FromAssigneeUserId uniqueidentifier NOT NULL,
        ToAssigneeUserId uniqueidentifier NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        Depth int NOT NULL,
        AuthorizationKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        FormRevision bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT pk_fn_workflow_todo_transfer PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_todo_transfer_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance (Id),
        CONSTRAINT fk_fn_workflow_todo_transfer_step FOREIGN KEY (StepId) REFERENCES dbo.fn_workflow_step (Id),
        CONSTRAINT fk_fn_workflow_todo_transfer_slot FOREIGN KEY (SlotId) REFERENCES dbo.fn_workflow_approval_slot (Id),
        CONSTRAINT fk_fn_workflow_todo_transfer_origin FOREIGN KEY (OriginTodoId) REFERENCES dbo.fn_workflow_todo (Id),
        CONSTRAINT fk_fn_workflow_todo_transfer_from FOREIGN KEY (FromTodoId) REFERENCES dbo.fn_workflow_todo (Id),
        CONSTRAINT fk_fn_workflow_todo_transfer_to FOREIGN KEY (ToTodoId) REFERENCES dbo.fn_workflow_todo (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'uq_fn_workflow_todo_transfer_from')
    CREATE UNIQUE INDEX uq_fn_workflow_todo_transfer_from ON dbo.fn_workflow_todo_transfer (FromTodoId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'uq_fn_workflow_todo_transfer_to')
    CREATE UNIQUE INDEX uq_fn_workflow_todo_transfer_to ON dbo.fn_workflow_todo_transfer (ToTodoId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'uq_fn_workflow_todo_transfer_origin_depth')
    CREATE UNIQUE INDEX uq_fn_workflow_todo_transfer_origin_depth ON dbo.fn_workflow_todo_transfer (OriginTodoId, Depth);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ck_fn_workflow_todo_transfer_identity')
    ALTER TABLE dbo.fn_workflow_todo_transfer WITH CHECK ADD CONSTRAINT ck_fn_workflow_todo_transfer_identity CHECK (Depth BETWEEN 1 AND 8 AND FormRevision>0 AND FromTodoId<>ToTodoId AND FromAssigneeUserId<>ToAssigneeUserId AND ActorUserId=FromAssigneeUserId AND AuthorizationKey='workflow.todos.transfer');
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'StepId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定审批步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'StepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信租户作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'SlotId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原审批席位标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'SlotId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'OriginTodoId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'转办链起始待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'OriginTodoId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'FromTodoId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'关闭的原待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'FromTodoId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'ToTodoId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'新激活待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'ToTodoId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'OriginalAssigneeUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'链起始办理人快照', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'OriginalAssigneeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'FromAssigneeUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'交出待办的办理人', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'FromAssigneeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'ToAssigneeUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'接收待办的办理人', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'ToAssigneeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际执行转办的用户', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'Depth','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'有界转办链深度', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'Depth';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'AuthorizationKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'执行时的精确授权依据', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'AuthorizationKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'FormRevision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'新激活实际输入修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'FormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'转办事实创建时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流本人转办交接事实', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer';
GO
