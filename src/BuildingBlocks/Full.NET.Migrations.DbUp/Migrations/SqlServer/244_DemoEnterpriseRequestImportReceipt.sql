-- 244：企业申请导入回执与企业申请写入同事务；SQL Server 使用 UUID 非聚集主键和任务行唯一键。
IF OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.demo_enterprise_request_import_receipt (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        TaskId uniqueidentifier NOT NULL,
        LineNumber int NOT NULL,
        PayloadHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        EntityId uniqueidentifier NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_demo_enterprise_request_import_receipt PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_demo_enterprise_request_import_receipt_Tenant_Task_Line UNIQUE CLUSTERED (TenantId, TaskId, LineNumber)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'企业申请导入幂等回执', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_import_receipt';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_import_receipt', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt'), N'TenantId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_import_receipt', @level2type=N'COLUMN', @level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt'), N'TaskId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'导入任务关联标识，不建立跨模块外键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_import_receipt', @level2type=N'COLUMN', @level2name=N'TaskId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt'), N'LineNumber', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始工作簿行号，从一开始', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_import_receipt', @level2type=N'COLUMN', @level2name=N'LineNumber';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt'), N'PayloadHash', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不可变导入行载荷 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_import_receipt', @level2type=N'COLUMN', @level2name=N'PayloadHash';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt'), N'EntityId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已完成的企业申请标识，占位与业务写入同事务', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_import_receipt', @level2type=N'COLUMN', @level2name=N'EntityId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_receipt'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_import_receipt', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
