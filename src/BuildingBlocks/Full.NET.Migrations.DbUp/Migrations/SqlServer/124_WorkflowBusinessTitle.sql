-- 124：为工作流定义/版本与实例补充业务标题模板与启动快照列。
IF COL_LENGTH('dbo.fn_workflow_definition', 'BusinessTitleTemplate') IS NULL
    ALTER TABLE dbo.fn_workflow_definition
        ADD BusinessTitleTemplate NVARCHAR(256) NULL;

IF COL_LENGTH('dbo.fn_workflow_definition_version', 'BusinessTitleTemplate') IS NULL
    ALTER TABLE dbo.fn_workflow_definition_version
        ADD BusinessTitleTemplate NVARCHAR(256) NULL;

IF COL_LENGTH('dbo.fn_workflow_instance', 'BusinessTitle') IS NULL
    ALTER TABLE dbo.fn_workflow_instance
        ADD BusinessTitle NVARCHAR(256) NULL;
