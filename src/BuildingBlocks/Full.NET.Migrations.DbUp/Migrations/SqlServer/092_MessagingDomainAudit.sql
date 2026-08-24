-- 092：Messaging 模块 B0 域内同事务审计。

IF OBJECT_ID(N'dbo.fn_messaging_domain_audit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_domain_audit
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ActionKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        EntityId uniqueidentifier NOT NULL,
        Outcome varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActorUserId uniqueidentifier NULL,
        ActorDisplayName nvarchar(128) NULL,
        TraceId varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        DiffSummaryJson nvarchar(max) NULL,
        OccurredAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_messaging_domain_audit PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_messaging_domain_audit_Outcome
            CHECK (Outcome IN ('success', 'failure'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息投递领域审计表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_domain_audit'), N'ActionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'ActionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_domain_audit'), N'ActorDisplayName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者显示名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'ActorDisplayName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_domain_audit'), N'ActorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_domain_audit'), N'DiffSummaryJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'差异摘要(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'DiffSummaryJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_domain_audit'), N'EntityId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实体标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'EntityId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_domain_audit'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_domain_audit'), N'OccurredAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_domain_audit'), N'Outcome', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结果', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'Outcome';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_domain_audit'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_messaging_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_domain_audit'), N'TraceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'TraceId';
    CREATE CLUSTERED INDEX IX_fn_messaging_domain_audit_OccurredAtUtc_Id
        ON dbo.fn_messaging_domain_audit(OccurredAtUtc, Id);
END;
