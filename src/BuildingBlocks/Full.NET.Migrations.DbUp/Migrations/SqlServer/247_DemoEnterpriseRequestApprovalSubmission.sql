-- 247：提交状态、固定流程身份与 Outbox 同事务；仅创建本模块表，不猜测历史流程绑定。
IF OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.demo_enterprise_request_approval_submission (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        RequestId uniqueidentifier NOT NULL,
        RequestVersion bigint NOT NULL,
        WorkflowDefinitionVersionId uniqueidentifier NOT NULL,
        WorkflowInstanceId uniqueidentifier NOT NULL,
        SubmittedById uniqueidentifier NOT NULL,
        OrganizationUnitId uniqueidentifier NOT NULL,
        BusinessTitle nvarchar(200) NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        StartedAtUtc datetime2(6) NULL,
        FinalStatus varchar(32) NULL,
        CompletedAtUtc datetime2(6) NULL,
        LastMessageId uniqueidentifier NULL,
        CONSTRAINT PK_demo_enterprise_request_approval_submission PRIMARY KEY NONCLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND name = N'UX_demo_enterprise_request_approval_submission_Tenant_Request')
    CREATE UNIQUE CLUSTERED INDEX UX_demo_enterprise_request_approval_submission_Tenant_Request ON dbo.demo_enterprise_request_approval_submission (TenantId, RequestId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND name = N'UX_demo_enterprise_request_approval_submission_Instance')
    CREATE UNIQUE NONCLUSTERED INDEX UX_demo_enterprise_request_approval_submission_Instance ON dbo.demo_enterprise_request_approval_submission (WorkflowInstanceId);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'企业申请可靠审批提交日志', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'TenantId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'RequestId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属企业申请标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'RequestId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'RequestVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'提交时固定的申请版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'RequestVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'WorkflowDefinitionVersionId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定流程定义版本，不建立跨模块外键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'WorkflowDefinitionVersionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'WorkflowInstanceId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预分配的流程实例标识，不建立跨模块外键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'WorkflowInstanceId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'SubmittedById', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'提交人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'SubmittedById';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'OrganizationUnitId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'提交时固定的机构单元标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'OrganizationUnitId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'BusinessTitle', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'提交时固定的业务标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'BusinessTitle';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'StartedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程启动回执时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'FinalStatus', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已封存的审批终态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'FinalStatus';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'CompletedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'终态回写时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_submission'), N'LastMessageId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'终态事件消息标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_submission', @level2type=N'COLUMN', @level2name=N'LastMessageId';
