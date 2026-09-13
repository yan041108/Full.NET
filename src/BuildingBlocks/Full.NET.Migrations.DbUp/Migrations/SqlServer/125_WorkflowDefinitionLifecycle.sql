-- 125：为工作流定义补充启停/归档状态列。



IF COL_LENGTH('dbo.fn_workflow_definition', 'StatusKey') IS NULL

BEGIN

    ALTER TABLE dbo.fn_workflow_definition

        ADD StatusKey varchar(16) NOT NULL

            CONSTRAINT DF_fn_workflow_definition_StatusKey DEFAULT ('active');


    IF NOT EXISTS (


        SELECT 1


        FROM sys.extended_properties


        WHERE class = 1


          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')


          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'StatusKey', 'ColumnId')


          AND name = N'MS_Description'


    )


        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'StatusKey';

END;

GO

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


