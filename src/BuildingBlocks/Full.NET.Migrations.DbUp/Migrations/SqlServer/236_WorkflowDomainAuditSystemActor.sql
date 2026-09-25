-- 236：系统自动恢复没有用户操作者，允许工作流领域审计保留空操作者。
IF COL_LENGTH(N'dbo.fn_workflow_domain_audit', N'ActorUserId') IS NULL
    THROW 51236, 'Workflow domain audit ActorUserId column is missing.', 1;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
      AND name = N'ActorUserId'
      AND is_nullable = 0)
    ALTER TABLE dbo.fn_workflow_domain_audit ALTER COLUMN ActorUserId uniqueidentifier NULL;
