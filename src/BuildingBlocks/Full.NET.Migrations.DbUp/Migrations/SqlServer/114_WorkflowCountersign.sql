-- 114：为工作流待办加签引入链与有序加签项事实表；前加签会挂起原办理人待办，后加签在原办理人同意后依次激活。
IF OBJECT_ID(N'dbo.fn_workflow_countersign_chain', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_countersign_chain
    (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        StepId uniqueidentifier NOT NULL,
        OriginTodoId uniqueidentifier NOT NULL,
        DirectionKey varchar(16) NOT NULL,
        StatusKey varchar(16) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_workflow_countersign_chain PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_countersign_chain_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT FK_fn_workflow_countersign_chain_Step FOREIGN KEY (StepId) REFERENCES dbo.fn_workflow_step(Id),
        CONSTRAINT FK_fn_workflow_countersign_chain_OriginTodo FOREIGN KEY (OriginTodoId) REFERENCES dbo.fn_workflow_todo(Id),
        CONSTRAINT CK_fn_workflow_countersign_chain_Direction CHECK (DirectionKey IN ('before', 'after')),
        CONSTRAINT CK_fn_workflow_countersign_chain_Status CHECK (StatusKey IN ('active', 'completed', 'cancelled'))
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流加签链表',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_chain';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_chain', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_chain', @level2type=N'COLUMN', @level2name=N'InstanceId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程步骤标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_chain', @level2type=N'COLUMN', @level2name=N'StepId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发起加签的原待办标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_chain', @level2type=N'COLUMN', @level2name=N'OriginTodoId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'加签方向键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_chain', @level2type=N'COLUMN', @level2name=N'DirectionKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'加签链状态键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_chain', @level2type=N'COLUMN', @level2name=N'StatusKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发起加签的用户标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_chain', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_chain', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
END;

IF OBJECT_ID(N'dbo.fn_workflow_countersign_item', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_countersign_item
    (
        Id uniqueidentifier NOT NULL,
        ChainId uniqueidentifier NOT NULL,
        SequenceNo int NOT NULL,
        AssigneeUserId uniqueidentifier NOT NULL,
        TodoId uniqueidentifier NULL,
        StatusKey varchar(16) NOT NULL,
        CONSTRAINT PK_fn_workflow_countersign_item PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_countersign_item_Chain FOREIGN KEY (ChainId) REFERENCES dbo.fn_workflow_countersign_chain(Id),
        CONSTRAINT FK_fn_workflow_countersign_item_Todo FOREIGN KEY (TodoId) REFERENCES dbo.fn_workflow_todo(Id),
        CONSTRAINT UQ_fn_workflow_countersign_item_Chain_Sequence UNIQUE (ChainId, SequenceNo),
        CONSTRAINT CK_fn_workflow_countersign_item_Sequence CHECK (SequenceNo > 0),
        CONSTRAINT CK_fn_workflow_countersign_item_Status CHECK (StatusKey IN ('pending', 'active', 'completed', 'cancelled'))
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流加签项表',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_item';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_item', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属加签链标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_item', @level2type=N'COLUMN', @level2name=N'ChainId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'加签顺序号',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_item', @level2type=N'COLUMN', @level2name=N'SequenceNo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'加签办理人标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_item', @level2type=N'COLUMN', @level2name=N'AssigneeUserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'关联待办标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_item', @level2type=N'COLUMN', @level2name=N'TodoId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'加签项状态键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_countersign_item', @level2type=N'COLUMN', @level2name=N'StatusKey';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_countersign_chain')
      AND name = N'UX_fn_workflow_countersign_chain_ActiveOrigin')
    CREATE UNIQUE INDEX UX_fn_workflow_countersign_chain_ActiveOrigin
        ON dbo.fn_workflow_countersign_chain(OriginTodoId)
        WHERE StatusKey = 'active';

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_countersign_item')
      AND name = N'UX_fn_workflow_countersign_item_Todo')
    CREATE UNIQUE INDEX UX_fn_workflow_countersign_item_Todo
        ON dbo.fn_workflow_countersign_item(TodoId)
        WHERE TodoId IS NOT NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_countersign_item')
      AND name = N'IX_fn_workflow_countersign_item_Chain_Status')
    CREATE INDEX IX_fn_workflow_countersign_item_Chain_Status
        ON dbo.fn_workflow_countersign_item(ChainId, StatusKey, SequenceNo);
