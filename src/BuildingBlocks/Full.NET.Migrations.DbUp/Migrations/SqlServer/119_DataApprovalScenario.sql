-- 119：DataApproval 场景目录绑定表。
IF OBJECT_ID(N'dbo.fn_dataapproval_scenario', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_dataapproval_scenario
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ScenarioKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_fn_dataapproval_scenario_IsEnabled DEFAULT (0),
        WorkflowDefinitionKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        WorkflowDefinitionVersionId uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_dataapproval_scenario_Version DEFAULT (1),
        CONSTRAINT PK_fn_dataapproval_scenario PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_dataapproval_scenario_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_dataapproval_scenario_Version CHECK (Version > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据审批场景表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'ScenarioKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'场景键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'ScenarioKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'WorkflowDefinitionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流定义键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'WorkflowDefinitionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_scenario'), N'WorkflowDefinitionVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流定义版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario', @level2type=N'COLUMN', @level2name=N'WorkflowDefinitionVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据审批场景绑定表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
      AND name = N'UX_fn_dataapproval_scenario_ScenarioKey')
    CREATE UNIQUE CLUSTERED INDEX UX_fn_dataapproval_scenario_ScenarioKey
        ON dbo.fn_dataapproval_scenario (TenantScopeKey, ScenarioKey);
