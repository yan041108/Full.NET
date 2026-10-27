-- MySQL DDL 隐式提交；按对象状态重入，保留失败 Apply 的恢复摘要。
DROP PROCEDURE IF EXISTS fn_codegeneration_failure_recovery;
DELIMITER $$
CREATE PROCEDURE fn_codegeneration_failure_recovery()
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_codegeneration_run' AND CONSTRAINT_NAME='CK_fn_codegeneration_run_Outcome' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_codegeneration_run DROP CHECK CK_fn_codegeneration_run_Outcome;
    END IF;
    ALTER TABLE fn_codegeneration_run ADD CONSTRAINT CK_fn_codegeneration_run_Outcome
            CHECK
            (
                (
                    (Status IN ('running', 'succeeded') OR (OperationKind = 'apply' AND Status = 'failed'))
                    AND ModuleKey IS NOT NULL
                    AND EntityKey IS NOT NULL
                    AND SchemaSha256 IS NOT NULL
                    AND SchemaSha256 REGEXP '^[0-9a-f]{64}$'
                    AND
                    (
                        (OperationKind IN ('preview', 'apply') AND ArtifactCount > 0)
                        OR
                        (OperationKind = 'rollback' AND ArtifactCount >= 0)
                    )
                    AND ManifestSha256 IS NOT NULL
                    AND ManifestSha256 REGEXP '^[0-9a-f]{64}$'
                    AND ((Status <> 'failed' AND ErrorCode IS NULL) OR (Status = 'failed' AND ErrorCode IS NOT NULL AND CHAR_LENGTH(ErrorCode) > 0))
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
CALL fn_codegeneration_failure_recovery();
DROP PROCEDURE IF EXISTS fn_codegeneration_failure_recovery;
