IF OBJECT_ID(N'dbo.fn_organization_user_unit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_user_unit
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        UnitId uniqueidentifier NOT NULL,
        IsPrimary bit NOT NULL,
        IsActive bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_organization_user_unit_Version DEFAULT (1),
        CONSTRAINT PK_fn_organization_user_unit PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_organization_user_unit_Unit
            FOREIGN KEY (UnitId) REFERENCES dbo.fn_organization_unit(Id)
    );
    CREATE UNIQUE INDEX UX_fn_organization_user_unit_Tenant_User_Unit
        ON dbo.fn_organization_user_unit(TenantId, UserId, UnitId);
    CREATE INDEX IX_fn_organization_user_unit_Tenant_User
        ON dbo.fn_organization_user_unit(TenantId, UserId, IsPrimary)
        WHERE IsActive = 1;
    CREATE INDEX IX_fn_organization_user_unit_Tenant_Unit
        ON dbo.fn_organization_user_unit(TenantId, UnitId)
        WHERE IsActive = 1;
    CREATE CLUSTERED INDEX CX_fn_organization_user_unit_Tenant_Created
        ON dbo.fn_organization_user_unit(TenantId, CreatedAtUtc);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
      AND name = N'DF_fn_organization_user_unit_IsPrimary'
)
    ALTER TABLE dbo.fn_organization_user_unit
        ADD CONSTRAINT DF_fn_organization_user_unit_IsPrimary DEFAULT (0) FOR IsPrimary;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
      AND name = N'DF_fn_organization_user_unit_IsActive'
)
    ALTER TABLE dbo.fn_organization_user_unit
        ADD CONSTRAINT DF_fn_organization_user_unit_IsActive DEFAULT (1) FOR IsActive;
