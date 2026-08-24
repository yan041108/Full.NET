-- 045：Host 代码生成运行摘要。
IF OBJECT_ID(N'dbo.fn_codegeneration_run', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_codegeneration_run
    (
        Id uniqueidentifier NOT NULL,
        TemplateId uniqueidentifier NULL,
        TemplateVersion bigint NULL,
        OperationKind varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Status varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ModuleKey varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        EntityKey varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        SchemaSha256 varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        ArtifactCount int NOT NULL,
        ManifestSha256 varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        ErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        RequestedByUserId uniqueidentifier NOT NULL,
        StartedAtUtc datetimeoffset(7) NOT NULL,
        FinishedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_codegeneration_run PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_codegeneration_run_Operation
            CHECK (OperationKind = 'preview'),
        CONSTRAINT CK_fn_codegeneration_run_Status
            CHECK (Status IN ('succeeded', 'failed')),
        CONSTRAINT CK_fn_codegeneration_run_Template
            CHECK
            (
                (TemplateId IS NULL AND TemplateVersion IS NULL)
                OR
                (TemplateId IS NOT NULL AND TemplateVersion > 0)
            ),
        CONSTRAINT CK_fn_codegeneration_run_Outcome
            CHECK
            (
                (
                    Status = 'succeeded'
                    AND ModuleKey IS NOT NULL
                    AND EntityKey IS NOT NULL
                    AND SchemaSha256 IS NOT NULL
                    AND LEN(SchemaSha256) = 64
                    AND SchemaSha256 NOT LIKE '%[^0-9a-f]%'
                    AND ArtifactCount > 0
                    AND ManifestSha256 IS NOT NULL
                    AND LEN(ManifestSha256) = 64
                    AND ManifestSha256 NOT LIKE '%[^0-9a-f]%'
                    AND ErrorCode IS NULL
                )
                OR
                (
                    Status = 'failed'
                    AND ModuleKey IS NULL
                    AND EntityKey IS NULL
                    AND SchemaSha256 IS NULL
                    AND ArtifactCount = 0
                    AND ManifestSha256 IS NULL
                    AND ErrorCode IS NOT NULL
                    AND LEN(ErrorCode) > 0
                )
            ),
        CONSTRAINT CK_fn_codegeneration_run_Time
            CHECK (FinishedAtUtc >= StartedAtUtc)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'代码生成运行表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'ArtifactCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'产物数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'ArtifactCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'EntityKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实体键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'EntityKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'ErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'FinishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结束时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'FinishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'ManifestSha256', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'清单 SHA256', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'ManifestSha256';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'ModuleKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模块键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'ModuleKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'OperationKind', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'OperationKind';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'RequestedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'请求人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'RequestedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'SchemaSha256', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema SHA256', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'SchemaSha256';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'StartedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'TemplateId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模板标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'TemplateId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'TemplateVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模板版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'TemplateVersion';
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
      AND indexObject.name = N'IX_fn_codegeneration_run_StatusStarted'
      AND
      (
          indexObject.is_unique = 1
          OR indexObject.has_filter = 1
          OR indexObject.is_disabled = 1
          OR
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal > 0
          ) <> 3
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
                AND indexColumn.is_descending_key = 0
                AND columnObject.name = N'Status'
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
                AND indexColumn.is_descending_key = 1
                AND columnObject.name = N'StartedAtUtc'
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
                AND indexColumn.key_ordinal = 3
                AND indexColumn.is_descending_key = 0
                AND columnObject.name = N'Id'
          )
      )
)
BEGIN
    DROP INDEX IX_fn_codegeneration_run_StatusStarted
        ON dbo.fn_codegeneration_run;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
      AND name = N'IX_fn_codegeneration_run_StatusStarted'
)
BEGIN
    CREATE INDEX IX_fn_codegeneration_run_StatusStarted
        ON dbo.fn_codegeneration_run (Status, StartedAtUtc DESC, Id);
END;
