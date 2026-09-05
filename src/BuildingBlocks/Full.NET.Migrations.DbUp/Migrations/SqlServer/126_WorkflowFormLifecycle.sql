-- 126：为工作流表单定义补充启停/归档状态与乐观并发版本列。

IF COL_LENGTH('dbo.fn_workflow_form_definition', 'StatusKey') IS NULL
BEGIN
    ALTER TABLE dbo.fn_workflow_form_definition
        ADD StatusKey varchar(16) NOT NULL
            CONSTRAINT DF_fn_workflow_form_definition_StatusKey DEFAULT ('active');
END

IF COL_LENGTH('dbo.fn_workflow_form_definition', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.fn_workflow_form_definition
        ADD Version bigint NOT NULL
            CONSTRAINT DF_fn_workflow_form_definition_Version DEFAULT (1);
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
      AND name = N'CK_fn_workflow_form_definition_StatusKey')
BEGIN
    ALTER TABLE dbo.fn_workflow_form_definition
        ADD CONSTRAINT CK_fn_workflow_form_definition_StatusKey
            CHECK (StatusKey IN ('active', 'disabled', 'archived'));
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
      AND name = N'CK_fn_workflow_form_definition_Version')
BEGIN
    ALTER TABLE dbo.fn_workflow_form_definition
        ADD CONSTRAINT CK_fn_workflow_form_definition_Version
            CHECK (Version > 0);
END
