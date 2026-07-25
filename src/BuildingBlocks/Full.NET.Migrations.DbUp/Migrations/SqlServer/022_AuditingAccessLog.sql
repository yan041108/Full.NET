-- 022：HTTP 访问审计汇总表（高写入：非聚集主键 + 时间路径聚集索引）。
IF OBJECT_ID(N'dbo.fn_auditing_access_log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_auditing_access_log
    (
        Id uniqueidentifier NOT NULL,
        OccurredAtUtc datetimeoffset(7) NOT NULL,
        HttpMethod varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RequestPath nvarchar(512) NOT NULL,
        StatusCode int NOT NULL,
        DurationMs int NOT NULL,
        UserId uniqueidentifier NULL,
        TenantId uniqueidentifier NULL,
        TraceId varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        ClientIpFingerprint varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        IsAuthenticated bit NOT NULL
            CONSTRAINT DF_fn_auditing_access_log_IsAuthenticated DEFAULT (0),
        CONSTRAINT PK_fn_auditing_access_log PRIMARY KEY NONCLUSTERED (Id)
    );
    CREATE CLUSTERED INDEX IX_fn_auditing_access_log_OccurredAtUtc_Id
        ON dbo.fn_auditing_access_log(OccurredAtUtc, Id);
    CREATE INDEX IX_fn_auditing_access_log_UserId_OccurredAtUtc
        ON dbo.fn_auditing_access_log(UserId, OccurredAtUtc);
END;
