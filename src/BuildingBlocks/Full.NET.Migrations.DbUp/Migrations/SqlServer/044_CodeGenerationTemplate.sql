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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'代码生成模板表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'DeletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'DeletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'DeletedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'DeletedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'Description', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'Description';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'IsDeleted', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否已软删除', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'IsDeleted';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'SchemaJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'SchemaJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'SchemaSha256', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema SHA256', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'SchemaSha256';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'UpdatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'UpdatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_template'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_template', @level2type=N'COLUMN', @level2name=N'Version';
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
