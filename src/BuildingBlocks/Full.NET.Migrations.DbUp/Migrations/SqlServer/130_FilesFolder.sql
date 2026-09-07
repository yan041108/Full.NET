-- 130：Host 虚拟目录与文件元数据修订号；目录仅表达逻辑归属，不映射磁盘路径。

IF OBJECT_ID(N'dbo.fn_files_folder', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_files_folder
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ParentId uniqueidentifier NULL,
        Name nvarchar(128) NOT NULL,
        DisplayOrder int NOT NULL CONSTRAINT DF_fn_files_folder_DisplayOrder DEFAULT (0),
        Revision bigint NOT NULL CONSTRAINT DF_fn_files_folder_Revision DEFAULT (0),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        UpdatedByUserId uniqueidentifier NULL,
        DeletedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_files_folder PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件虚拟目录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'DeletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'DeletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'DisplayOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'DisplayOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'ParentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'父级标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'ParentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'Revision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'Revision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_folder')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_folder'), N'UpdatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_folder', @level2type=N'COLUMN', @level2name=N'UpdatedByUserId';

    CREATE INDEX IX_fn_files_folder_ParentId
        ON dbo.fn_files_folder(ParentId, DisplayOrder, Name)
        WHERE DeletedAtUtc IS NULL;

    CREATE UNIQUE INDEX UX_fn_files_folder_ParentId_Name
        ON dbo.fn_files_folder(ParentId, Name)
        WHERE DeletedAtUtc IS NULL;
END;

IF COL_LENGTH(N'dbo.fn_files_file', N'FolderId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_files_file ADD FolderId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_files_file')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'FolderId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目录标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'FolderId';
END;

IF COL_LENGTH(N'dbo.fn_files_file', N'Revision') IS NULL
BEGIN
    ALTER TABLE dbo.fn_files_file
        ADD Revision bigint NOT NULL CONSTRAINT DF_fn_files_file_Revision DEFAULT (0);

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_files_file')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'Revision', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'Revision';
END;

IF COL_LENGTH(N'dbo.fn_files_file', N'UpdatedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_files_file ADD UpdatedAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_files_file')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'UpdatedAtUtc', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
END;

IF COL_LENGTH(N'dbo.fn_files_file', N'UpdatedByUserId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_files_file ADD UpdatedByUserId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_files_file')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'UpdatedByUserId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'UpdatedByUserId';
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_files_file')
      AND name = N'IX_fn_files_file_FolderId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_fn_files_file_FolderId
        ON dbo.fn_files_file(FolderId, CreatedAtUtc DESC, Id)
        WHERE DeletedAtUtc IS NULL AND StorageState = 'ready';
END;
