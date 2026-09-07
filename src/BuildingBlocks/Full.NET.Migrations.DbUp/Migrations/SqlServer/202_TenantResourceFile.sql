-- 202：租户资源文件由 Files 独立拥有，不复用 Host 文件授权或跨模块外键。
IF OBJECT_ID(N'dbo.fn_files_tenant_resource_file', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_files_tenant_resource_file (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        OwnerModuleKey varchar(64) NOT NULL,
        ResourceId uniqueidentifier NOT NULL,
        OriginalFileName nvarchar(255) NOT NULL,
        ContentType varchar(128) NOT NULL,
        SizeBytes bigint NOT NULL,
        ContentHash varchar(64) NOT NULL,
        ProviderKey varchar(64) NOT NULL,
        StorageKey varchar(256) NOT NULL,
        StatusKey varchar(16) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_files_tenant_resource_file PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_files_tenant_resource_file_StatusKey CHECK (StatusKey IN ('pending', 'ready', 'released'))
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户业务资源文件所有权与上传生命周期',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件 UUID v7 标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'Id';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'TenantId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属租户',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'TenantId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'OwnerModuleKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'资源所属模块稳定键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'OwnerModuleKey';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'ResourceId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属业务资源标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'ResourceId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'OriginalFileName', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'安全下载文件名',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'OriginalFileName';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'ContentType', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容类型',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'ContentType';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'SizeBytes', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际文件字节数',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'SizeBytes';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'ContentHash', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际内容 SHA-256 摘要',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'ContentHash';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'ProviderKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序稳定键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'ProviderKey';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'StorageKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Files 生成的不可外传对象键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'StorageKey';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'StatusKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'上传发布及释放状态',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'StatusKey';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'CreatedByUserId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已授权创建主体',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_tenant_resource_file'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间 UTC',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_tenant_resource_file', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
