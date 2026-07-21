IF OBJECT_ID(N'dbo.fn_organization_unit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_unit
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        ParentId uniqueidentifier NULL,
        Code varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        DisplayOrder int NOT NULL,
        IsActive bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_organization_unit_Version DEFAULT (1),
        CONSTRAINT PK_fn_organization_unit PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_organization_unit_Parent
            FOREIGN KEY (ParentId) REFERENCES dbo.fn_organization_unit(Id)
    );
    CREATE UNIQUE INDEX UX_fn_organization_unit_Tenant_Code
        ON dbo.fn_organization_unit(TenantId, Code);
    CREATE INDEX IX_fn_organization_unit_Tenant_Parent
        ON dbo.fn_organization_unit(TenantId, ParentId, DisplayOrder)
        WHERE IsActive = 1;
    CREATE CLUSTERED INDEX CX_fn_organization_unit_Tenant_Created
        ON dbo.fn_organization_unit(TenantId, CreatedAtUtc);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_unit')
      AND name = N'DF_fn_organization_unit_IsActive'
)
    ALTER TABLE dbo.fn_organization_unit
        ADD CONSTRAINT DF_fn_organization_unit_IsActive DEFAULT (1) FOR IsActive;
