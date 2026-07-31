-- 044：Host 代码生成模板目录。
IF OBJECT_ID(N'dbo.fn_codegeneration_template', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_codegeneration_template
    (
        Id uniqueidentifier NOT NULL,
        Name nvarchar(128) NOT NULL,
        Description nvarchar(512) NULL,
        SchemaJson nvarchar(max) NOT NULL,
        SchemaSha256 varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        UpdatedByUserId uniqueidentifier NULL,
        DeletedAtUtc datetimeoffset(7) NULL,
        DeletedByUserId uniqueidentifier NULL,
        IsDeleted bit NOT NULL
            CONSTRAINT DF_fn_codegeneration_template_IsDeleted DEFAULT (0),
        Version bigint NOT NULL
            CONSTRAINT DF_fn_codegeneration_template_Version DEFAULT (1),
        CONSTRAINT PK_fn_codegeneration_template PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_codegeneration_template_SchemaJson
            CHECK (ISJSON(SchemaJson) = 1),
        CONSTRAINT CK_fn_codegeneration_template_SchemaSha256
            CHECK
            (
                LEN(SchemaSha256) = 64
                AND SchemaSha256 NOT LIKE '%[^0-9a-f]%'
            ),
        CONSTRAINT CK_fn_codegeneration_template_Version CHECK (Version > 0),
        CONSTRAINT CK_fn_codegeneration_template_DeleteAudit
            CHECK
            (
                (IsDeleted = 0
                 AND DeletedAtUtc IS NULL
                 AND DeletedByUserId IS NULL)
                OR
                (IsDeleted = 1
                 AND DeletedAtUtc IS NOT NULL
                 AND DeletedByUserId IS NOT NULL)
            )
    );
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
      AND indexObject.name =
          N'IX_fn_codegeneration_template_ActiveUpdatedCreated'
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
          ) <> 4
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
                AND columnObject.name = N'IsDeleted'
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
                AND columnObject.name = N'UpdatedAtUtc'
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
                AND indexColumn.is_descending_key = 1
                AND columnObject.name = N'CreatedAtUtc'
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
                AND indexColumn.key_ordinal = 4
                AND indexColumn.is_descending_key = 0
                AND columnObject.name = N'Id'
          )
      )
)
BEGIN
    DROP INDEX IX_fn_codegeneration_template_ActiveUpdatedCreated
        ON dbo.fn_codegeneration_template;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
      AND name = N'IX_fn_codegeneration_template_ActiveUpdatedCreated'
)
BEGIN
    CREATE INDEX IX_fn_codegeneration_template_ActiveUpdatedCreated
        ON dbo.fn_codegeneration_template
            (IsDeleted, UpdatedAtUtc DESC, CreatedAtUtc DESC, Id);
END;
