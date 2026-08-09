-- 093：MySQL DDL 会隐式提交，使用存储过程逐列逐表幂等收敛。
-- 与 SqlServer 093 同构：分类/标签/文档项/版本补字段，新建文档权限与文档分享表。
DROP PROCEDURE IF EXISTS fn_document_admin_net_parity;
DELIMITER $$
CREATE PROCEDURE fn_document_admin_net_parity()
BEGIN
    -- ============================================================
    -- 1. fn_document_category 新增：Code/Icon/Color/Description
    -- ============================================================
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_category'
          AND COLUMN_NAME = 'Code'
    ) THEN
        ALTER TABLE fn_document_category
            ADD COLUMN Code varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_category'
          AND COLUMN_NAME = 'Icon'
    ) THEN
        ALTER TABLE fn_document_category
            ADD COLUMN Icon varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_category'
          AND COLUMN_NAME = 'Color'
    ) THEN
        ALTER TABLE fn_document_category
            ADD COLUMN Color varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_category'
          AND COLUMN_NAME = 'Description'
    ) THEN
        ALTER TABLE fn_document_category
            ADD COLUMN Description varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    -- ============================================================
    -- 2. fn_document_tag 新增：Color/UseCount
    -- ============================================================
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_tag'
          AND COLUMN_NAME = 'Color'
    ) THEN
        ALTER TABLE fn_document_tag
            ADD COLUMN Color varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_tag'
          AND COLUMN_NAME = 'UseCount'
    ) THEN
        ALTER TABLE fn_document_tag
            ADD COLUMN UseCount int NOT NULL DEFAULT 0;
    END IF;

    -- 2.1 fn_document_tag 补齐：Code/Icon/Description（与 Category 统一字段集）
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_tag'
          AND COLUMN_NAME = 'Code'
    ) THEN
        ALTER TABLE fn_document_tag
            ADD COLUMN Code varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_tag'
          AND COLUMN_NAME = 'Icon'
    ) THEN
        ALTER TABLE fn_document_tag
            ADD COLUMN Icon varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_tag'
          AND COLUMN_NAME = 'Description'
    ) THEN
        ALTER TABLE fn_document_tag
            ADD COLUMN Description varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    -- ============================================================
    -- 3. fn_document_item 新增：DocumentNo/DocumentType/SizeKb/Thumbnail/Status/LastAccessTime/AccessCount/Sort
    -- ============================================================
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'DocumentNo'
    ) THEN
        ALTER TABLE fn_document_item
            ADD COLUMN DocumentNo varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'DocumentType'
    ) THEN
        ALTER TABLE fn_document_item
            ADD COLUMN DocumentType int NOT NULL DEFAULT 99;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'SizeKb'
    ) THEN
        ALTER TABLE fn_document_item
            ADD COLUMN SizeKb bigint NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'Thumbnail'
    ) THEN
        ALTER TABLE fn_document_item
            ADD COLUMN Thumbnail varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'Status'
    ) THEN
        ALTER TABLE fn_document_item
            ADD COLUMN Status int NOT NULL DEFAULT 2;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'LastAccessTime'
    ) THEN
        ALTER TABLE fn_document_item
            ADD COLUMN LastAccessTime datetime(6) NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'AccessCount'
    ) THEN
        ALTER TABLE fn_document_item
            ADD COLUMN AccessCount int NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'Sort'
    ) THEN
        ALTER TABLE fn_document_item
            ADD COLUMN Sort int NOT NULL DEFAULT 0;
    END IF;

    -- ============================================================
    -- 4. fn_document_version 新增：ChangeDescription
    -- ============================================================
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_version'
          AND COLUMN_NAME = 'ChangeDescription'
    ) THEN
        ALTER TABLE fn_document_version
            ADD COLUMN ChangeDescription varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    -- ============================================================
    -- 5. fn_document_permission 文档权限表
    -- ============================================================
    CREATE TABLE IF NOT EXISTS fn_document_permission
    (
        Id BINARY(16) NOT NULL,
        TenantId BINARY(16) NULL,
        DocumentId BINARY(16) NOT NULL,
        PermissionType int NOT NULL,
        ObjectType int NOT NULL,
        ObjectId BINARY(16) NOT NULL,
        CreatedAtUtc datetime(6) NOT NULL,
        CreatedByUserId BINARY(16) NOT NULL,
        CONSTRAINT PK_fn_document_permission PRIMARY KEY (Id),
        CONSTRAINT FK_fn_document_permission_Document
            FOREIGN KEY (DocumentId) REFERENCES fn_document_item (Id) ON DELETE CASCADE
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_permission'
          AND INDEX_NAME = 'UX_fn_document_permission_Scope_Document_Object_Type'
    )
    THEN
        CREATE UNIQUE INDEX UX_fn_document_permission_Scope_Document_Object_Type
            ON fn_document_permission (TenantId, DocumentId, ObjectType, ObjectId, PermissionType);
    END IF;

    -- ============================================================
    -- 6. fn_document_share 文档分享表
    -- ============================================================
    CREATE TABLE IF NOT EXISTS fn_document_share
    (
        Id BINARY(16) NOT NULL,
        TenantId BINARY(16) NULL,
        DocumentId BINARY(16) NOT NULL,
        ShareCode varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
        Password varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL,
        ValidDays int NOT NULL DEFAULT 0,
        ExpireTime datetime(6) NULL,
        AccessCount int NOT NULL DEFAULT 0,
        SharePermission int NOT NULL DEFAULT 1,
        IsEnabled boolean NOT NULL DEFAULT true,
        CreatedAtUtc datetime(6) NOT NULL,
        CreatedByUserId BINARY(16) NOT NULL,
        CONSTRAINT PK_fn_document_share PRIMARY KEY (Id),
        CONSTRAINT FK_fn_document_share_Document
            FOREIGN KEY (DocumentId) REFERENCES fn_document_item (Id) ON DELETE CASCADE
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND INDEX_NAME = 'UX_fn_document_share_Scope_Code'
    )
    THEN
        CREATE UNIQUE INDEX UX_fn_document_share_Scope_Code
            ON fn_document_share (TenantId, ShareCode);
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND INDEX_NAME = 'IX_fn_document_share_DocumentId'
    )
    THEN
        CREATE INDEX IX_fn_document_share_DocumentId
            ON fn_document_share (DocumentId);
    END IF;
END$$
DELIMITER ;

CALL fn_document_admin_net_parity();
DROP PROCEDURE fn_document_admin_net_parity;
