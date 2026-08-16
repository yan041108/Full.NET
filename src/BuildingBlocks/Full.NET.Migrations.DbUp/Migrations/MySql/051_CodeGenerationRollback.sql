-- 051：为 Host Rollback 增加 SourceApplyRunId 与成功回滚唯一约束。
DROP PROCEDURE IF EXISTS fn_codegeneration_rollback_boundary;
DELIMITER $$
CREATE PROCEDURE fn_codegeneration_rollback_boundary()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND COLUMN_NAME = 'SourceApplyRunId'
    ) THENALTER TABLE fn_codegeneration_run ADD SourceApplyRunId BINARY(16) NULL
                AFTER TemplateVersion COMMENT '来源应用运行标识'
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND INDEX_NAME =
              'UX_fn_codegeneration_run_SucceededRollbackSourceApplyRunId'
    ) THEN
        ALTER TABLE fn_codegeneration_run
            DROP INDEX UX_fn_codegeneration_run_SucceededRollbackSourceApplyRunId;
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND COLUMN_NAME = 'SucceededRollbackSourceApplyRunId'
    ) THEN
        ALTER TABLE fn_codegeneration_run
            DROP COLUMN SucceededRollbackSourceApplyRunId;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND COLUMN_NAME = 'SucceededRollbackSourceApplyRunId'
    ) THENALTER TABLE fn_codegeneration_run ADD SucceededRollbackSourceApplyRunId BINARY(16)
                GENERATED ALWAYS AS
                (
                    CASE
                        WHEN OperationKind = 'rollback'
                         AND Status = 'succeeded'
                        THEN SourceApplyRunId
                        ELSE NULL
                    END
                )
                STORED
                AFTER SourceApplyRunId COMMENT '成功回滚来源应用运行标识'
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND CONSTRAINT_NAME = 'CK_fn_codegeneration_run_Outcome'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_codegeneration_run
            DROP CHECK CK_fn_codegeneration_run_Outcome;
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND CONSTRAINT_NAME = 'CK_fn_codegeneration_run_Status'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_codegeneration_run
            DROP CHECK CK_fn_codegeneration_run_Status;
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND CONSTRAINT_NAME = 'CK_fn_codegeneration_run_Operation'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_codegeneration_run
            DROP CHECK CK_fn_codegeneration_run_Operation;
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND CONSTRAINT_NAME = 'CK_fn_codegeneration_run_ApplyTemplate'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_codegeneration_run
            DROP CHECK CK_fn_codegeneration_run_ApplyTemplate;
    END IF;

    ALTER TABLE fn_codegeneration_run
        ADD CONSTRAINT CK_fn_codegeneration_run_Operation
            CHECK (OperationKind IN ('preview', 'apply', 'rollback')),
        ADD CONSTRAINT CK_fn_codegeneration_run_Status
            CHECK (Status IN ('running', 'succeeded', 'failed')),
        ADD CONSTRAINT CK_fn_codegeneration_run_ApplyTemplate
            CHECK
            (
                (
                    OperationKind IN ('preview', 'apply')
                    AND SourceApplyRunId IS NULL
                    AND
                    (
                        (OperationKind = 'preview' AND Status <> 'running')
                        OR
                        (
                            OperationKind = 'apply'
                            AND TemplateId IS NOT NULL
                            AND TemplateVersion > 0
                        )
                    )
                )
                OR
                (
                    OperationKind = 'rollback'
                    AND SourceApplyRunId IS NOT NULL
                    AND TemplateId IS NULL
                    AND TemplateVersion IS NULL
                )
            ),
        ADD CONSTRAINT CK_fn_codegeneration_run_Outcome
            CHECK
            (
                (
                    Status IN ('running', 'succeeded')
                    AND ModuleKey IS NOT NULL
                    AND EntityKey IS NOT NULL
                    AND SchemaSha256 REGEXP '^[0-9a-f]{64}$'
                    AND
                    (
                        (OperationKind IN ('preview', 'apply') AND ArtifactCount > 0)
                        OR
                        (OperationKind = 'rollback' AND ArtifactCount >= 0)
                    )
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
            );

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND INDEX_NAME =
              'UX_fn_codegeneration_run_SucceededRollbackSourceApplyRunId'
    ) THEN
        ALTER TABLE fn_codegeneration_run
            ADD UNIQUE INDEX UX_fn_codegeneration_run_SucceededRollbackSourceApplyRunId
                (SucceededRollbackSourceApplyRunId);
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND INDEX_NAME = 'IX_fn_codegeneration_run_SourceApplyRunId'
    )
    AND
    (
        (
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_codegeneration_run'
              AND INDEX_NAME = 'IX_fn_codegeneration_run_SourceApplyRunId'
        ) <> 1
        OR EXISTS
        (
            SELECT 1
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_codegeneration_run'
              AND INDEX_NAME = 'IX_fn_codegeneration_run_SourceApplyRunId'
              AND
              (
                  NON_UNIQUE <> 1
                  OR SUB_PART IS NOT NULL
                  OR COLUMN_NAME <> 'SourceApplyRunId'
              )
        )
    ) THEN
        DROP INDEX IX_fn_codegeneration_run_SourceApplyRunId
            ON fn_codegeneration_run;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND INDEX_NAME = 'IX_fn_codegeneration_run_SourceApplyRunId'
    ) THEN
        CREATE INDEX IX_fn_codegeneration_run_SourceApplyRunId
            ON fn_codegeneration_run (SourceApplyRunId);
    END IF;
END$$
DELIMITER ;

CALL fn_codegeneration_rollback_boundary();
DROP PROCEDURE fn_codegeneration_rollback_boundary;