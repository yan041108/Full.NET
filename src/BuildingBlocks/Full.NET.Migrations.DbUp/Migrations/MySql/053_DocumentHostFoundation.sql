-- 053：Host 文档库基础表（分类、标签、文档项、版本与标签关联）。
CREATE TABLE IF NOT EXISTS fn_document_category (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ParentId BINARY(16) NULL COMMENT '父级标识',
    Name varchar(128) NOT NULL COMMENT '名称',
    SortOrder int NOT NULL DEFAULT 0 COMMENT '排序顺序',
    IsDeleted boolean NOT NULL DEFAULT false COMMENT '是否已软删除',
    DeletedAtUtc datetime(6) NULL COMMENT '删除时间(UTC)',
    DeletedByUserId BINARY(16) NULL COMMENT '删除人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_document_category PRIMARY KEY (Id),
    CONSTRAINT CK_fn_document_category_DeleteAudit
        CHECK
        (
            (IsDeleted = false AND DeletedAtUtc IS NULL AND DeletedByUserId IS NULL)
            OR (IsDeleted = true AND DeletedAtUtc IS NOT NULL AND DeletedByUserId IS NOT NULL)
        )
) COMMENT='文档分类表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_document_tag (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    Name varchar(64) NOT NULL COMMENT '名称',
    IsDeleted boolean NOT NULL DEFAULT false COMMENT '是否已软删除',
    DeletedAtUtc datetime(6) NULL COMMENT '删除时间(UTC)',
    DeletedByUserId BINARY(16) NULL COMMENT '删除人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_document_tag PRIMARY KEY (Id),
    CONSTRAINT CK_fn_document_tag_DeleteAudit
        CHECK
        (
            (IsDeleted = false AND DeletedAtUtc IS NULL AND DeletedByUserId IS NULL)
            OR (IsDeleted = true AND DeletedAtUtc IS NOT NULL AND DeletedByUserId IS NOT NULL)
        )
) COMMENT='文档标签表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_document_item (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    CategoryId BINARY(16) NULL COMMENT '分类标识',
    CurrentVersionId BINARY(16) NULL COMMENT '当前版本标识',
    Title varchar(256) NOT NULL COMMENT '标题',
    Description varchar(2000) NULL COMMENT '描述',
    IsDeleted boolean NOT NULL DEFAULT false COMMENT '是否已软删除',
    DeletedAtUtc datetime(6) NULL COMMENT '删除时间(UTC)',
    DeletedByUserId BINARY(16) NULL COMMENT '删除人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人用户标识',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    UpdatedByUserId BINARY(16) NULL COMMENT '更新人用户标识',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_document_item PRIMARY KEY (Id),
    CONSTRAINT FK_fn_document_item_Category
        FOREIGN KEY (CategoryId) REFERENCES fn_document_category (Id),
    CONSTRAINT CK_fn_document_item_DeleteAudit
        CHECK
        (
            (IsDeleted = false AND DeletedAtUtc IS NULL AND DeletedByUserId IS NULL)
            OR (IsDeleted = true AND DeletedAtUtc IS NOT NULL AND DeletedByUserId IS NOT NULL)
        )
) COMMENT='文档条目表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_document_version (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    DocumentItemId BINARY(16) NOT NULL COMMENT '文档项标识',
    FileId BINARY(16) NOT NULL COMMENT '文件标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '内容哈希',
    SizeBytes bigint NOT NULL COMMENT '大小(字节)',
    UploadedByUserId BINARY(16) NOT NULL COMMENT '上传人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_document_version PRIMARY KEY (Id),
    CONSTRAINT FK_fn_document_version_Item
        FOREIGN KEY (DocumentItemId) REFERENCES fn_document_item (Id),
    CONSTRAINT CK_fn_document_version_Number CHECK (VersionNumber > 0),
    CONSTRAINT CK_fn_document_version_ContentHash
        CHECK
        (
            ContentHash IS NULL
            OR (CHAR_LENGTH(ContentHash) = 64 AND ContentHash REGEXP '^[0-9a-f]{64}$')
        )
) COMMENT='文档版本表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_document_tag_assignment (
    DocumentItemId BINARY(16) NOT NULL COMMENT '文档项标识',
    TagId BINARY(16) NOT NULL COMMENT '标签标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_document_tag_assignment PRIMARY KEY (DocumentItemId, TagId),
    CONSTRAINT FK_fn_document_tag_assignment_Item
        FOREIGN KEY (DocumentItemId) REFERENCES fn_document_item (Id),
    CONSTRAINT FK_fn_document_tag_assignment_Tag
        FOREIGN KEY (TagId) REFERENCES fn_document_tag (Id)
) COMMENT='文档标签关联表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS fn_document_host_indexes;
DELIMITER $$
CREATE PROCEDURE fn_document_host_indexes()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_category'
          AND INDEX_NAME = 'UX_fn_document_category_Scope_Parent_Name'
    )
    THEN
        CREATE UNIQUE INDEX UX_fn_document_category_Scope_Parent_Name
            ON fn_document_category (TenantId, ParentId, Name);
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_tag'
          AND INDEX_NAME = 'UX_fn_document_tag_Scope_Name'
    )
    THEN
        CREATE UNIQUE INDEX UX_fn_document_tag_Scope_Name
            ON fn_document_tag (TenantId, Name);
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_version'
          AND INDEX_NAME = 'UX_fn_document_version_Item_Number'
    )
    THEN
        CREATE UNIQUE INDEX UX_fn_document_version_Item_Number
            ON fn_document_version (DocumentItemId, VersionNumber);
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_version'
          AND INDEX_NAME = 'IX_fn_document_version_FileId'
    )
    THEN
        CREATE INDEX IX_fn_document_version_FileId
            ON fn_document_version (FileId);
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND INDEX_NAME = 'IX_fn_document_item_HostList'
    )
    THEN
        CREATE INDEX IX_fn_document_item_HostList
            ON fn_document_item (TenantId, IsDeleted, UpdatedAtUtc, Id);
    END IF;
END$$
DELIMITER ;
CALL fn_document_host_indexes();
DROP PROCEDURE fn_document_host_indexes;

