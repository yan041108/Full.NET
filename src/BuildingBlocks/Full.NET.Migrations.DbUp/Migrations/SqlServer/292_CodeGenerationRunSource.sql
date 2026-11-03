-- 292：来源可以是冻结内联 Schema；模板来源仍须成对提供标识与正版本。
-- 两个约束在同一事务中替换，校验失败时恢复原约束，不改写已有业务行。
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CK_fn_codegeneration_run_Template', N'C') IS NOT NULL
    ALTER TABLE dbo.fn_codegeneration_run DROP CONSTRAINT CK_fn_codegeneration_run_Template;
IF OBJECT_ID(N'dbo.CK_fn_codegeneration_run_ApplyTemplate', N'C') IS NOT NULL
    ALTER TABLE dbo.fn_codegeneration_run DROP CONSTRAINT CK_fn_codegeneration_run_ApplyTemplate;
ALTER TABLE dbo.fn_codegeneration_run WITH CHECK ADD
    CONSTRAINT CK_fn_codegeneration_run_Template CHECK
    (
        (TemplateId IS NULL AND TemplateVersion IS NULL)
        OR (TemplateId IS NOT NULL AND TemplateVersion IS NOT NULL AND TemplateVersion > 0)
    ),
    CONSTRAINT CK_fn_codegeneration_run_ApplyTemplate CHECK
    (
        (OperationKind IN ('preview', 'apply') AND SourceApplyRunId IS NULL
            AND (OperationKind = 'apply' OR Status <> 'running'))
        OR (OperationKind = 'rollback' AND SourceApplyRunId IS NOT NULL
            AND TemplateId IS NULL AND TemplateVersion IS NULL)
    );
COMMIT TRANSACTION;
