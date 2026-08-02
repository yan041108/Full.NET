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

