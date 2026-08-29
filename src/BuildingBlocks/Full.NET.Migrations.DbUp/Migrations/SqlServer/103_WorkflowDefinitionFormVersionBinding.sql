-- 103：补齐已发布流程定义对不可变表单版本的强制绑定。
IF COL_LENGTH(N'dbo.fn_workflow_definition_version', N'FormVersionId') IS NULL
    ALTER TABLE dbo.fn_workflow_definition_version ADD FormVersionId uniqueidentifier NULL;

-- 102 尚未开放定义发布 API；若环境存在绕过应用写入的历史版本，必须人工确认绑定而不是猜测回填。
EXEC(N'
    IF EXISTS (SELECT 1 FROM dbo.fn_workflow_definition_version WHERE FormVersionId IS NULL)
        THROW 51030, ''Workflow definition versions require an explicit FormVersionId before migration 103 can continue.'', 1;
');

EXEC(N'ALTER TABLE dbo.fn_workflow_definition_version ALTER COLUMN FormVersionId uniqueidentifier NOT NULL;');

IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
      AND minor_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.fn_workflow_definition_version'),
          N'FormVersionId',
          'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定绑定的表单版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version', @level2type=N'COLUMN', @level2name=N'FormVersionId';

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
      AND name = N'FK_fn_workflow_definition_version_FormVersion')
    EXEC(N'
        ALTER TABLE dbo.fn_workflow_definition_version
            ADD CONSTRAINT FK_fn_workflow_definition_version_FormVersion
            FOREIGN KEY (FormVersionId) REFERENCES dbo.fn_workflow_form_version(Id);
    ');
