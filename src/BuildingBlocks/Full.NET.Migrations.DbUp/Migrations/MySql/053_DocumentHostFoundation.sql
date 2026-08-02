-- 053：Host 文档库基础表（分类、标签、文档项、版本与标签关联）。
CREATE TABLE IF NOT EXISTS fn_document_category
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    ParentId BINARY(16) NULL,
    Name varchar(128) NOT NULL,
    SortOrder int NOT NULL DEFAULT 0,
    IsDeleted boolean NOT NULL DEFAULT false,
    DeletedAtUtc datetime(6) NULL,
    DeletedByUserId BINARY(16) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_document_category PRIMARY KEY (Id),
    CONSTRAINT CK_fn_document_category_DeleteAudit
        CHECK
        (
            (IsDeleted = false AND DeletedAtUtc IS NULL AND DeletedByUserId IS NULL)
            OR (IsDeleted = true AND DeletedAtUtc IS NOT NULL AND DeletedByUserId IS NOT NULL)
        )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_document_tag
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    Name varchar(64) NOT NULL,
    IsDeleted boolean NOT NULL DEFAULT false,
    DeletedAtUtc datetime(6) NULL,
    DeletedByUserId BINARY(16) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_document_tag PRIMARY KEY (Id),
    CONSTRAINT CK_fn_document_tag_DeleteAudit
        CHECK
        (
            (IsDeleted = false AND DeletedAtUtc IS NULL AND DeletedByUserId IS NULL)
            OR (IsDeleted = true AND DeletedAtUtc IS NOT NULL AND DeletedByUserId IS NOT NULL)
        )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_document_item
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    CategoryId BINARY(16) NULL,
    CurrentVersionId BINARY(16) NULL,
    Title varchar(256) NOT NULL,
    Description varchar(2000) NULL,
    IsDeleted boolean NOT NULL DEFAULT false,
    DeletedAtUtc datetime(6) NULL,
    DeletedByUserId BINARY(16) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    CreatedByUserId BINARY(16) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    UpdatedByUserId BINARY(16) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_document_item PRIMARY KEY (Id),
    CONSTRAINT FK_fn_document_item_Category
        FOREIGN KEY (CategoryId) REFERENCES fn_document_category (Id),
    CONSTRAINT CK_fn_document_item_DeleteAudit
        CHECK
        (
            (IsDeleted = false AND DeletedAtUtc IS NULL AND DeletedByUserId IS NULL)
            OR (IsDeleted = true AND DeletedAtUtc IS NOT NULL AND DeletedByUserId IS NOT NULL)
        )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_document_version
(
    Id BINARY(16) NOT NULL,
    DocumentItemId BINARY(16) NOT NULL,
    FileId BINARY(16) NOT NULL,
    VersionNumber int NOT NULL,
    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    SizeBytes bigint NOT NULL,
    UploadedByUserId BINARY(16) NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_document_tag_assignment
(
    DocumentItemId BINARY(16) NOT NULL,
    TagId BINARY(16) NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    CONSTRAINT PK_fn_document_tag_assignment PRIMARY KEY (DocumentItemId, TagId),
    CONSTRAINT FK_fn_document_tag_assignment_Item
        FOREIGN KEY (DocumentItemId) REFERENCES fn_document_item (Id),
    CONSTRAINT FK_fn_document_tag_assignment_Tag
        FOREIGN KEY (TagId) REFERENCES fn_document_tag (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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

