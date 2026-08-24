-- 027：Host 作用域文件元数据与本地存储索引。

IF OBJECT_ID(N'dbo.fn_files_file', N'U') IS NULL

BEGIN

    CREATE TABLE dbo.fn_files_file

    (

        Id uniqueidentifier NOT NULL,

        TenantId uniqueidentifier NULL,

        OriginalFileName nvarchar(260) NOT NULL,

        ContentType varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,

        SizeBytes bigint NOT NULL,

        StorageKey varchar(512) COLLATE Latin1_General_100_BIN2 NOT NULL,

        ContentHash char(64) NULL,

        CreatedAtUtc datetimeoffset(7) NOT NULL,

        CreatedByUserId uniqueidentifier NOT NULL,

        DeletedAtUtc datetimeoffset(7) NULL,

        CONSTRAINT PK_fn_files_file PRIMARY KEY CLUSTERED (Id)

    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件文件表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'ContentHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'ContentHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'ContentType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'ContentType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'DeletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'DeletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'OriginalFileName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始文件名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'OriginalFileName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'SizeBytes', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'大小(字节)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'SizeBytes';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'StorageKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'StorageKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'TenantId';

    CREATE INDEX IX_fn_files_file_CreatedAtUtc

        ON dbo.fn_files_file(CreatedAtUtc DESC, Id)

        WHERE DeletedAtUtc IS NULL;

    CREATE UNIQUE INDEX UX_fn_files_file_StorageKey

        ON dbo.fn_files_file(StorageKey)

        WHERE DeletedAtUtc IS NULL;

END;

