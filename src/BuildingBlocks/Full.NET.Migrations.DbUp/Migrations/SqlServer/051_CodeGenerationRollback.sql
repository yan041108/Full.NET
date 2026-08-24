-- 051：为 Host Rollback 增加 SourceApplyRunId 与成功回滚唯一约束。
-- 新增列后的 CHECK/INDEX 必须进入动态 SQL，避免同批次编译期看不到新列。
IF COL_LENGTH(N'dbo.fn_codegeneration_run', N'SourceApplyRunId') IS NULL
BEGIN
    EXEC(N'
        ALTER TABLE dbo.fn_codegeneration_run
            ADD SourceApplyRunId uniqueidentifier NULL;
    ');
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_codegeneration_run'), N'SourceApplyRunId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'来源应用运行标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_codegeneration_run', @level2type=N'COLUMN', @level2name=N'SourceApplyRunId';
END;

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

EXEC(N'
ALTER TABLE dbo.fn_codegeneration_run
    ADD CONSTRAINT CK_fn_codegeneration_run_Operation
        CHECK (OperationKind IN (''preview'', ''apply'', ''rollback'')),
        CONSTRAINT CK_fn_codegeneration_run_Status
        CHECK (Status IN (''running'', ''succeeded'', ''failed'')),
        CONSTRAINT CK_fn_codegeneration_run_ApplyTemplate
        CHECK
        (
            (
                OperationKind IN (''preview'', ''apply'')
                AND SourceApplyRunId IS NULL
                AND
                (
                    (OperationKind = ''preview'' AND Status <> ''running'')
                    OR
                    (
                        OperationKind = ''apply''
                        AND TemplateId IS NOT NULL
                        AND TemplateVersion > 0
                    )
                )
            )
            OR
            (
                OperationKind = ''rollback''
                AND SourceApplyRunId IS NOT NULL
                AND TemplateId IS NULL
                AND TemplateVersion IS NULL
            )
        ),
        CONSTRAINT CK_fn_codegeneration_run_Outcome
        CHECK
        (
            (
                Status IN (''running'', ''succeeded'')
                AND ModuleKey IS NOT NULL
                AND EntityKey IS NOT NULL
                AND SchemaSha256 IS NOT NULL
                AND LEN(SchemaSha256) = 64
                AND SchemaSha256 NOT LIKE ''%[^0-9a-f]%''
                AND
                (
                    (OperationKind IN (''preview'', ''apply'') AND ArtifactCount > 0)
                    OR
                    (OperationKind = ''rollback'' AND ArtifactCount >= 0)
                )
                AND ManifestSha256 IS NOT NULL
                AND LEN(ManifestSha256) = 64
                AND ManifestSha256 NOT LIKE ''%[^0-9a-f]%''
                AND ErrorCode IS NULL
            )
            OR
            (
                Status = ''failed''
                AND ModuleKey IS NULL
                AND EntityKey IS NULL
                AND SchemaSha256 IS NULL
                AND ArtifactCount = 0
                AND ManifestSha256 IS NULL
                AND ErrorCode IS NOT NULL
                AND LEN(ErrorCode) > 0
            )
        );
');

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
      AND indexObject.name = N'UX_fn_codegeneration_run_SucceededRollbackSourceApplyRunId'
      AND
      (
          indexObject.is_unique = 0
          OR indexObject.has_filter = 0
          OR indexObject.is_disabled = 1
          OR indexObject.filter_definition <>
              N'(OperationKind=''rollback'' AND Status=''succeeded'')'
      )
)
BEGIN
    DROP INDEX UX_fn_codegeneration_run_SucceededRollbackSourceApplyRunId
        ON dbo.fn_codegeneration_run;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
      AND name = N'UX_fn_codegeneration_run_SucceededRollbackSourceApplyRunId'
)
BEGIN
    EXEC(N'
    CREATE UNIQUE INDEX UX_fn_codegeneration_run_SucceededRollbackSourceApplyRunId
        ON dbo.fn_codegeneration_run (SourceApplyRunId)
        WHERE OperationKind = ''rollback''
          AND Status = ''succeeded'';
    ');
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
      AND indexObject.name = N'IX_fn_codegeneration_run_SourceApplyRunId'
      AND
      (
          indexObject.is_unique = 1
          OR indexObject.is_disabled = 1
          OR
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal > 0
          ) <> 1
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 1
                AND columnObject.name = N'SourceApplyRunId'
          )
      )
)
BEGIN
    DROP INDEX IX_fn_codegeneration_run_SourceApplyRunId
        ON dbo.fn_codegeneration_run;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
      AND name = N'IX_fn_codegeneration_run_SourceApplyRunId'
)
BEGIN
    EXEC(N'
    CREATE INDEX IX_fn_codegeneration_run_SourceApplyRunId
        ON dbo.fn_codegeneration_run (SourceApplyRunId);
    ');
END;