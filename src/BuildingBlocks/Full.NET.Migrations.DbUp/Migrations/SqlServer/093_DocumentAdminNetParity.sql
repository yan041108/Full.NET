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
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_category')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'Code', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'Code';
END;

IF COL_LENGTH(N'dbo.fn_document_category', N'Icon') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_category
        ADD Icon nvarchar(128) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_category')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'Icon', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'图标', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'Icon';
END;

IF COL_LENGTH(N'dbo.fn_document_category', N'Color') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_category
        ADD Color nvarchar(16) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_category')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'Color', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'颜色', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'Color';
END;

IF COL_LENGTH(N'dbo.fn_document_category', N'Description') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_category
        ADD Description nvarchar(500) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_category')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_category'), N'Description', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category', @level2type=N'COLUMN', @level2name=N'Description';
END;

-- ============================================================
-- 2. fn_document_tag 新增：Color/UseCount
-- ============================================================
IF COL_LENGTH(N'dbo.fn_document_tag', N'Color') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD Color nvarchar(16) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'Color', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'颜色', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'Color';
END;

IF COL_LENGTH(N'dbo.fn_document_tag', N'UseCount') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD UseCount int NOT NULL
            CONSTRAINT DF_fn_document_tag_UseCount DEFAULT (0);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'UseCount', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'使用次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'UseCount';
END;

-- 2.1 fn_document_tag 补齐：Code/Icon/Description（与 Category 统一字段集）
IF COL_LENGTH(N'dbo.fn_document_tag', N'Code') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD Code nvarchar(64) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'Code', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'Code';
END;

IF COL_LENGTH(N'dbo.fn_document_tag', N'Icon') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD Icon nvarchar(128) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'Icon', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'图标', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'Icon';
END;

IF COL_LENGTH(N'dbo.fn_document_tag', N'Description') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD Description nvarchar(500) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'Description', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'Description';
END;

-- ============================================================
-- 3. fn_document_item 新增：DocumentNo/DocumentType/SizeKb/Thumbnail/Status/LastAccessTime/AccessCount/Sort
--    DocumentType 存储枚举 int（1=Word..99=Other），Status 存储（1=Draft 2=Published 3=Archived 4=Deleted）。
-- ============================================================
IF COL_LENGTH(N'dbo.fn_document_item', N'DocumentNo') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD DocumentNo nvarchar(64) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_item')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'DocumentNo', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档编号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'DocumentNo';
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'DocumentType') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD DocumentType int NOT NULL
            CONSTRAINT DF_fn_document_item_DocumentType DEFAULT (99);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_item')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'DocumentType', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'DocumentType';
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'SizeKb') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD SizeKb bigint NOT NULL
            CONSTRAINT DF_fn_document_item_SizeKb DEFAULT (0);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_item')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'SizeKb', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'大小(KB)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'SizeKb';
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'Thumbnail') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD Thumbnail nvarchar(512) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_item')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'Thumbnail', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'缩略图', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'Thumbnail';
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'Status') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD Status int NOT NULL
            CONSTRAINT DF_fn_document_item_Status DEFAULT (2);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_item')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'Status', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'Status';
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'LastAccessTime') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD LastAccessTime datetimeoffset(7) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_item')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'LastAccessTime', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后访问时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'LastAccessTime';
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'AccessCount') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD AccessCount int NOT NULL
            CONSTRAINT DF_fn_document_item_AccessCount DEFAULT (0);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_item')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'AccessCount', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'访问次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'AccessCount';
END;

IF COL_LENGTH(N'dbo.fn_document_item', N'Sort') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_item
        ADD Sort int NOT NULL
            CONSTRAINT DF_fn_document_item_Sort DEFAULT (0);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_item')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_item'), N'Sort', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'Sort';
END;

-- 4. fn_document_version 新增：ChangeDescription（变更说明）、FileName、MimeType、Extension
--    FileName/MimeType/Extension 来自版本上传时的 Files 元数据快照，避免匿名分享响应
--    需要跨模块 JOIN fn_files_file 破坏模块边界。
-- ============================================================
IF COL_LENGTH(N'dbo.fn_document_version', N'ChangeDescription') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_version
        ADD ChangeDescription nvarchar(500) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_version')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'ChangeDescription', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'变更说明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'ChangeDescription';
END;

IF COL_LENGTH(N'dbo.fn_document_version', N'FileName') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_version
        ADD FileName nvarchar(260) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_version')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'FileName', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'FileName';
END;

IF COL_LENGTH(N'dbo.fn_document_version', N'MimeType') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_version
        ADD MimeType nvarchar(128) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_version')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'MimeType', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'MIME 类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'MimeType';
END;

IF COL_LENGTH(N'dbo.fn_document_version', N'Extension') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_version
        ADD Extension nvarchar(32) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_version')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version'), N'Extension', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件扩展名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version', @level2type=N'COLUMN', @level2name=N'Extension';
END;

