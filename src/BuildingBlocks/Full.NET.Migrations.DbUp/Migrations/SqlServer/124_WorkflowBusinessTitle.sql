-- 124：为工作流定义/版本与实例补充业务标题模板与启动快照列。
IF COL_LENGTH('dbo.fn_workflow_definition', 'BusinessTitleTemplate') IS NULL
    ALTER TABLE dbo.fn_workflow_definition
        ADD BusinessTitleTemplate NVARCHAR(256) NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'BusinessTitleTemplate', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务标题模板', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'BusinessTitleTemplate';

IF COL_LENGTH('dbo.fn_workflow_definition_version', 'BusinessTitleTemplate') IS NULL
    ALTER TABLE dbo.fn_workflow_definition_version
        ADD BusinessTitleTemplate NVARCHAR(256) NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_version'), N'BusinessTitleTemplate', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务标题模板', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version', @level2type=N'COLUMN', @level2name=N'BusinessTitleTemplate';

IF COL_LENGTH('dbo.fn_workflow_instance', 'BusinessTitle') IS NULL
    ALTER TABLE dbo.fn_workflow_instance
        ADD BusinessTitle NVARCHAR(256) NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'BusinessTitle', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'BusinessTitle';
