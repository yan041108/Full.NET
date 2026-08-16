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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息投递领域审计表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'ActionKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者显示名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'ActorDisplayName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'差异摘要(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'DiffSummaryJson';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实体标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'EntityId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结果', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'Outcome';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_domain_audit', @level2type=N'COLUMN', @level2name=N'TraceId';
    CREATE CLUSTERED INDEX IX_fn_messaging_domain_audit_OccurredAtUtc_Id
        ON dbo.fn_messaging_domain_audit(OccurredAtUtc, Id);
END;
