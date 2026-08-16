-- 045：Host 代码生成运行摘要。
CREATE TABLE IF NOT EXISTS fn_codegeneration_run (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TemplateId BINARY(16) NULL COMMENT '模板标识',
    TemplateVersion bigint NULL COMMENT '模板版本',
    OperationKind varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作类型',
    Status varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态',
    ModuleKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '模块键',
    EntityKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '实体键',
    SchemaSha256 char(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT 'Schema SHA256',
    ArtifactCount int NOT NULL COMMENT '产物数量',
    ManifestSha256 char(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '清单 SHA256',
    ErrorCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '错误码',
    RequestedByUserId BINARY(16) NOT NULL COMMENT '请求人用户标识',
    StartedAtUtc datetime(6) NOT NULL COMMENT '开始时间(UTC)',
    FinishedAtUtc datetime(6) NOT NULL COMMENT '结束时间(UTC)',
    CONSTRAINT PK_fn_codegeneration_run PRIMARY KEY (Id),
    CONSTRAINT CK_fn_codegeneration_run_Operation
        CHECK (OperationKind = 'preview'),
    CONSTRAINT CK_fn_codegeneration_run_Status
        CHECK (Status IN ('succeeded', 'failed')),
    CONSTRAINT CK_fn_codegeneration_run_Template
        CHECK
        (
            (TemplateId IS NULL AND TemplateVersion IS NULL)
            OR
            (TemplateId IS NOT NULL AND TemplateVersion > 0)
        ),
    CONSTRAINT CK_fn_codegeneration_run_Outcome
        CHECK
        (
            (
                Status = 'succeeded'
                AND ModuleKey IS NOT NULL
                AND EntityKey IS NOT NULL
                AND SchemaSha256 REGEXP '^[0-9a-f]{64}$'
                AND ArtifactCount > 0
                AND ManifestSha256 REGEXP '^[0-9a-f]{64}$'
                AND ErrorCode IS NULL
            )
            OR
            (
                Status = 'failed'
                AND ModuleKey IS NULL
                AND EntityKey IS NULL
                AND SchemaSha256 IS NULL
                AND ArtifactCount = 0
                AND ManifestSha256 IS NULL
                AND ErrorCode IS NOT NULL
                AND CHAR_LENGTH(ErrorCode) > 0
            )
        ),
    CONSTRAINT CK_fn_codegeneration_run_Time
        CHECK (FinishedAtUtc >= StartedAtUtc)
) COMMENT='代码生成运行表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- MySQL DDL 会隐式提交，索引修复必须独立覆盖表已存在但索引缺失或形状错误的状态。
DROP PROCEDURE IF EXISTS fn_codegeneration_run_index;
DELIMITER $$
CREATE PROCEDURE fn_codegeneration_run_index()
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND INDEX_NAME = 'IX_fn_codegeneration_run_StatusStarted'
    )
    AND
    (
        (
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_codegeneration_run'
              AND INDEX_NAME = 'IX_fn_codegeneration_run_StatusStarted'
        ) <> 3
        OR EXISTS
        (
            SELECT 1
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_codegeneration_run'
              AND INDEX_NAME = 'IX_fn_codegeneration_run_StatusStarted'
              AND
              (
                  NON_UNIQUE <> 1
                  OR SUB_PART IS NOT NULL
                  OR
                  (
                      SEQ_IN_INDEX = 1
                      AND (COLUMN_NAME <> 'Status' OR COLLATION <> 'A')
                  )
                  OR
                  (
                      SEQ_IN_INDEX = 2
                      AND (COLUMN_NAME <> 'StartedAtUtc' OR COLLATION <> 'D')
                  )
                  OR
                  (
                      SEQ_IN_INDEX = 3
                      AND (COLUMN_NAME <> 'Id' OR COLLATION <> 'A')
                  )
              )
        )
    ) THEN
        DROP INDEX IX_fn_codegeneration_run_StatusStarted
            ON fn_codegeneration_run;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND INDEX_NAME = 'IX_fn_codegeneration_run_StatusStarted'
    ) THEN
        CREATE INDEX IX_fn_codegeneration_run_StatusStarted
            ON fn_codegeneration_run (Status, StartedAtUtc DESC, Id);
    END IF;
END$$
DELIMITER ;

CALL fn_codegeneration_run_index();
DROP PROCEDURE fn_codegeneration_run_index;
