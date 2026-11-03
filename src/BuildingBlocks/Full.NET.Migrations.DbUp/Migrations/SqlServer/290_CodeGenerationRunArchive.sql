-- 当次包独立保留，模板删除或 Apply 回滚均不级联删除历史元数据。
IF OBJECT_ID('dbo.fn_codegeneration_run_archive', 'U') IS NULL
    CREATE TABLE dbo.fn_codegeneration_run_archive (
        Id uniqueidentifier NOT NULL,
        SchemaJson nvarchar(max) NOT NULL,
        SchemaSha256 varchar(64) NOT NULL,
        GeneratorVersion varchar(64) NOT NULL,
        ManifestSha256 varchar(64) NOT NULL,
        ZipSha256 varchar(64) NOT NULL,
        FileName nvarchar(256) NOT NULL,
        ByteLength bigint NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_codegeneration_run_archive PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run_archive')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'代码生成不可变下载包元数据及独立保留期限', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run_archive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run_archive')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run_archive'), N'ByteLength', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始 zip 字节数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run_archive', @level2type=N'COLUMN', @level2name=N'ByteLength';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run_archive')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run_archive'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'冻结的到期时刻，到期只删除包', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run_archive', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run_archive')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run_archive'), N'FileName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'鉴权下载的安全文件名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run_archive', @level2type=N'COLUMN', @level2name=N'FileName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run_archive')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run_archive'), N'GeneratorVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'生成器版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run_archive', @level2type=N'COLUMN', @level2name=N'GeneratorVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run_archive')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run_archive'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'与生成运行相同的应用 UUID v7', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run_archive', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run_archive')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run_archive'), N'ManifestSha256', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当次产物或完整工作区清单摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run_archive', @level2type=N'COLUMN', @level2name=N'ManifestSha256';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run_archive')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run_archive'), N'SchemaJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当次规范 Schema，包含冻结关系输入', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run_archive', @level2type=N'COLUMN', @level2name=N'SchemaJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run_archive')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run_archive'), N'SchemaSha256', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规范 Schema 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run_archive', @level2type=N'COLUMN', @level2name=N'SchemaSha256';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run_archive')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run_archive'), N'ZipSha256', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'保存的精确 zip 字节摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run_archive', @level2type=N'COLUMN', @level2name=N'ZipSha256';
GO
