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
