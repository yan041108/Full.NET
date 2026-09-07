-- 209：岗位导入回执与岗位写入同事务；SQL Server 使用 UUID 非聚集主键和任务行唯一键。
IF OBJECT_ID(N'dbo.fn_organization_position_import', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_position_import (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        TaskId uniqueidentifier NOT NULL,
        LineNumber int NOT NULL,
        PayloadHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PositionId uniqueidentifier NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_organization_position_import PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_fn_organization_position_import_Tenant_Task_Line UNIQUE CLUSTERED (TenantId, TaskId, LineNumber)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_organization_position_import') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'岗位导入幂等回执', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_import';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_organization_position_import') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_import'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_import', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_organization_position_import') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_import'), N'TenantId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_import', @level2type=N'COLUMN', @level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_organization_position_import') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_import'), N'TaskId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'导入任务关联标识，不建立跨模块外键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_import', @level2type=N'COLUMN', @level2name=N'TaskId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_organization_position_import') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_import'), N'LineNumber', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始工作簿行号，从一开始', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_import', @level2type=N'COLUMN', @level2name=N'LineNumber';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_organization_position_import') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_import'), N'PayloadHash', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不可变导入行载荷 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_import', @level2type=N'COLUMN', @level2name=N'PayloadHash';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_organization_position_import') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_import'), N'PositionId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已完成的岗位标识，占位与业务写入同事务', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_import', @level2type=N'COLUMN', @level2name=N'PositionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_organization_position_import') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_import'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_import', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
