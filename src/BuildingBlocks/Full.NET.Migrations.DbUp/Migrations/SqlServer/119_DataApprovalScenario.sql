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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据审批场景绑定表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_scenario';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')
      AND name = N'UX_fn_dataapproval_scenario_ScenarioKey')
    CREATE UNIQUE CLUSTERED INDEX UX_fn_dataapproval_scenario_ScenarioKey
        ON dbo.fn_dataapproval_scenario (TenantScopeKey, ScenarioKey);
