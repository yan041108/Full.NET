-- 286：完整 VForm 设计资产独立于业务校验投影；创建式 DDL 支持未记账后重跑。
IF OBJECT_ID(N'dbo.fn_workflow_form_design_asset', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_form_design_asset (
        FormId uniqueidentifier NOT NULL,
        TenantScopeKey nvarchar(64) NOT NULL,
        SourceCommit varchar(40) NOT NULL,
        FormatVersion int NOT NULL,
        DesignerJson nvarchar(max) NOT NULL,
        ContentHash varchar(64) NOT NULL,
        CONSTRAINT pk_fn_workflow_form_design_asset PRIMARY KEY CLUSTERED (FormId),
        CONSTRAINT fk_fn_workflow_form_design_asset_owner FOREIGN KEY (FormId) REFERENCES dbo.fn_workflow_form_definition (Id) ON DELETE CASCADE
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_asset') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_asset'), N'FormId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单定义标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_asset', @level2type=N'COLUMN', @level2name=N'FormId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_asset') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_asset'), N'TenantScopeKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_asset', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_asset') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_asset'), N'SourceCommit', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定上游源码提交', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_asset', @level2type=N'COLUMN', @level2name=N'SourceCommit';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_asset') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_asset'), N'FormatVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原版 JSON 格式版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_asset', @level2type=N'COLUMN', @level2name=N'FormatVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_asset') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_asset'), N'DesignerJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整原版设计 JSON', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_asset', @level2type=N'COLUMN', @level2name=N'DesignerJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_asset') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_asset'), N'ContentHash', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整设计内容 SHA256', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_asset', @level2type=N'COLUMN', @level2name=N'ContentHash';
GO
IF OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_form_design_snapshot (
        FormVersionId uniqueidentifier NOT NULL,
        SourceCommit varchar(40) NOT NULL,
        FormatVersion int NOT NULL,
        DesignerJson nvarchar(max) NOT NULL,
        DesignHash varchar(64) NOT NULL,
        RuntimeVersion varchar(64) NOT NULL,
        BindingJson nvarchar(max) NOT NULL,
        CONSTRAINT pk_fn_workflow_form_design_snapshot PRIMARY KEY CLUSTERED (FormVersionId),
        CONSTRAINT fk_fn_workflow_form_design_snapshot_owner FOREIGN KEY (FormVersionId) REFERENCES dbo.fn_workflow_form_version (Id) ON DELETE CASCADE
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot'), N'FormVersionId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不可变表单版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_snapshot', @level2type=N'COLUMN', @level2name=N'FormVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot'), N'SourceCommit', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定上游源码提交', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_snapshot', @level2type=N'COLUMN', @level2name=N'SourceCommit';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot'), N'FormatVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'设计 JSON 格式版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_snapshot', @level2type=N'COLUMN', @level2name=N'FormatVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot'), N'DesignerJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整不可变设计 JSON', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_snapshot', @level2type=N'COLUMN', @level2name=N'DesignerJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot'), N'DesignHash', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'设计内容摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_snapshot', @level2type=N'COLUMN', @level2name=N'DesignHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot'), N'RuntimeVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际渲染产物 SHA256', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_snapshot', @level2type=N'COLUMN', @level2name=N'RuntimeVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot'), N'BindingJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时固化的组件绑定及运行目标', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_snapshot', @level2type=N'COLUMN', @level2name=N'BindingJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_asset') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流完整原版设计草稿资产', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_asset';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_form_design_snapshot') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流不可变原版设计发布快照', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_design_snapshot';
GO
