IF OBJECT_ID(N'dbo.fn_tenant_tenant', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenant_tenant
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        Identifier nvarchar(64) NOT NULL,
        Name nvarchar(128) NOT NULL,
        Domain nvarchar(255) NOT NULL,
        IsActive bit NOT NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_tenant_tenant_Version DEFAULT (1)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户租户表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenant_tenant';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenant_tenant'), N'CreatedAt', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenant_tenant', @level2type=N'COLUMN', @level2name=N'CreatedAt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenant_tenant'), N'Domain', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'域名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenant_tenant', @level2type=N'COLUMN', @level2name=N'Domain';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenant_tenant'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenant_tenant', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenant_tenant'), N'Identifier', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一标识符', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenant_tenant', @level2type=N'COLUMN', @level2name=N'Identifier';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenant_tenant'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenant_tenant', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenant_tenant'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenant_tenant', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenant_tenant'), N'UpdatedAt', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenant_tenant', @level2type=N'COLUMN', @level2name=N'UpdatedAt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenant_tenant'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenant_tenant', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_tenant_tenant_Identifier ON dbo.fn_tenant_tenant(Identifier);
    CREATE UNIQUE INDEX UX_fn_tenant_tenant_Domain ON dbo.fn_tenant_tenant(Domain);
END;

IF OBJECT_ID(N'dbo.fn_outbox_message', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_outbox_message
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        Type nvarchar(256) NOT NULL,
        SchemaVersion int NOT NULL,
        ContentType nvarchar(128) NOT NULL,
        TenantId uniqueidentifier NULL,
        TraceId varchar(32) NULL,
        Payload varbinary(max) NOT NULL,
        OccurredAt datetimeoffset(7) NOT NULL,
        ProcessedAt datetimeoffset(7) NULL,
        NextAttemptAt datetimeoffset(7) NULL,
        Attempts int NOT NULL CONSTRAINT DF_fn_outbox_message_Attempts DEFAULT (0),
        LockId uniqueidentifier NULL,
        LockedUntil datetimeoffset(7) NULL,
        Error nvarchar(2000) NULL
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事务发件箱消息，承载待发布的集成事件', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'Attempts', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'重试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'Attempts';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'ContentType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'ContentType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'Error', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误信息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'Error';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'LockId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'锁标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'LockId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'LockedUntil', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'锁定截止时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'LockedUntil';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'NextAttemptAt', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下次重试时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'NextAttemptAt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'OccurredAt', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'OccurredAt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'Payload', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息正文', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'Payload';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'ProcessedAt', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'处理完成时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'ProcessedAt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'SchemaVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema 版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'TraceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'TraceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'Type', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'Type';
    CREATE INDEX IX_fn_outbox_message_Pending
        ON dbo.fn_outbox_message(ProcessedAt, NextAttemptAt, LockedUntil, OccurredAt);
END;
