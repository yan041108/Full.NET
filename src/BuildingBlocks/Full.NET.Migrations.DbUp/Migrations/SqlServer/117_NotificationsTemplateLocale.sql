-- 117：通知模板 BCP 47 语言变体；同一 TemplateKey 可按 LocaleTag 维护多份草稿与发布版本。
IF COL_LENGTH(N'dbo.fn_notifications_template', N'LocaleTag') IS NULL
    ALTER TABLE dbo.fn_notifications_template
        ADD LocaleTag varchar(35) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_notifications_template_LocaleTag DEFAULT ('zh-CN');

IF COL_LENGTH(N'dbo.fn_notifications_template', N'DefaultLocaleTag') IS NULL
    ALTER TABLE dbo.fn_notifications_template
        ADD DefaultLocaleTag varchar(35) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_notifications_template_DefaultLocaleTag DEFAULT ('zh-CN');

IF COL_LENGTH(N'dbo.fn_notifications_template_version', N'LocaleTag') IS NULL
    ALTER TABLE dbo.fn_notifications_template_version
        ADD LocaleTag varchar(35) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_notifications_template_version_LocaleTag DEFAULT ('zh-CN');

EXEC sys.sp_executesql N'
UPDATE dbo.fn_notifications_template
SET LocaleTag = ''zh-CN'',
    DefaultLocaleTag = ''zh-CN''
WHERE LocaleTag IS NULL OR DefaultLocaleTag IS NULL;
';

EXEC sys.sp_executesql N'
UPDATE v
SET v.LocaleTag = t.LocaleTag
FROM dbo.fn_notifications_template_version v
INNER JOIN dbo.fn_notifications_template t ON t.Id = v.TemplateId
WHERE v.LocaleTag IS NULL OR v.LocaleTag = '''';
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
