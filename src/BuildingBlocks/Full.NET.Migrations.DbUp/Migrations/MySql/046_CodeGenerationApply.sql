-- 046：为 Host Apply 增加可恢复的 running 到终态状态机。
DROP PROCEDURE IF EXISTS fn_codegeneration_apply_constraints;
DELIMITER $$
CREATE PROCEDURE fn_codegeneration_apply_constraints()
BEGIN
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
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_codegeneration_run'
          AND CONSTRAINT_NAME = 'CK_fn_codegeneration_run_ApplyTemplate'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_codegeneration_run
            DROP CHECK CK_fn_codegeneration_run_ApplyTemplate;
    END IF;

    ALTER TABLE fn_codegeneration_run
        ADD CONSTRAINT CK_fn_codegeneration_run_Operation
            CHECK (OperationKind IN ('preview', 'apply')),
        ADD CONSTRAINT CK_fn_codegeneration_run_Status
            CHECK (Status IN ('running', 'succeeded', 'failed')),
        ADD CONSTRAINT CK_fn_codegeneration_run_ApplyTemplate
            CHECK
            (
                (OperationKind = 'preview' AND Status <> 'running')
                OR
                (
                    OperationKind = 'apply'
                    AND TemplateId IS NOT NULL
                    AND TemplateVersion > 0
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
            );
END$$
DELIMITER ;

CALL fn_codegeneration_apply_constraints();
DROP PROCEDURE fn_codegeneration_apply_constraints;
