-- 127：工作流表单提交附件投影表，供 Files claim 探测与运行时授权校验。

IF OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_form_submission_attachment
    (
        Id uniqueidentifier NOT NULL,
        SubmissionId uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        FieldKey nvarchar(64) NOT NULL,
        FileId uniqueidentifier NOT NULL,
        TenantScopeKey nvarchar(128) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_workflow_form_submission_attachment PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_form_submission_attachment_Submission
            FOREIGN KEY (SubmissionId) REFERENCES dbo.fn_workflow_form_submission(Id),
        CONSTRAINT FK_fn_workflow_form_submission_attachment_Instance
            FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT UQ_fn_workflow_form_submission_attachment_SubmissionFieldFile
            UNIQUE (SubmissionId, FieldKey, FileId)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流表单提交附件表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission_attachment';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission_attachment', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment'), N'FieldKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'字段键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission_attachment', @level2type=N'COLUMN', @level2name=N'FieldKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment'), N'FileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission_attachment', @level2type=N'COLUMN', @level2name=N'FileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission_attachment', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment'), N'InstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission_attachment', @level2type=N'COLUMN', @level2name=N'InstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment'), N'SubmissionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'提交标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission_attachment', @level2type=N'COLUMN', @level2name=N'SubmissionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission_attachment', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';

    CREATE CLUSTERED INDEX IX_fn_workflow_form_submission_attachment_SubmissionId
        ON dbo.fn_workflow_form_submission_attachment (SubmissionId);

    CREATE NONCLUSTERED INDEX IX_fn_workflow_form_submission_attachment_FileId
        ON dbo.fn_workflow_form_submission_attachment (FileId);

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission_attachment')
          AND minor_id = 0
          AND name = N'MS_Description')
        EXEC sys.sp_addextendedproperty
            @name = N'MS_Description',
            @value = N'工作流表单提交附件投影表',
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE', @level1name = N'fn_workflow_form_submission_attachment';
END
