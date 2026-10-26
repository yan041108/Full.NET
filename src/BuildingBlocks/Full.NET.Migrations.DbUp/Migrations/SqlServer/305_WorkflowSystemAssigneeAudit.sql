-- 系统选人裁定不能冒充某个人的意见；只有明确的系统步骤审计允许无操作人。
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ActorUserId' AND is_nullable=0)
    ALTER TABLE dbo.fn_workflow_domain_audit ALTER COLUMN ActorUserId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor')
    ALTER TABLE dbo.fn_workflow_domain_audit WITH CHECK ADD CONSTRAINT ck_fn_workflow_domain_audit_actor
        CHECK (ActorUserId IS NOT NULL OR (OperationKey='assignee.handle' AND ResourceTypeKey='step'));
GO
IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'人工操作人标识；系统选人裁定为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'ActorUserId';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工操作人标识；系统选人裁定为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'ActorUserId';
GO
