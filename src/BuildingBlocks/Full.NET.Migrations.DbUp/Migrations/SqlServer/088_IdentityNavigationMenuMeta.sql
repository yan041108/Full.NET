IF COL_LENGTH(N'dbo.fn_identity_navigation', N'MenuType') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ADD MenuType varchar(16) NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'菜单类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'MenuType';
END;

IF COL_LENGTH(N'dbo.fn_identity_navigation', N'Redirect') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ADD Redirect varchar(256) NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'重定向路径', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'Redirect';
END;

IF COL_LENGTH(N'dbo.fn_identity_navigation', N'LinkUrl') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ADD LinkUrl varchar(512) NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'外链地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'LinkUrl';
END;

IF COL_LENGTH(N'dbo.fn_identity_navigation', N'IsHidden') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ADD IsHidden bit NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否隐藏', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'IsHidden';
END;

IF COL_LENGTH(N'dbo.fn_identity_navigation', N'IsKeepAlive') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ADD IsKeepAlive bit NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否缓存页面', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'IsKeepAlive';
END;

IF COL_LENGTH(N'dbo.fn_identity_navigation', N'IsAffix') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ADD IsAffix bit NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否固定标签', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'IsAffix';
END;

IF COL_LENGTH(N'dbo.fn_identity_navigation', N'IsEmbedded') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ADD IsEmbedded bit NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否内嵌页面', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'IsEmbedded';
END;

IF COL_LENGTH(N'dbo.fn_identity_navigation', N'Remark') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ADD Remark nvarchar(500) NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'备注', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'Remark';
END;

EXEC(N'UPDATE dbo.fn_identity_navigation SET MenuType = ''menu'' WHERE MenuType IS NULL;');
EXEC(N'UPDATE dbo.fn_identity_navigation SET IsHidden = 0 WHERE IsHidden IS NULL;');
EXEC(N'UPDATE dbo.fn_identity_navigation SET IsKeepAlive = 0 WHERE IsKeepAlive IS NULL;');
EXEC(N'UPDATE dbo.fn_identity_navigation SET IsAffix = 0 WHERE IsAffix IS NULL;');
EXEC(N'UPDATE dbo.fn_identity_navigation SET IsEmbedded = 0 WHERE IsEmbedded IS NULL;');

IF EXISTS (
    SELECT 1
    FROM sys.columns AS columnObject
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_navigation'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'MenuType'
      AND columnObject.is_nullable = 1)
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ALTER COLUMN MenuType varchar(16) NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints AS defaultObject
    INNER JOIN sys.columns AS columnObject
        ON defaultObject.parent_object_id = columnObject.object_id
       AND defaultObject.parent_column_id = columnObject.column_id
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_navigation'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'MenuType')
BEGIN
    ALTER TABLE dbo.fn_identity_navigation
        ADD CONSTRAINT DF_fn_identity_navigation_MenuType DEFAULT ('menu') FOR MenuType;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns AS columnObject
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_navigation'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'IsHidden'
      AND columnObject.is_nullable = 1)
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ALTER COLUMN IsHidden bit NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints AS defaultObject
    INNER JOIN sys.columns AS columnObject
        ON defaultObject.parent_object_id = columnObject.object_id
       AND defaultObject.parent_column_id = columnObject.column_id
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_navigation'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'IsHidden')
BEGIN
    ALTER TABLE dbo.fn_identity_navigation
        ADD CONSTRAINT DF_fn_identity_navigation_IsHidden DEFAULT (0) FOR IsHidden;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns AS columnObject
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_navigation'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'IsKeepAlive'
      AND columnObject.is_nullable = 1)
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ALTER COLUMN IsKeepAlive bit NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints AS defaultObject
    INNER JOIN sys.columns AS columnObject
        ON defaultObject.parent_object_id = columnObject.object_id
       AND defaultObject.parent_column_id = columnObject.column_id
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_navigation'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'IsKeepAlive')
BEGIN
    ALTER TABLE dbo.fn_identity_navigation
        ADD CONSTRAINT DF_fn_identity_navigation_IsKeepAlive DEFAULT (0) FOR IsKeepAlive;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns AS columnObject
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_navigation'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'IsAffix'
      AND columnObject.is_nullable = 1)
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ALTER COLUMN IsAffix bit NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints AS defaultObject
    INNER JOIN sys.columns AS columnObject
        ON defaultObject.parent_object_id = columnObject.object_id
       AND defaultObject.parent_column_id = columnObject.column_id
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_navigation'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'IsAffix')
BEGIN
    ALTER TABLE dbo.fn_identity_navigation
        ADD CONSTRAINT DF_fn_identity_navigation_IsAffix DEFAULT (0) FOR IsAffix;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns AS columnObject
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_navigation'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'IsEmbedded'
      AND columnObject.is_nullable = 1)
BEGIN
    ALTER TABLE dbo.fn_identity_navigation ALTER COLUMN IsEmbedded bit NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints AS defaultObject
    INNER JOIN sys.columns AS columnObject
        ON defaultObject.parent_object_id = columnObject.object_id
       AND defaultObject.parent_column_id = columnObject.column_id
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_navigation'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'IsEmbedded')
BEGIN
    ALTER TABLE dbo.fn_identity_navigation
        ADD CONSTRAINT DF_fn_identity_navigation_IsEmbedded DEFAULT (0) FOR IsEmbedded;
END;
