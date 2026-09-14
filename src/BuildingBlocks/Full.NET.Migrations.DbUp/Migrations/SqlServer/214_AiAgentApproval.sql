-- 214：Agent 审批、委托与工具审计扩展；expand-only，支持幂等重跑。
IF OBJECT_ID(N'dbo.fn_ai_agent_approval', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_agent_approval (
        Id uniqueidentifier NOT NULL,
        ScopeKey varchar(32) NOT NULL,
        TenantId uniqueidentifier NULL,
        RunId uniqueidentifier NOT NULL,
        OperationId uniqueidentifier NOT NULL,
        SessionId uniqueidentifier NOT NULL,
        ToolName varchar(128) NOT NULL,
        ToolVersion int NOT NULL,
        ArgumentsHash varchar(64) NOT NULL,
        ArgumentsProtected nvarchar(max) NOT NULL,
        PolicyVersion int NOT NULL,
        PresentationJson nvarchar(max) NOT NULL,
        RequestedBy uniqueidentifier NOT NULL,
        ApproverId uniqueidentifier NULL,
        DecisionKey varchar(16) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        ConsumedAtUtc datetimeoffset(7) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_ai_agent_approval_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_agent_approval PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_ai_agent_approval_DecisionKey CHECK (DecisionKey IN (N'pending', N'approved', N'denied'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能agent approval表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'ApproverId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Approver标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'ApproverId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'ArgumentsHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Arguments Hash', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'ArgumentsHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'ArgumentsProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Arguments Protected', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'ArgumentsProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'ConsumedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'ConsumedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'DecisionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'决策键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'DecisionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'OperationId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Operation标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'OperationId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'PolicyVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Policy Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'PolicyVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'PresentationJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Presentation(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'PresentationJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'RequestedBy', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Requested By', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'RequestedBy';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'RunId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'运行标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'RunId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'SessionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'会话标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'SessionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'ToolName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工具名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'ToolName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'ToolVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Tool Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'ToolVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_approval'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1 FROM sys.extended_properties
        WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval') AND minor_id = 0 AND name = N'MS_Description')
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能 Agent 审批表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval';
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_approval') AND name = N'UX_fn_ai_agent_approval_Operation')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_ai_agent_approval_Operation ON dbo.fn_ai_agent_approval (OperationId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_approval') AND name = N'IX_fn_ai_agent_approval_Run')
    CREATE NONCLUSTERED INDEX IX_fn_ai_agent_approval_Run ON dbo.fn_ai_agent_approval (RunId, CreatedAtUtc);

IF OBJECT_ID(N'dbo.fn_ai_agent_delegation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_agent_delegation (
        Id uniqueidentifier NOT NULL,
        ScopeKey varchar(32) NOT NULL,
        TenantId uniqueidentifier NULL,
        GrantorUserId uniqueidentifier NOT NULL,
        GranteeUserId uniqueidentifier NOT NULL,
        ToolName varchar(128) NULL,
        PermissionCode varchar(128) NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        RevokedAtUtc datetimeoffset(7) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_ai_agent_delegation_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_agent_delegation PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能agent delegation表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'GranteeUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Grantee User标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'GranteeUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'GrantorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Grantor User标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'GrantorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'PermissionCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权限码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'PermissionCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'RevokedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'撤销时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'RevokedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'ToolName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工具名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'ToolName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_delegation'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1 FROM sys.extended_properties
        WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation') AND minor_id = 0 AND name = N'MS_Description')
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能 Agent 委托表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation';
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation') AND name = N'IX_fn_ai_agent_delegation_Grantee')
    CREATE NONCLUSTERED INDEX IX_fn_ai_agent_delegation_Grantee ON dbo.fn_ai_agent_delegation (GranteeUserId, ExpiresAtUtc);

IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'RunId') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD RunId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'RunId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'运行标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'RunId';
IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'StepId') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD StepId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'StepId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'StepId';
IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'ArgumentsHash') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD ArgumentsHash varchar(64) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'ArgumentsHash', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Arguments Hash', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'ArgumentsHash';
IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'ApprovalId') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD ApprovalId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'ApprovalId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Approval标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'ApprovalId';
IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'ReceiptId') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD ReceiptId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'ReceiptId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Receipt标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'ReceiptId';
