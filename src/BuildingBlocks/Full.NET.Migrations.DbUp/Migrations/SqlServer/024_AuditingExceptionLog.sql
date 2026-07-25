-- 024：未处理异常审计汇总表（高写入：非聚集主键 + 时间路径聚集索引）。
IF OBJECT_ID(N'dbo.fn_auditing_exception_log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_auditing_exception_log
    (
        Id uniqueidentifier NOT NULL,
        OccurredAtUtc datetimeoffset(7) NOT NULL,
        ExceptionType varchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Message nvarchar(1024) NOT NULL,
        StackTrace nvarchar(4000) NULL,
        HttpMethod varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
        RequestPath nvarchar(512) NULL,
        UserId uniqueidentifier NULL,
        TenantId uniqueidentifier NULL,
        TraceId varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        ClientIpFingerprint varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        CONSTRAINT PK_fn_auditing_exception_log PRIMARY KEY NONCLUSTERED (Id)
    );
    CREATE CLUSTERED INDEX IX_fn_auditing_exception_log_OccurredAtUtc_Id
        ON dbo.fn_auditing_exception_log(OccurredAtUtc, Id);
    CREATE INDEX IX_fn_auditing_exception_log_ExceptionType_OccurredAtUtc
        ON dbo.fn_auditing_exception_log(ExceptionType, OccurredAtUtc);
END;
