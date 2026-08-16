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
        ALTER TABLE fn_document_category ADD Code varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '编码';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_category'
          AND COLUMN_NAME = 'Icon'
    ) THEN
        ALTER TABLE fn_document_category ADD Icon varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '图标';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_category'
          AND COLUMN_NAME = 'Color'
    ) THEN
        ALTER TABLE fn_document_category ADD Color varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '颜色';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_category'
          AND COLUMN_NAME = 'Description'
    ) THEN
        ALTER TABLE fn_document_category ADD Description varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '描述';
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
        ALTER TABLE fn_document_tag ADD Color varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '颜色';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_tag'
          AND COLUMN_NAME = 'UseCount'
    ) THEN
        ALTER TABLE fn_document_tag ADD UseCount int NOT NULL DEFAULT 0 COMMENT '使用次数';
    END IF;

    -- 2.1 fn_document_tag 补齐：Code/Icon/Description（与 Category 统一字段集）
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_tag'
          AND COLUMN_NAME = 'Code'
    ) THEN
        ALTER TABLE fn_document_tag ADD Code varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '编码';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_tag'
          AND COLUMN_NAME = 'Icon'
    ) THEN
        ALTER TABLE fn_document_tag ADD Icon varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '图标';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_tag'
          AND COLUMN_NAME = 'Description'
    ) THEN
        ALTER TABLE fn_document_tag ADD Description varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '描述';
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
        ALTER TABLE fn_document_item ADD DocumentNo varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '文档编号';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'DocumentType'
    ) THEN
        ALTER TABLE fn_document_item ADD DocumentType int NOT NULL DEFAULT 99 COMMENT '文档类型';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'SizeKb'
    ) THEN
        ALTER TABLE fn_document_item ADD SizeKb bigint NOT NULL DEFAULT 0 COMMENT '大小(KB)';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'Thumbnail'
    ) THEN
        ALTER TABLE fn_document_item ADD Thumbnail varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '缩略图';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'Status'
    ) THEN
        ALTER TABLE fn_document_item ADD Status int NOT NULL DEFAULT 2 COMMENT '状态';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'LastAccessTime'
    ) THEN
        ALTER TABLE fn_document_item ADD LastAccessTime datetime(6) NULL COMMENT '最后访问时间';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'AccessCount'
    ) THEN
        ALTER TABLE fn_document_item ADD AccessCount int NOT NULL DEFAULT 0 COMMENT '访问次数';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_item'
          AND COLUMN_NAME = 'Sort'
    ) THEN
        ALTER TABLE fn_document_item ADD Sort int NOT NULL DEFAULT 0 COMMENT '排序';
    END IF;

    -- ============================================================
    -- 4. fn_document_version 新增：ChangeDescription、FileName、MimeType、Extension
    -- ============================================================
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_version'
          AND COLUMN_NAME = 'ChangeDescription'
    ) THEN
        ALTER TABLE fn_document_version ADD ChangeDescription varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '变更说明';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_version'
          AND COLUMN_NAME = 'FileName'
    ) THEN
        ALTER TABLE fn_document_version ADD FileName varchar(260) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '文件名';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_version'
          AND COLUMN_NAME = 'MimeType'
    ) THEN
        ALTER TABLE fn_document_version ADD MimeType varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT 'MIME 类型';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_version'
          AND COLUMN_NAME = 'Extension'
    ) THEN
        ALTER TABLE fn_document_version ADD Extension varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '文件扩展名';
    END IF;

    -- ============================================================
    -- 5. fn_document_permission 文档权限表
    --    列清单严格对齐 DocumentPermissionSql.Projection / Insert：
    --    Id, TenantId, DocumentId, UserId, PermissionLevel, CreatedAtUtc
    -- ============================================================
    CREATE TABLE IF NOT EXISTS fn_document_permission (
        Id BINARY(16) NOT NULL COMMENT '逻辑主键',
        TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
        DocumentId BINARY(16) NOT NULL COMMENT '文档标识',
        UserId BINARY(16) NOT NULL COMMENT '用户标识',
        PermissionLevel varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '权限级别',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
        CONSTRAINT PK_fn_document_permission PRIMARY KEY (Id),
        CONSTRAINT FK_fn_document_permission_Document
            FOREIGN KEY (DocumentId) REFERENCES fn_document_item (Id) ON DELETE CASCADE
    ) COMMENT='文档权限表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

    -- 5.1 fn_document_permission 逐列幂等补列（表已存在但字段缺漏场景）
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_permission'
          AND COLUMN_NAME = 'TenantId'
    ) THEN
        ALTER TABLE fn_document_permission ADD TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_permission'
          AND COLUMN_NAME = 'DocumentId'
    ) THEN
        ALTER TABLE fn_document_permission ADD DocumentId BINARY(16) NOT NULL
            DEFAULT (0x00000000000000000000000000000000) COMMENT '文档标识';
        ALTER TABLE fn_document_permission
            ALTER COLUMN DocumentId DROP DEFAULT;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_permission'
          AND COLUMN_NAME = 'UserId'
    ) THEN
        ALTER TABLE fn_document_permission ADD UserId BINARY(16) NOT NULL
            DEFAULT (0x00000000000000000000000000000000) COMMENT '用户标识';
        ALTER TABLE fn_document_permission
            ALTER COLUMN UserId DROP DEFAULT;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_permission'
          AND COLUMN_NAME = 'PermissionLevel'
    ) THEN
        ALTER TABLE fn_document_permission ADD PermissionLevel varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL
            DEFAULT '' COMMENT '权限级别';
        ALTER TABLE fn_document_permission
            ALTER COLUMN PermissionLevel DROP DEFAULT;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_permission'
          AND COLUMN_NAME = 'CreatedAtUtc'
    ) THEN
        ALTER TABLE fn_document_permission ADD CreatedAtUtc datetime(6) NOT NULL
            DEFAULT '1970-01-01 00:00:00' COMMENT '创建时间(UTC)';
        ALTER TABLE fn_document_permission
            ALTER COLUMN CreatedAtUtc DROP DEFAULT;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_permission'
          AND INDEX_NAME = 'UX_fn_document_permission_Scope_Document_User'
    )
    THEN
        CREATE UNIQUE INDEX UX_fn_document_permission_Scope_Document_User
            ON fn_document_permission (TenantId, DocumentId, UserId);
    END IF;

    -- ============================================================
    -- 6. fn_document_share 文档分享表
    --    列清单严格对齐 DocumentShareSql.Projection / Insert：
    --    Id, TenantId, DocumentId, ShareCode, CreatedAtUtc, ExpireTime,
    --    PasswordHash, MaxAccessCount, AccessCount, IsEnabled, Version
    --    ShareCode 使用 ASCII 字符集，无需 Unicode 排序规则。
    -- ============================================================
    CREATE TABLE IF NOT EXISTS fn_document_share (
        Id BINARY(16) NOT NULL COMMENT '逻辑主键',
        TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
        DocumentId BINARY(16) NOT NULL COMMENT '文档标识',
        ShareCode varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '分享码',
        PasswordHash varchar(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '密码哈希',
        ExpireTime datetime(6) NOT NULL COMMENT '过期时间',
        MaxAccessCount int NULL COMMENT '最大访问次数',
        AccessCount int NOT NULL DEFAULT 0 COMMENT '访问次数',
        IsEnabled boolean NOT NULL DEFAULT true COMMENT '是否启用',
        Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
        CONSTRAINT PK_fn_document_share PRIMARY KEY (Id),
        CONSTRAINT FK_fn_document_share_Document
            FOREIGN KEY (DocumentId) REFERENCES fn_document_item (Id) ON DELETE CASCADE
    ) COMMENT='文档分享表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

    -- 6.0 fn_document_share：Password → PasswordHash 列收敛迁移
    --     表已存在时：保证 PasswordHash 列存在、长度 1024、内容包含旧 Password；
    --     收敛完成后删除遗留 Password 列。
    IF EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'Password'
    ) AND NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'PasswordHash'
    ) THEN
        ALTER TABLE fn_document_share ADD PasswordHash varchar(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL
            COMMENT '密码哈希' AFTER ShareCode;
        UPDATE fn_document_share
            SET PasswordHash = Password
            WHERE PasswordHash IS NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'PasswordHash'
    ) THEN
        ALTER TABLE fn_document_share ADD PasswordHash varchar(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL
            COMMENT '密码哈希' AFTER ShareCode;
    END IF;

    -- 中文注释：PasswordHash 已存在但字符长度 < 1024 时扩展，容纳 ASP.NET Core Identity v3 PBKDF2 输出。
    IF EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'PasswordHash'
          AND CHARACTER_MAXIMUM_LENGTH < 1024
    ) THEN
        ALTER TABLE fn_document_share
            MODIFY COLUMN PasswordHash varchar(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
    END IF;

    -- 中文注释：收敛完毕删除旧 Password 列（在确保 PasswordHash 已补齐数据之后）。
    IF EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'Password'
    ) AND EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'PasswordHash'
    ) THEN
        UPDATE fn_document_share
            SET PasswordHash = COALESCE(PasswordHash, Password)
            WHERE PasswordHash IS NULL;
        ALTER TABLE fn_document_share
            DROP COLUMN Password;
    END IF;

    -- 6.1 fn_document_share 逐列幂等补列（表已存在但字段缺漏场景），仅处理其他业务列；
    --     PasswordHash 已在 6.0 收敛分支中完成，不再重复处理。
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'TenantId'
    ) THEN
        ALTER TABLE fn_document_share ADD TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'DocumentId'
    ) THEN
        ALTER TABLE fn_document_share ADD DocumentId BINARY(16) NOT NULL
            DEFAULT (0x00000000000000000000000000000000) COMMENT '文档标识';
        ALTER TABLE fn_document_share
            ALTER COLUMN DocumentId DROP DEFAULT;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'ShareCode'
    ) THEN
        ALTER TABLE fn_document_share ADD ShareCode varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL
            DEFAULT '' COMMENT '分享码';
        ALTER TABLE fn_document_share
            ALTER COLUMN ShareCode DROP DEFAULT;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'ExpireTime'
    ) THEN
        ALTER TABLE fn_document_share ADD ExpireTime datetime(6) NOT NULL
            DEFAULT '1970-01-01 00:00:00' COMMENT '过期时间';
        ALTER TABLE fn_document_share
            ALTER COLUMN ExpireTime DROP DEFAULT;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'MaxAccessCount'
    ) THEN
        ALTER TABLE fn_document_share ADD MaxAccessCount int NULL COMMENT '最大访问次数';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'AccessCount'
    ) THEN
        ALTER TABLE fn_document_share ADD AccessCount int NOT NULL DEFAULT 0 COMMENT '访问次数';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'IsEnabled'
    ) THEN
        ALTER TABLE fn_document_share ADD IsEnabled boolean NOT NULL DEFAULT true COMMENT '是否启用';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'Version'
    ) THEN
        ALTER TABLE fn_document_share ADD Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_document_share'
          AND COLUMN_NAME = 'CreatedAtUtc'
    ) THEN
        ALTER TABLE fn_document_share ADD CreatedAtUtc datetime(6) NOT NULL
            DEFAULT '1970-01-01 00:00:00' COMMENT '创建时间(UTC)';
        ALTER TABLE fn_document_share
            ALTER COLUMN CreatedAtUtc DROP DEFAULT;
    END IF;

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
