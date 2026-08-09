-- 093：对齐 Admin.NET 文档管理模块字段与新子表。
-- 为现有表补齐分类/标签/文档项/版本的业务字段；新建文档权限表与文档分享表。
-- 所有列使用幂等探测，避免重复执行 DDL 时报错。

-- ============================================================
-- 1. fn_document_category 新增：Code/Icon/Color/Description
-- ============================================================
IF COL_LENGTH(N'dbo.fn_document_category', N'Code') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_category
        ADD Code nvarchar(64) NULL;
END;

IF COL_LENGTH(N'dbo.fn_document_category', N'Icon') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_category
        ADD Icon nvarchar(128) NULL;
END;

IF COL_LENGTH(N'dbo.fn_document_category', N'Color') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_category
        ADD Color nvarchar(16) NULL;
END;

IF COL_LENGTH(N'dbo.fn_document_category', N'Description') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_category
        ADD Description nvarchar(500) NULL;
END;

-- ============================================================
-- 2. fn_document_tag 新增：Color/UseCount
-- ============================================================
IF COL_LENGTH(N'dbo.fn_document_tag', N'Color') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD Color nvarchar(16) NULL;
END;

IF COL_LENGTH(N'dbo.fn_document_tag', N'UseCount') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD UseCount int NOT NULL
            CONSTRAINT DF_fn_document_tag_UseCount DEFAULT (0);
END;

-- 2.1 fn_document_tag 补齐：Code/Icon/Description（与 Category 统一字段集）
IF COL_LENGTH(N'dbo.fn_document_tag', N'Code') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD Code nvarchar(64) NULL;
END;

IF COL_LENGTH(N'dbo.fn_document_tag', N'Icon') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD Icon nvarchar(128) NULL;
END;

IF COL_LENGTH(N'dbo.fn_document_tag', N'Description') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD Description nvarchar(500) NULL;
END;

-- ============================================================
-- 3. fn_document_item 新增：DocumentNo/DocumentType/SizeKb/Thumbnail/Status/LastAccessTime/AccessCount/Sort
--    DocumentType 存储枚举 int（1=Word..99=Other），Status 存储（1=Draft 2=Published 3=Archived 4=Deleted）。
-- ============================================================
IF COL_LENGTH(N'dbo.fn_document_item', N'DocumentNo') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD DocumentNo nvarchar(64) NULL;
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'DocumentType') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD DocumentType int NOT NULL
            CONSTRAINT DF_fn_document_item_DocumentType DEFAULT (99);
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'SizeKb') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD SizeKb bigint NOT NULL
            CONSTRAINT DF_fn_document_item_SizeKb DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'Thumbnail') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD Thumbnail nvarchar(512) NULL;
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'Status') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD Status int NOT NULL
            CONSTRAINT DF_fn_document_item_Status DEFAULT (2);
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'LastAccessTime') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD LastAccessTime datetimeoffset(7) NULL;
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'AccessCount') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD AccessCount int NOT NULL
            CONSTRAINT DF_fn_document_item_AccessCount DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'Sort') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD Sort int NOT NULL
            CONSTRAINT DF_fn_document_item_Sort DEFAULT (0);
END;

-- ============================================================
-- 4. fn_document_version 新增：ChangeDescription（变更说明）
-- ============================================================
IF COL_LENGTH(N'dbo.fn_document_version', N'ChangeDescription') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_version
        ADD ChangeDescription nvarchar(500) NULL;
END;

-- ============================================================
-- 5. fn_document_permission 文档权限表
--    支持用户/部门/角色对象，权限类型：View/Download/Edit/Delete/Share
-- ============================================================
IF OBJECT_ID(N'dbo.fn_document_permission', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_permission
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        DocumentId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        PermissionLevel nvarchar(64) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_document_permission PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_document_permission_Document
            FOREIGN KEY (DocumentId) REFERENCES dbo.fn_document_item(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE CLUSTERED INDEX CX_fn_document_permission_Scope_Document_User
        ON dbo.fn_document_permission(TenantId, DocumentId, UserId);
END;

-- ============================================================
-- 6. fn_document_share 文档分享表
-- ============================================================
IF OBJECT_ID(N'dbo.fn_document_share', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_share
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        DocumentId uniqueidentifier NOT NULL,
        ShareCode nvarchar(64) NOT NULL,
        Password nvarchar(512) NULL,
        ExpireTime datetimeoffset(7) NOT NULL,
        MaxAccessCount int NULL,
        AccessCount int NOT NULL
            CONSTRAINT DF_fn_document_share_AccessCount DEFAULT (0),
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_document_share_IsEnabled DEFAULT (1),
        Version bigint NOT NULL
            CONSTRAINT DF_fn_document_share_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_document_share PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_document_share_Document
            FOREIGN KEY (DocumentId) REFERENCES dbo.fn_document_item(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_fn_document_share_Scope_Code
        ON dbo.fn_document_share(TenantId, ShareCode);
    CREATE INDEX IX_fn_document_share_DocumentId
        ON dbo.fn_document_share(DocumentId);
END;
