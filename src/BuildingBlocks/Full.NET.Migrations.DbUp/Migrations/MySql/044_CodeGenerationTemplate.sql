-- 044：Host 代码生成模板目录。
CREATE TABLE IF NOT EXISTS fn_codegeneration_template
(
    Id BINARY(16) NOT NULL,
    Name varchar(128) NOT NULL,
    Description varchar(512) NULL,
    SchemaJson json NOT NULL,
    SchemaSha256 char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    CreatedByUserId BINARY(16) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    UpdatedByUserId BINARY(16) NULL,
    DeletedAtUtc datetime(6) NULL,
    DeletedByUserId BINARY(16) NULL,
    IsDeleted boolean NOT NULL DEFAULT false,
    Version bigint NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_codegeneration_template PRIMARY KEY (Id),
    CONSTRAINT CK_fn_codegeneration_template_SchemaSha256
        CHECK
        (
            CHAR_LENGTH(SchemaSha256) = 64
            AND SchemaSha256 REGEXP '^[0-9a-f]{64}$'
        ),
    CONSTRAINT CK_fn_codegeneration_template_Version CHECK (Version > 0),
    CONSTRAINT CK_fn_codegeneration_template_DeleteAudit
        CHECK
        (
            (IsDeleted = false
             AND DeletedAtUtc IS NULL
             AND DeletedByUserId IS NULL)
            OR
            (IsDeleted = true
             AND DeletedAtUtc IS NOT NULL
             AND DeletedByUserId IS NOT NULL)
        )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- MySQL DDL 会隐式提交，索引修复必须独立覆盖表已创建但索引缺失或形状错误的状态。
DROP PROCEDURE IF EXISTS fn_codegeneration_template_index;
DELIMITER $$
CREATE PROCEDURE fn_codegeneration_template_index()
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_template'
          AND INDEX_NAME = 'IX_fn_codegeneration_template_ActiveUpdatedCreated'
    )
    AND
    (
        (
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_codegeneration_template'
              AND INDEX_NAME =
                  'IX_fn_codegeneration_template_ActiveUpdatedCreated'
        ) <> 4
        OR EXISTS
        (
            SELECT 1
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_codegeneration_template'
              AND INDEX_NAME =
                  'IX_fn_codegeneration_template_ActiveUpdatedCreated'
              AND
              (
                  NON_UNIQUE <> 1
                  OR SUB_PART IS NOT NULL
                  OR
                  (
                      SEQ_IN_INDEX = 1
                      AND
                      (
                          COLUMN_NAME <> 'IsDeleted'
                          OR COLLATION <> 'A'
                      )
                  )
                  OR
                  (
                      SEQ_IN_INDEX = 2
                      AND
                      (
                          COLUMN_NAME <> 'UpdatedAtUtc'
                          OR COLLATION <> 'D'
                      )
                  )
                  OR
                  (
                      SEQ_IN_INDEX = 3
                      AND
                      (
                          COLUMN_NAME <> 'CreatedAtUtc'
                          OR COLLATION <> 'D'
                      )
                  )
                  OR
                  (
                      SEQ_IN_INDEX = 4
                      AND
                      (
                          COLUMN_NAME <> 'Id'
                          OR COLLATION <> 'A'
                      )
                  )
              )
        )
    ) THEN
        DROP INDEX IX_fn_codegeneration_template_ActiveUpdatedCreated
            ON fn_codegeneration_template;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_template'
          AND INDEX_NAME = 'IX_fn_codegeneration_template_ActiveUpdatedCreated'
    ) THEN
        CREATE INDEX IX_fn_codegeneration_template_ActiveUpdatedCreated
            ON fn_codegeneration_template
                (IsDeleted, UpdatedAtUtc DESC, CreatedAtUtc DESC, Id);
    END IF;
END$$
DELIMITER ;

CALL fn_codegeneration_template_index();
DROP PROCEDURE fn_codegeneration_template_index;
