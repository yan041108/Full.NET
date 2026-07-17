IF COL_LENGTH(N'dbo.fn_identity_user', N'PreferredLocale') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user ADD PreferredLocale varchar(35) NULL;
    EXEC(N'UPDATE dbo.fn_identity_user SET PreferredLocale = ''zh-CN'' WHERE PreferredLocale IS NULL;');
    EXEC(N'ALTER TABLE dbo.fn_identity_user ALTER COLUMN PreferredLocale varchar(35) NOT NULL;');
    EXEC(N'ALTER TABLE dbo.fn_identity_user ADD CONSTRAINT DF_fn_identity_user_PreferredLocale DEFAULT (''zh-CN'') FOR PreferredLocale;');
END;

IF COL_LENGTH(N'dbo.fn_identity_user', N'ProfileVersion') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user ADD ProfileVersion int NULL;
    EXEC(N'UPDATE dbo.fn_identity_user SET ProfileVersion = 1 WHERE ProfileVersion IS NULL;');
    EXEC(N'ALTER TABLE dbo.fn_identity_user ALTER COLUMN ProfileVersion int NOT NULL;');
    EXEC(N'ALTER TABLE dbo.fn_identity_user ADD CONSTRAINT DF_fn_identity_user_ProfileVersion DEFAULT (1) FOR ProfileVersion;');
END;

IF COL_LENGTH(N'dbo.fn_tenant_tenant', N'DefaultLocale') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenant_tenant ADD DefaultLocale varchar(35) NULL;
    EXEC(N'UPDATE dbo.fn_tenant_tenant SET DefaultLocale = ''zh-CN'' WHERE DefaultLocale IS NULL;');
    EXEC(N'ALTER TABLE dbo.fn_tenant_tenant ALTER COLUMN DefaultLocale varchar(35) NOT NULL;');
    EXEC(N'ALTER TABLE dbo.fn_tenant_tenant ADD CONSTRAINT DF_fn_tenant_tenant_DefaultLocale DEFAULT (''zh-CN'') FOR DefaultLocale;');
END;
