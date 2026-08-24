-- 043：出站调用审计汇总表（高写入：非聚集主键 + 时间路径聚集索引）。

IF OBJECT_ID(N'dbo.fn_auditing_outbound_call', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_auditing_outbound_call
    (
        Id uniqueidentifier NOT NULL,
        OccurredAtUtc datetimeoffset(7) NOT NULL,
        ProviderKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        OperationKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DestinationHostCategory varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        StatusCode int NOT NULL,
        Succeeded bit NOT NULL
            CONSTRAINT DF_fn_auditing_outbound_call_Succeeded DEFAULT (0),
        DurationMs int NOT NULL,
        RetryCount int NOT NULL
            CONSTRAINT DF_fn_auditing_outbound_call_RetryCount DEFAULT (0),
        TraceId varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        SafeErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        TenantId uniqueidentifier NULL,
        UserId uniqueidentifier NULL,
        CONSTRAINT PK_fn_auditing_outbound_call PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_auditing_outbound_call_StatusCode
            CHECK (StatusCode BETWEEN 0 AND 999),
        CONSTRAINT CK_fn_auditing_outbound_call_DurationMs
            CHECK (DurationMs >= 0),
        CONSTRAINT CK_fn_auditing_outbound_call_RetryCount
            CHECK (RetryCount >= 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'审计出站调用表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'DestinationHostCategory', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目标主机类别', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'DestinationHostCategory';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'DurationMs', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'耗时(毫秒)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'DurationMs';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'OccurredAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'OperationKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'OperationKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'ProviderKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'ProviderKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'RetryCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'重试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'RetryCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'SafeErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'安全错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'SafeErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'StatusCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'HTTP 状态码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'StatusCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'Succeeded', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否成功', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'Succeeded';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'TraceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'TraceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_outbound_call'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_auditing_outbound_call', @level2type=N'COLUMN', @level2name=N'UserId';
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
      AND indexObject.name = N'IX_fn_auditing_outbound_call_OccurredAtUtc_Id'
      AND
      (
          indexObject.is_unique = 1
          OR indexObject.type <> 1
          OR indexObject.is_disabled = 1
          OR
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal > 0
          ) <> 2
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 1
                AND columnObject.name = N'OccurredAtUtc'
          )
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 2
                AND columnObject.name = N'Id'
          )
      )
)
BEGIN
    DROP INDEX IX_fn_auditing_outbound_call_OccurredAtUtc_Id
        ON dbo.fn_auditing_outbound_call;
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
      AND indexObject.name = N'IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc'
      AND
      (
          indexObject.is_unique = 1
          OR indexObject.type <> 2
          OR indexObject.is_disabled = 1
          OR
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal > 0
          ) <> 2
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 1
                AND columnObject.name = N'ProviderKey'
          )
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 2
                AND columnObject.name = N'OccurredAtUtc'
          )
      )
)
BEGIN
    DROP INDEX IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc
        ON dbo.fn_auditing_outbound_call;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
      AND name = N'IX_fn_auditing_outbound_call_OccurredAtUtc_Id'
)
BEGIN
    CREATE CLUSTERED INDEX IX_fn_auditing_outbound_call_OccurredAtUtc_Id
        ON dbo.fn_auditing_outbound_call(OccurredAtUtc, Id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_auditing_outbound_call')
      AND name = N'IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc'
)
BEGIN
    CREATE INDEX IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc
        ON dbo.fn_auditing_outbound_call(ProviderKey, OccurredAtUtc);
END;
