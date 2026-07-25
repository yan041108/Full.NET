-- 018：Host 租户套餐目录主数据；套餐与租户绑定在后续切片交付。
IF OBJECT_ID(N'dbo.fn_tenancy_tenant_package', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_tenant_package
    (
        Id uniqueidentifier NOT NULL,
        Code varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        Description nvarchar(512) NULL,
        IsActive bit NOT NULL
            CONSTRAINT DF_fn_tenancy_tenant_package_IsActive DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_tenancy_tenant_package_Version DEFAULT (1),
        CONSTRAINT PK_fn_tenancy_tenant_package PRIMARY KEY CLUSTERED (Id)
    );
    CREATE UNIQUE INDEX UX_fn_tenancy_tenant_package_Code
        ON dbo.fn_tenancy_tenant_package(Code);
END;
