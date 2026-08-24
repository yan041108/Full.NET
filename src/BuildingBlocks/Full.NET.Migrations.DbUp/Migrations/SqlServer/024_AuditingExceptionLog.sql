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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'审计异常日志表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'ClientIpFingerprint', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端 IP 指纹', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'ClientIpFingerprint';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'ExceptionType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'异常类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'ExceptionType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'HttpMethod', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'HTTP 方法', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'HttpMethod';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'Message', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息文本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'Message';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'OccurredAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'RequestPath', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'请求路径', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'RequestPath';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'StackTrace', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'堆栈跟踪', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'StackTrace';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'TraceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'TraceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_exception_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_exception_log'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_exception_log', @level2type=N'COLUMN', @level2name=N'UserId';
    CREATE CLUSTERED INDEX IX_fn_auditing_exception_log_OccurredAtUtc_Id
        ON dbo.fn_auditing_exception_log(OccurredAtUtc, Id);
    CREATE INDEX IX_fn_auditing_exception_log_ExceptionType_OccurredAtUtc
        ON dbo.fn_auditing_exception_log(ExceptionType, OccurredAtUtc);
END;
