-- 224: entitlement catalog, tenant bindings and enforcement phase.
IF OBJECT_ID(N'dbo.fn_tenancy_entitlement_catalog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_entitlement_catalog (
        Id uniqueidentifier NOT NULL,
        Code nvarchar(64) NOT NULL,
        Name nvarchar(128) NOT NULL,
        Description nvarchar(512) NULL,
        EntitlementType nvarchar(32) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_fn_tenancy_entitlement_catalog_IsActive DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_tenancy_entitlement_catalog_Version DEFAULT (1),
        CONSTRAINT PK_fn_tenancy_entitlement_catalog PRIMARY KEY NONCLUSTERED (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_tenancy_entitlement_catalog_Code' AND object_id = OBJECT_ID(N'dbo.fn_tenancy_entitlement_catalog'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_tenancy_entitlement_catalog_Code
        ON dbo.fn_tenancy_entitlement_catalog (Code);

IF OBJECT_ID(N'dbo.fn_tenancy_tenant_entitlement_binding', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_tenant_entitlement_binding (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        EntitlementId uniqueidentifier NOT NULL,
        EffectiveFromUtc datetimeoffset(7) NOT NULL,
        EffectiveToUtc datetimeoffset(7) NULL,
        SourcePackageId uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_tenancy_tenant_entitlement_binding_Version DEFAULT (1),
        CONSTRAINT PK_fn_tenancy_tenant_entitlement_binding PRIMARY KEY NONCLUSTERED (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_tenancy_tenant_entitlement_binding_Tenant' AND object_id = OBJECT_ID(N'dbo.fn_tenancy_tenant_entitlement_binding'))
    CREATE NONCLUSTERED INDEX IX_fn_tenancy_tenant_entitlement_binding_Tenant
        ON dbo.fn_tenancy_tenant_entitlement_binding (TenantId, EntitlementId);

IF OBJECT_ID(N'dbo.fn_tenancy_settings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_settings (
        Id uniqueidentifier NOT NULL,
        EntitlementEnforcementPhase nvarchar(32) NOT NULL
            CONSTRAINT DF_fn_tenancy_settings_EntitlementEnforcementPhase DEFAULT (N'Compatibility'),
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_tenancy_settings_Version DEFAULT (1),
        CONSTRAINT PK_fn_tenancy_settings PRIMARY KEY NONCLUSTERED (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.fn_tenancy_settings WHERE Id = '00000000-0000-4000-8000-000000000010')
    INSERT INTO dbo.fn_tenancy_settings (Id, EntitlementEnforcementPhase, UpdatedAtUtc, Version)
    VALUES ('00000000-0000-4000-8000-000000000010', N'Compatibility', SYSUTCDATETIME(), 1);
