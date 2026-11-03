-- 申请单导入回执与业务行同事务提交；表和唯一约束在同一条 DDL 创建，重跑不丢失既有回执。
-- 不建立跨模块外键；SQL Server 显式声明 UUID v7 聚集主键，注释可独立恢复。
IF OBJECT_ID(N'dbo.demo_enterprise_request_import_row',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.demo_enterprise_request_import_row (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        TaskId uniqueidentifier NOT NULL,
        LineNumber int NOT NULL,
        Digest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RequestId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_demo_enterprise_request_import_row PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_demo_enterprise_request_import_row_TenantId_TaskId_LineNumber UNIQUE (TenantId,TaskId,LineNumber)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_import_row') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'企业申请导入行幂等回执',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_import_row';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_import_row') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_row'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用生成的 UUID v7 回执标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_import_row',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_import_row') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_row'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_import_row',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_import_row') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_row'),N'TaskId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'调度方持久化导入任务标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_import_row',@level2type=N'COLUMN',@level2name=N'TaskId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_import_row') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_row'),N'LineNumber','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源工作簿原始行号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_import_row',@level2type=N'COLUMN',@level2name=N'LineNumber';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_import_row') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_row'),N'Digest','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定源文件、主体和行内容的 SHA256 摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_import_row',@level2type=N'COLUMN',@level2name=N'Digest';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_import_row') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_row'),N'RequestId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'同事务创建的业务申请标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_import_row',@level2type=N'COLUMN',@level2name=N'RequestId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_import_row') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_import_row'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'回执提交时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_import_row',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
