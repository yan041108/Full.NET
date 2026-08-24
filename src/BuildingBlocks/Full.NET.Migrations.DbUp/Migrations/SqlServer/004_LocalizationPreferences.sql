-- 本迁移允许在 SQL Server 非事务 DDL 部分成功但 DbUp 尚未记账后重跑。
-- 每个列、回填、空值约束和默认约束独立收敛，避免存在列时跳过后续修复。
IF COL_LENGTH(N'dbo.fn_identity_user', N'PreferredLocale') IS NULL
    ALTER TABLE dbo.fn_identity_user ADD PreferredLocale varchar(35) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'PreferredLocale', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'首选语言区域', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'PreferredLocale';

EXEC(N'UPDATE dbo.fn_identity_user SET PreferredLocale = ''zh-CN'' WHERE PreferredLocale IS NULL;');

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_identity_user')
      AND name = N'PreferredLocale'
      AND is_nullable = 1
)
    EXEC(N'ALTER TABLE dbo.fn_identity_user ALTER COLUMN PreferredLocale varchar(35) NOT NULL;');

DECLARE @preferredDefaultName sysname;
DECLARE @preferredDefaultDefinition nvarchar(4000);
DECLARE @dropDefaultSql nvarchar(max);
SELECT @preferredDefaultName = defaultObject.name,
       @preferredDefaultDefinition = defaultObject.definition
FROM sys.default_constraints AS defaultObject
INNER JOIN sys.columns AS columnObject
    ON columnObject.object_id = defaultObject.parent_object_id
   AND columnObject.column_id = defaultObject.parent_column_id
WHERE defaultObject.parent_object_id = OBJECT_ID(N'dbo.fn_identity_user')
  AND columnObject.name = N'PreferredLocale';

IF @preferredDefaultName IS NOT NULL
   AND
   (
       @preferredDefaultName <> N'DF_fn_identity_user_PreferredLocale'
       OR @preferredDefaultDefinition NOT LIKE N'%zh-CN%'
   )
BEGIN
    SET @dropDefaultSql = N'ALTER TABLE dbo.fn_identity_user DROP CONSTRAINT '
        + QUOTENAME(@preferredDefaultName) + N';';
    EXEC sys.sp_executesql @dropDefaultSql;
    SET @preferredDefaultName = NULL;
END;

IF @preferredDefaultName IS NULL
    ALTER TABLE dbo.fn_identity_user
        ADD CONSTRAINT DF_fn_identity_user_PreferredLocale
        DEFAULT ('zh-CN') FOR PreferredLocale;

IF COL_LENGTH(N'dbo.fn_identity_user', N'ProfileVersion') IS NULL
    ALTER TABLE dbo.fn_identity_user ADD ProfileVersion int NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'ProfileVersion', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'资料版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'ProfileVersion';

EXEC(N'UPDATE dbo.fn_identity_user SET ProfileVersion = 1 WHERE ProfileVersion IS NULL;');

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_identity_user')
      AND name = N'ProfileVersion'
      AND is_nullable = 1
)
    EXEC(N'ALTER TABLE dbo.fn_identity_user ALTER COLUMN ProfileVersion int NOT NULL;');

DECLARE @profileDefaultName sysname;
DECLARE @profileDefaultDefinition nvarchar(4000);
SELECT @profileDefaultName = defaultObject.name,
       @profileDefaultDefinition = defaultObject.definition
FROM sys.default_constraints AS defaultObject
INNER JOIN sys.columns AS columnObject
    ON columnObject.object_id = defaultObject.parent_object_id
   AND columnObject.column_id = defaultObject.parent_column_id
WHERE defaultObject.parent_object_id = OBJECT_ID(N'dbo.fn_identity_user')
  AND columnObject.name = N'ProfileVersion';

IF @profileDefaultName IS NOT NULL
   AND
   (
       @profileDefaultName <> N'DF_fn_identity_user_ProfileVersion'
       OR REPLACE(REPLACE(@profileDefaultDefinition, N'(', N''), N')', N'') <> N'1'
   )
BEGIN
    SET @dropDefaultSql = N'ALTER TABLE dbo.fn_identity_user DROP CONSTRAINT '
        + QUOTENAME(@profileDefaultName) + N';';
    EXEC sys.sp_executesql @dropDefaultSql;
    SET @profileDefaultName = NULL;
END;

IF @profileDefaultName IS NULL
    ALTER TABLE dbo.fn_identity_user
        ADD CONSTRAINT DF_fn_identity_user_ProfileVersion
        DEFAULT (1) FOR ProfileVersion;

IF COL_LENGTH(N'dbo.fn_tenant_tenant', N'DefaultLocale') IS NULL
    ALTER TABLE dbo.fn_tenant_tenant ADD DefaultLocale varchar(35) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenant_tenant'), N'DefaultLocale', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'默认语言区域', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenant_tenant', @level2type=N'COLUMN', @level2name=N'DefaultLocale';

EXEC(N'UPDATE dbo.fn_tenant_tenant SET DefaultLocale = ''zh-CN'' WHERE DefaultLocale IS NULL;');

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
      AND name = N'DefaultLocale'
      AND is_nullable = 1
)
    EXEC(N'ALTER TABLE dbo.fn_tenant_tenant ALTER COLUMN DefaultLocale varchar(35) NOT NULL;');

DECLARE @tenantDefaultName sysname;
DECLARE @tenantDefaultDefinition nvarchar(4000);
SELECT @tenantDefaultName = defaultObject.name,
       @tenantDefaultDefinition = defaultObject.definition
FROM sys.default_constraints AS defaultObject
INNER JOIN sys.columns AS columnObject
    ON columnObject.object_id = defaultObject.parent_object_id
   AND columnObject.column_id = defaultObject.parent_column_id
WHERE defaultObject.parent_object_id = OBJECT_ID(N'dbo.fn_tenant_tenant')
  AND columnObject.name = N'DefaultLocale';

IF @tenantDefaultName IS NOT NULL
   AND
   (
       @tenantDefaultName <> N'DF_fn_tenant_tenant_DefaultLocale'
       OR @tenantDefaultDefinition NOT LIKE N'%zh-CN%'
   )
BEGIN
    SET @dropDefaultSql = N'ALTER TABLE dbo.fn_tenant_tenant DROP CONSTRAINT '
        + QUOTENAME(@tenantDefaultName) + N';';
    EXEC sys.sp_executesql @dropDefaultSql;
    SET @tenantDefaultName = NULL;
END;

IF @tenantDefaultName IS NULL
    ALTER TABLE dbo.fn_tenant_tenant
        ADD CONSTRAINT DF_fn_tenant_tenant_DefaultLocale
        DEFAULT ('zh-CN') FOR DefaultLocale;
