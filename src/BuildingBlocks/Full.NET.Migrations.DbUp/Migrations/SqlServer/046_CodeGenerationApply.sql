-- 046：为 Host Apply 增加可恢复的 running 到终态状态机。
IF OBJECT_ID(N'dbo.CK_fn_codegeneration_run_Outcome', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.fn_codegeneration_run
        DROP CONSTRAINT CK_fn_codegeneration_run_Outcome;
END;

IF OBJECT_ID(N'dbo.CK_fn_codegeneration_run_Status', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.fn_codegeneration_run
        DROP CONSTRAINT CK_fn_codegeneration_run_Status;
END;

IF OBJECT_ID(N'dbo.CK_fn_codegeneration_run_Operation', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.fn_codegeneration_run
        DROP CONSTRAINT CK_fn_codegeneration_run_Operation;
END;

IF OBJECT_ID(N'dbo.CK_fn_codegeneration_run_ApplyTemplate', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.fn_codegeneration_run
        DROP CONSTRAINT CK_fn_codegeneration_run_ApplyTemplate;
END;

ALTER TABLE dbo.fn_codegeneration_run
    ADD CONSTRAINT CK_fn_codegeneration_run_Operation
        CHECK (OperationKind IN ('preview', 'apply')),
        CONSTRAINT CK_fn_codegeneration_run_Status
        CHECK (Status IN ('running', 'succeeded', 'failed')),
        CONSTRAINT CK_fn_codegeneration_run_ApplyTemplate
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
        CONSTRAINT CK_fn_codegeneration_run_Outcome
        CHECK
        (
            (
                Status IN ('running', 'succeeded')
                AND ModuleKey IS NOT NULL
                AND EntityKey IS NOT NULL
                AND SchemaSha256 IS NOT NULL
                AND LEN(SchemaSha256) = 64
                AND SchemaSha256 NOT LIKE '%[^0-9a-f]%'
                AND ArtifactCount > 0
                AND ManifestSha256 IS NOT NULL
                AND LEN(ManifestSha256) = 64
                AND ManifestSha256 NOT LIKE '%[^0-9a-f]%'
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
                AND LEN(ErrorCode) > 0
            )
        );
