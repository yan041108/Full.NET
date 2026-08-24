-- 023：已认证写操作审计汇总表（高写入：非聚集主键 + 时间路径聚集索引）。
IF OBJECT_ID(N'dbo.fn_auditing_operation_log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_auditing_operation_log
    (
        Id uniqueidentifier NOT NULL,
        OccurredAtUtc datetimeoffset(7) NOT NULL,
        ActionKey varchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        HttpMethod varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RequestPath nvarchar(512) NOT NULL,
        StatusCode int NOT NULL,
        DurationMs int NOT NULL,
        Succeeded bit NOT NULL,
        UserId uniqueidentifier NULL,
        TenantId uniqueidentifier NULL,
        TraceId varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        ClientIpFingerprint varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        PermissionCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        CONSTRAINT PK_fn_auditing_operation_log PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'审计操作日志表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'ActionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'ActionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'ClientIpFingerprint', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端 IP 指纹', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'ClientIpFingerprint';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'DurationMs', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'耗时(毫秒)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'DurationMs';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'HttpMethod', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'HTTP 方法', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'HttpMethod';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'OccurredAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'PermissionCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权限码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'PermissionCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'RequestPath', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'请求路径', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'RequestPath';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'StatusCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'HTTP 状态码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'StatusCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'Succeeded', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否成功', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'Succeeded';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'TraceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'TraceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'UserId';
    CREATE CLUSTERED INDEX IX_fn_auditing_operation_log_OccurredAtUtc_Id
        ON dbo.fn_auditing_operation_log(OccurredAtUtc, Id);
    CREATE INDEX IX_fn_auditing_operation_log_UserId_OccurredAtUtc
        ON dbo.fn_auditing_operation_log(UserId, OccurredAtUtc);
    CREATE INDEX IX_fn_auditing_operation_log_ActionKey_OccurredAtUtc
        ON dbo.fn_auditing_operation_log(ActionKey, OccurredAtUtc);
END;
