IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'LogoFileId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD LogoFileId uniqueidentifier NULL;
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'SystemTitle') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD SystemTitle nvarchar(128) NULL;
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'ContactPhone') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD ContactPhone nvarchar(32) NULL;
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'ContactEmail') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD ContactEmail nvarchar(256) NULL;
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'ContactAddress') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD ContactAddress nvarchar(512) NULL;
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'Copyright') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD Copyright nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
      AND name = N'IX_fn_tenancy_tenant_LogoFileId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_fn_tenancy_tenant_LogoFileId
        ON dbo.fn_tenancy_tenant(LogoFileId)
        WHERE LogoFileId IS NOT NULL;
END;
