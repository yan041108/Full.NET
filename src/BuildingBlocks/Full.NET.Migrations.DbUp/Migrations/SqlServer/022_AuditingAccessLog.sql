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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'审计访问日志表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端 IP 指纹', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'ClientIpFingerprint';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'耗时(毫秒)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'DurationMs';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'HTTP 方法', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'HttpMethod';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否已认证', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'IsAuthenticated';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'请求路径', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'RequestPath';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'HTTP 状态码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'StatusCode';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'TraceId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_access_log', @level2type=N'COLUMN', @level2name=N'UserId';
    CREATE CLUSTERED INDEX IX_fn_auditing_access_log_OccurredAtUtc_Id
        ON dbo.fn_auditing_access_log(OccurredAtUtc, Id);
    CREATE INDEX IX_fn_auditing_access_log_UserId_OccurredAtUtc
        ON dbo.fn_auditing_access_log(UserId, OccurredAtUtc);
END;
