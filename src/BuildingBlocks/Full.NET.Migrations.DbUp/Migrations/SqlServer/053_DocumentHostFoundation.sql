-- 053：Host 文档库基础表（分类、标签、文档项、版本与标签关联）。
IF OBJECT_ID(N'dbo.fn_document_category', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_category
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ParentId uniqueidentifier NULL,
        Name nvarchar(128) NOT NULL,
        SortOrder int NOT NULL
            CONSTRAINT DF_fn_document_category_SortOrder DEFAULT (0),
        IsDeleted bit NOT NULL
            CONSTRAINT DF_fn_document_category_IsDeleted DEFAULT (0),
        DeletedAtUtc datetimeoffset(7) NULL,
        DeletedByUserId uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_document_category_Version DEFAULT (1),
        CONSTRAINT PK_fn_document_category PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_document_category_DeleteAudit
            CHECK
            (
                (IsDeleted = 0 AND DeletedAtUtc IS NULL AND DeletedByUserId IS NULL)
                OR (IsDeleted = 1 AND DeletedAtUtc IS NOT NULL AND DeletedByUserId IS NOT NULL)
            )
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档分类表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'DeletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'DeletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'DeletedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'DeletedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'IsDeleted', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否已软删除', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'IsDeleted';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'ParentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'父级标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'ParentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'SortOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'SortOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_category')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF OBJECT_ID(N'dbo.fn_document_tag', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_tag
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        Name nvarchar(64) NOT NULL,
        IsDeleted bit NOT NULL
            CONSTRAINT DF_fn_document_tag_IsDeleted DEFAULT (0),
        DeletedAtUtc datetimeoffset(7) NULL,
        DeletedByUserId uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_document_tag_Version DEFAULT (1),
        CONSTRAINT PK_fn_document_tag PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_document_tag_DeleteAudit
            CHECK
            (
                (IsDeleted = 0 AND DeletedAtUtc IS NULL AND DeletedByUserId IS NULL)
                OR (IsDeleted = 1 AND DeletedAtUtc IS NOT NULL AND DeletedByUserId IS NOT NULL)
            )
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标签表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'DeletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'DeletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'DeletedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'DeletedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'IsDeleted', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否已软删除', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'IsDeleted';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF OBJECT_ID(N'dbo.fn_document_item', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_item
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        CategoryId uniqueidentifier NULL,
        CurrentVersionId uniqueidentifier NULL,
        Title nvarchar(256) NOT NULL,
        Description nvarchar(2000) NULL,
        IsDeleted bit NOT NULL
            CONSTRAINT DF_fn_document_item_IsDeleted DEFAULT (0),
        DeletedAtUtc datetimeoffset(7) NULL,
        DeletedByUserId uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        UpdatedByUserId uniqueidentifier NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_document_item_Version DEFAULT (1),
        CONSTRAINT PK_fn_document_item PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_document_item_Category
            FOREIGN KEY (CategoryId) REFERENCES dbo.fn_document_category(Id),
        CONSTRAINT CK_fn_document_item_DeleteAudit
            CHECK
            (
                (IsDeleted = 0 AND DeletedAtUtc IS NULL AND DeletedByUserId IS NULL)
                OR (IsDeleted = 1 AND DeletedAtUtc IS NOT NULL AND DeletedByUserId IS NOT NULL)
            )
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档条目表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'CategoryId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分类标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'CategoryId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'CurrentVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'CurrentVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'DeletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'DeletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'DeletedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'DeletedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'Description', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'Description';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'IsDeleted', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否已软删除', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'IsDeleted';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'Title', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'Title';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'UpdatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'UpdatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF OBJECT_ID(N'dbo.fn_document_version', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_version
    (
        Id uniqueidentifier NOT NULL,
        DocumentItemId uniqueidentifier NOT NULL,
        FileId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        ContentHash char(64) NULL,
        SizeBytes bigint NOT NULL,
        UploadedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_document_version PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_document_version_Item
            FOREIGN KEY (DocumentItemId) REFERENCES dbo.fn_document_item(Id),
        CONSTRAINT CK_fn_document_version_Number CHECK (VersionNumber > 0),
        CONSTRAINT CK_fn_document_version_ContentHash
            CHECK
            (
                ContentHash IS NULL
                OR (LEN(ContentHash) = 64 AND ContentHash NOT LIKE '%[^0-9a-f]%')
            )
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档版本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'ContentHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'ContentHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'DocumentItemId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档项标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'DocumentItemId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'FileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'FileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'SizeBytes', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'大小(字节)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'SizeBytes';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'UploadedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'上传人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'UploadedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'VersionNumber';
    CREATE UNIQUE INDEX UX_fn_document_version_Item_Number
        ON dbo.fn_document_version(DocumentItemId, VersionNumber);
    CREATE INDEX IX_fn_document_version_FileId
        ON dbo.fn_document_version(FileId);
END;

IF OBJECT_ID(N'dbo.fn_document_tag_assignment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_tag_assignment
    (
        DocumentItemId uniqueidentifier NOT NULL,
        TagId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_document_tag_assignment PRIMARY KEY CLUSTERED (DocumentItemId, TagId),
        CONSTRAINT FK_fn_document_tag_assignment_Item
            FOREIGN KEY (DocumentItemId) REFERENCES dbo.fn_document_item(Id),
        CONSTRAINT FK_fn_document_tag_assignment_Tag
            FOREIGN KEY (TagId) REFERENCES dbo.fn_document_tag(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag_assignment')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标签关联表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag_assignment';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag_assignment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag_assignment'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag_assignment', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag_assignment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag_assignment'), N'DocumentItemId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档项标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag_assignment', @level2type=N'COLUMN', @level2name=N'DocumentItemId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag_assignment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag_assignment'), N'TagId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标签标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag_assignment', @level2type=N'COLUMN', @level2name=N'TagId';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_document_category')
      AND indexObject.name = N'UX_fn_document_category_Scope_Parent_Name'
)
    CREATE UNIQUE INDEX UX_fn_document_category_Scope_Parent_Name
        ON dbo.fn_document_category(TenantId, ParentId, Name)
        WHERE IsDeleted = 0;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_document_tag')
      AND indexObject.name = N'UX_fn_document_tag_Scope_Name'
)
    CREATE UNIQUE INDEX UX_fn_document_tag_Scope_Name
        ON dbo.fn_document_tag(TenantId, Name)
        WHERE IsDeleted = 0;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_document_item')
      AND indexObject.name = N'IX_fn_document_item_HostList'
)
    CREATE INDEX IX_fn_document_item_HostList
        ON dbo.fn_document_item(TenantId, IsDeleted, UpdatedAtUtc DESC, Id)
        WHERE IsDeleted = 0;

