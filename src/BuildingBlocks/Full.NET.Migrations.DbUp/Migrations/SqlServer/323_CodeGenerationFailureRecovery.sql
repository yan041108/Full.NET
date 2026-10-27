-- 失败 Apply 保留冻结摘要，便于用原检查点恢复；历史无摘要失败记录仍可读取。
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_codegeneration_run') AND name=N'CK_fn_codegeneration_run_Outcome')
    ALTER TABLE dbo.fn_codegeneration_run DROP CONSTRAINT CK_fn_codegeneration_run_Outcome;
ALTER TABLE dbo.fn_codegeneration_run ADD CONSTRAINT CK_fn_codegeneration_run_Outcome
        CHECK
        (
            (
                (Status IN ('running', 'succeeded') OR (OperationKind = 'apply' AND Status = 'failed'))
                AND ModuleKey IS NOT NULL
                AND EntityKey IS NOT NULL
                AND SchemaSha256 IS NOT NULL
                AND LEN(SchemaSha256) = 64
                AND SchemaSha256 NOT LIKE '%[^0-9a-f]%'
                AND
                (
                    (OperationKind IN ('preview', 'apply') AND ArtifactCount > 0)
                    OR
                    (OperationKind = 'rollback' AND ArtifactCount >= 0)
                )
                AND ManifestSha256 IS NOT NULL
                AND LEN(ManifestSha256) = 64
                AND ManifestSha256 NOT LIKE '%[^0-9a-f]%'
                AND ((Status <> 'failed' AND ErrorCode IS NULL) OR (Status = 'failed' AND ErrorCode IS NOT NULL AND LEN(ErrorCode) > 0))
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
                AND LEN(ErrorCode) > 0
            )
        );
COMMIT TRANSACTION;
