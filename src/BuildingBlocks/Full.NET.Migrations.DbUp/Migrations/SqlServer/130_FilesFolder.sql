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
END;

IF COL_LENGTH(N'dbo.fn_files_file', N'Revision') IS NULL
BEGIN
    ALTER TABLE dbo.fn_files_file
        ADD Revision bigint NOT NULL CONSTRAINT DF_fn_files_file_Revision DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.fn_files_file', N'UpdatedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_files_file ADD UpdatedAtUtc datetimeoffset(7) NULL;
END;

IF COL_LENGTH(N'dbo.fn_files_file', N'UpdatedByUserId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_files_file ADD UpdatedByUserId uniqueidentifier NULL;
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
