-- 125：为工作流定义补充启停/归档状态列。



IF COL_LENGTH('dbo.fn_workflow_definition', 'StatusKey') IS NULL

BEGIN

    ALTER TABLE dbo.fn_workflow_definition

        ADD StatusKey varchar(16) NOT NULL

            CONSTRAINT DF_fn_workflow_definition_StatusKey DEFAULT ('active');

END



IF NOT EXISTS (

    SELECT 1

    FROM sys.check_constraints

    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_workflow_definition')

      AND name = N'CK_fn_workflow_definition_StatusKey')

BEGIN

    ALTER TABLE dbo.fn_workflow_definition

        ADD CONSTRAINT CK_fn_workflow_definition_StatusKey

            CHECK (StatusKey IN ('active', 'disabled', 'archived'));

END