-- ============================================================
-- 5. fn_document_permission 文档权限表
--    支持用户/部门/角色对象，权限类型：View/Download/Edit/Delete/Share
--    列清单严格对齐 DocumentPermissionSql.Projection / Insert：
--    Id, TenantId, DocumentId, UserId, PermissionLevel, CreatedAtUtc
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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档权限表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'DocumentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'DocumentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'PermissionLevel', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权限级别', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'PermissionLevel';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'UserId';
    CREATE UNIQUE CLUSTERED INDEX CX_fn_document_permission_Scope_Document_User
        ON dbo.fn_document_permission(TenantId, DocumentId, UserId);
END;

-- 5.1 fn_document_permission 逐列幂等补列（表已存在但字段缺漏场景）
IF COL_LENGTH(N'dbo.fn_document_permission', N'TenantId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_permission
        ADD TenantId uniqueidentifier NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'TenantId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'TenantId';
END;

IF COL_LENGTH(N'dbo.fn_document_permission', N'DocumentId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_permission
        ADD DocumentId uniqueidentifier NOT NULL
            CONSTRAINT DF_fn_document_permission_DocumentId DEFAULT ('00000000-0000-0000-0000-000000000000');
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'DocumentId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'DocumentId';
    ALTER TABLE dbo.fn_document_permission
        DROP CONSTRAINT IF EXISTS DF_fn_document_permission_DocumentId;
END;

IF COL_LENGTH(N'dbo.fn_document_permission', N'UserId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_permission
        ADD UserId uniqueidentifier NOT NULL
            CONSTRAINT DF_fn_document_permission_UserId DEFAULT ('00000000-0000-0000-0000-000000000000');
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'UserId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'UserId';
    ALTER TABLE dbo.fn_document_permission
        DROP CONSTRAINT IF EXISTS DF_fn_document_permission_UserId;
END;

IF COL_LENGTH(N'dbo.fn_document_permission', N'PermissionLevel') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_permission
        ADD PermissionLevel nvarchar(64) NOT NULL
            CONSTRAINT DF_fn_document_permission_PermissionLevel DEFAULT ('');
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'PermissionLevel', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权限级别', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'PermissionLevel';
    ALTER TABLE dbo.fn_document_permission
        DROP CONSTRAINT IF EXISTS DF_fn_document_permission_PermissionLevel;
END;

IF COL_LENGTH(N'dbo.fn_document_permission', N'CreatedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_permission
        ADD CreatedAtUtc datetimeoffset(7) NOT NULL
            CONSTRAINT DF_fn_document_permission_CreatedAtUtc DEFAULT ('0001-01-01T00:00:00+00:00');
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_permission')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_permission'), N'CreatedAtUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    ALTER TABLE dbo.fn_document_permission
        DROP CONSTRAINT IF EXISTS DF_fn_document_permission_CreatedAtUtc;
END;

-- ============================================================
-- 6. fn_document_share 文档分享表
--    列清单严格对齐 DocumentShareSql.Projection / Insert：
--    Id, TenantId, DocumentId, ShareCode, CreatedAtUtc, ExpireTime,
--    PasswordHash, MaxAccessCount, AccessCount, IsEnabled, Version
--    ShareCode 使用 varchar(64) 存储 ASCII 随机码，无需 Unicode。
-- ============================================================
IF OBJECT_ID(N'dbo.fn_document_share', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_share
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        DocumentId uniqueidentifier NOT NULL,
        ShareCode varchar(64) NOT NULL,
        PasswordHash nvarchar(1024) NULL,
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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档分享表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'AccessCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'访问次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'AccessCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'DocumentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'DocumentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'ExpireTime', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'ExpireTime';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'MaxAccessCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最大访问次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'MaxAccessCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'PasswordHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密码哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'PasswordHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'ShareCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分享码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'ShareCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_share')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_document_share_Scope_Code
        ON dbo.fn_document_share(TenantId, ShareCode);
    CREATE INDEX IX_fn_document_share_DocumentId
        ON dbo.fn_document_share(DocumentId);
END;
ELSE
BEGIN
    -- 中文注释：表已存在时的漂移收敛逻辑。
    -- 1) 若存在旧 Password 列，需要无损迁移到 PasswordHash 列并扩展长度为 1024：
    --    先创建 PasswordHash、迁移数据、再删除旧列。
    -- 2) 若 PasswordHash 不存在，则补齐列。
    IF COL_LENGTH(N'dbo.fn_document_share', N'PasswordHash') IS NULL
       AND COL_LENGTH(N'dbo.fn_document_share', N'Password') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.fn_document_share
            ADD PasswordHash nvarchar(1024) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'PasswordHash', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密码哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'PasswordHash';
        UPDATE dbo.fn_document_share
            SET PasswordHash = Password
            WHERE PasswordHash IS NULL;
    END;

    IF COL_LENGTH(N'dbo.fn_document_share', N'PasswordHash') IS NULL
       AND COL_LENGTH(N'dbo.fn_document_share', N'Password') IS NULL
    BEGIN
        ALTER TABLE dbo.fn_document_share
            ADD PasswordHash nvarchar(1024) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'PasswordHash', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密码哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'PasswordHash';
    END;

    -- 中文注释：PasswordHash 存在但长度不足 1024 时，扩展列以容纳 Identity v3 格式。
    IF COL_LENGTH(N'dbo.fn_document_share', N'PasswordHash') IS NOT NULL
       AND EXISTS (
           SELECT 1
           FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.fn_document_share')
             AND name = N'PasswordHash'
             AND system_type_id = TYPE_ID(N'nvarchar')
             AND max_length IN (-1, 256, 512, 1024)
             AND max_length < 2048) -- max_length 存储字节数；1024 字符 = 2048 字节 (nvarchar)
    BEGIN
        ALTER TABLE dbo.fn_document_share
            ALTER COLUMN PasswordHash nvarchar(1024) NULL;
    END;

    -- 中文注释：删除遗留的 Password 列（在完成 PasswordHash 收敛之后）。
    IF COL_LENGTH(N'dbo.fn_document_share', N'Password') IS NOT NULL
       AND COL_LENGTH(N'dbo.fn_document_share', N'PasswordHash') IS NOT NULL
    BEGIN
        UPDATE dbo.fn_document_share
            SET PasswordHash = COALESCE(PasswordHash, Password)
            WHERE PasswordHash IS NULL;
        ALTER TABLE dbo.fn_document_share
            DROP COLUMN IF EXISTS Password;
    END;
END;

-- 6.1 fn_document_share 逐列幂等补列（表已存在但字段缺漏场景），此处仅处理其他业务列；
--     PasswordHash 已在上方收敛分支中完成，不再重复处理。
IF COL_LENGTH(N'dbo.fn_document_share', N'TenantId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_share
        ADD TenantId uniqueidentifier NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'TenantId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'TenantId';
END;

IF COL_LENGTH(N'dbo.fn_document_share', N'DocumentId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_share
        ADD DocumentId uniqueidentifier NOT NULL
            CONSTRAINT DF_fn_document_share_DocumentId DEFAULT ('00000000-0000-0000-0000-000000000000');
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'DocumentId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'DocumentId';
    ALTER TABLE dbo.fn_document_share
        DROP CONSTRAINT IF EXISTS DF_fn_document_share_DocumentId;
END;

IF COL_LENGTH(N'dbo.fn_document_share', N'ShareCode') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_share
        ADD ShareCode varchar(64) NOT NULL
            CONSTRAINT DF_fn_document_share_ShareCode DEFAULT ('');
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'ShareCode', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分享码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'ShareCode';
    ALTER TABLE dbo.fn_document_share
        DROP CONSTRAINT IF EXISTS DF_fn_document_share_ShareCode;
END;

IF COL_LENGTH(N'dbo.fn_document_share', N'ExpireTime') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_share
        ADD ExpireTime datetimeoffset(7) NOT NULL
            CONSTRAINT DF_fn_document_share_ExpireTime DEFAULT ('0001-01-01T00:00:00+00:00');
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'ExpireTime', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'ExpireTime';
    ALTER TABLE dbo.fn_document_share
        DROP CONSTRAINT IF EXISTS DF_fn_document_share_ExpireTime;
END;

IF COL_LENGTH(N'dbo.fn_document_share', N'MaxAccessCount') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_share
        ADD MaxAccessCount int NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'MaxAccessCount', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最大访问次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'MaxAccessCount';
END;

IF COL_LENGTH(N'dbo.fn_document_share', N'AccessCount') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_share
        ADD AccessCount int NOT NULL
            CONSTRAINT DF_fn_document_share_AccessCount DEFAULT (0);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'AccessCount', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'访问次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'AccessCount';
END;

IF COL_LENGTH(N'dbo.fn_document_share', N'IsEnabled') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_share
        ADD IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_document_share_IsEnabled DEFAULT (1);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'IsEnabled', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'IsEnabled';
END;

IF COL_LENGTH(N'dbo.fn_document_share', N'Version') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_share
        ADD Version bigint NOT NULL
            CONSTRAINT DF_fn_document_share_Version DEFAULT (1);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'Version', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF COL_LENGTH(N'dbo.fn_document_share', N'CreatedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_share
        ADD CreatedAtUtc datetimeoffset(7) NOT NULL
            CONSTRAINT DF_fn_document_share_CreatedAtUtc DEFAULT ('0001-01-01T00:00:00+00:00');
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_document_share')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_share'), N'CreatedAtUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    ALTER TABLE dbo.fn_document_share
        DROP CONSTRAINT IF EXISTS DF_fn_document_share_CreatedAtUtc;
END;

-- 6.2 fn_document_share 幂等补索引（若漏建）
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_fn_document_share_Scope_Code'
      AND object_id = OBJECT_ID(N'dbo.fn_document_share', N'U')
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_document_share_Scope_Code
        ON dbo.fn_document_share(TenantId, ShareCode);
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_fn_document_share_DocumentId'
      AND object_id = OBJECT_ID(N'dbo.fn_document_share', N'U')
)
BEGIN
    CREATE INDEX IX_fn_document_share_DocumentId
        ON dbo.fn_document_share(DocumentId);
END;
