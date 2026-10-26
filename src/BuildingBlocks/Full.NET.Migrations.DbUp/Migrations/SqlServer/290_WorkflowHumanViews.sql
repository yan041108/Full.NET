-- 290：固定人工活动视图；附件保留实际历史引用，并显式保存 claim 与租户资源归属。
IF COL_LENGTH(N'dbo.fn_workflow_form_submission_attachment', N'ClaimReferenceId') IS NULL
    ALTER TABLE dbo.fn_workflow_form_submission_attachment ADD ClaimReferenceId uniqueidentifier NULL;
GO
IF COL_LENGTH(N'dbo.fn_workflow_form_submission_attachment', N'OwnerResourceId') IS NULL
    ALTER TABLE dbo.fn_workflow_form_submission_attachment ADD OwnerResourceId uniqueidentifier NULL;
GO
UPDATE dbo.fn_workflow_form_submission_attachment SET ClaimReferenceId=SubmissionId WHERE ClaimReferenceId IS NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment') AND name=N'ClaimReferenceId' AND is_nullable=1)
    ALTER TABLE dbo.fn_workflow_form_submission_attachment ALTER COLUMN ClaimReferenceId uniqueidentifier NOT NULL;
GO
IF OBJECT_ID(N'dbo.fn_workflow_todo_view', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_todo_view (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        InputJson nvarchar(max) NOT NULL,
        InputRevision bigint NOT NULL,
        OutputJson nvarchar(max) NULL,
        OutputRevision bigint NULL,
        CONSTRAINT pk_fn_workflow_todo_view PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_todo_view_todo FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_todo (Id),
        CONSTRAINT fk_fn_workflow_todo_view_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance (Id)
    );
END;
GO
-- 只有当前活动的主路径输入可以证明；不把当前数据伪造成已完成待办的历史。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment') AND name=N'ix_fn_workflow_form_submission_attachment_claim')
    CREATE INDEX ix_fn_workflow_form_submission_attachment_claim ON dbo.fn_workflow_form_submission_attachment (ClaimReferenceId, FileId, TenantScopeKey);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment') AND name=N'ix_fn_workflow_form_submission_attachment_file_owner')
    CREATE INDEX ix_fn_workflow_form_submission_attachment_file_owner ON dbo.fn_workflow_form_submission_attachment (FileId, TenantScopeKey, InstanceId, OwnerResourceId);
GO
INSERT INTO dbo.fn_workflow_todo_view (Id, InstanceId, TenantScopeKey, InputJson, InputRevision)
SELECT todo.Id, todo.InstanceId, instance.TenantScopeKey, submission.SubmissionJson, submission.Revision
FROM dbo.fn_workflow_todo AS todo
INNER JOIN dbo.fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
INNER JOIN dbo.fn_workflow_step AS step ON step.Id=todo.StepId
INNER JOIN dbo.fn_workflow_form_submission AS submission ON submission.InstanceId=instance.Id
WHERE todo.StatusKey='active' AND step.ParallelJoinId IS NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.fn_workflow_todo_view AS existing WHERE existing.Id=todo.Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_view') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流本人活动输入输出视图', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_view';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_view'), N'Id', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键，复用待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_view', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_view'), N'InstanceId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_view', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_view'), N'TenantScopeKey', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信租户作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_view', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_view'), N'InputJson', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次人工激活的固定完整输入', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_view', @level2type=N'COLUMN', @level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_view'), N'InputRevision', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工激活时的表单修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_view', @level2type=N'COLUMN', @level2name=N'InputRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_view'), N'OutputJson', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本人办理成功时的固定完整输出', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_view', @level2type=N'COLUMN', @level2name=N'OutputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_view'), N'OutputRevision', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本人办理成功时的表单修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_view', @level2type=N'COLUMN', @level2name=N'OutputRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment'), N'ClaimReferenceId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Files 引用预留的明确归属身份', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission_attachment', @level2type=N'COLUMN', @level2name=N'ClaimReferenceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment'), N'OwnerResourceId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户文件所属资源身份，Host 文件为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission_attachment', @level2type=N'COLUMN', @level2name=N'OwnerResourceId';
GO
