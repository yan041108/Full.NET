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
    CREATE CLUSTERED INDEX IX_fn_messaging_domain_audit_OccurredAtUtc_Id
        ON dbo.fn_messaging_domain_audit(OccurredAtUtc, Id);
END;
