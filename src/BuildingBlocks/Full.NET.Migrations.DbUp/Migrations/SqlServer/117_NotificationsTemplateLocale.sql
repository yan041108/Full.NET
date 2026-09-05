-- 117：通知模板 BCP 47 语言变体；同一 TemplateKey 可按 LocaleTag 维护多份草稿与发布版本。
IF COL_LENGTH(N'dbo.fn_notifications_template', N'LocaleTag') IS NULL
    ALTER TABLE dbo.fn_notifications_template
        ADD LocaleTag varchar(35) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_notifications_template_LocaleTag DEFAULT ('zh-CN');

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'LocaleTag', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Locale Tag', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'LocaleTag';

IF COL_LENGTH(N'dbo.fn_notifications_template', N'DefaultLocaleTag') IS NULL
    ALTER TABLE dbo.fn_notifications_template
        ADD DefaultLocaleTag varchar(35) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_notifications_template_DefaultLocaleTag DEFAULT ('zh-CN');

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'DefaultLocaleTag', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Default Locale Tag', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'DefaultLocaleTag';

IF COL_LENGTH(N'dbo.fn_notifications_template_version', N'LocaleTag') IS NULL
    ALTER TABLE dbo.fn_notifications_template_version
        ADD LocaleTag varchar(35) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_notifications_template_version_LocaleTag DEFAULT ('zh-CN');

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'LocaleTag', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Locale Tag', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'LocaleTag';

EXEC sys.sp_executesql N'
UPDATE dbo.fn_notifications_template
SET LocaleTag = ''zh-CN'',
    DefaultLocaleTag = ''zh-CN''
WHERE LocaleTag IS NULL OR DefaultLocaleTag IS NULL;
';

EXEC sys.sp_executesql N'
UPDATE dbo.fn_notifications_template_version
SET LocaleTag = t.LocaleTag
FROM dbo.fn_notifications_template_version
INNER JOIN dbo.fn_notifications_template t ON t.Id = dbo.fn_notifications_template_version.TemplateId
WHERE dbo.fn_notifications_template_version.LocaleTag IS NULL
   OR dbo.fn_notifications_template_version.LocaleTag = '''';
';

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_notifications_template')
      AND name = N'UX_fn_notifications_template_Scope_Key')
    DROP INDEX UX_fn_notifications_template_Scope_Key ON dbo.fn_notifications_template;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_notifications_template')
      AND name = N'UX_fn_notifications_template_Scope_Key_Locale')
    CREATE UNIQUE INDEX UX_fn_notifications_template_Scope_Key_Locale
        ON dbo.fn_notifications_template(TenantScopeKey, TemplateKey, LocaleTag);
