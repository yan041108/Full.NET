-- 035：租户职级目录（非聚集主键 + 租户创建时间聚集索引）。
IF OBJECT_ID(N'dbo.fn_organization_position_level', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_position_level
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        Code varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        DisplayOrder int NOT NULL,
        IsActive bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_organization_position_level_Version DEFAULT (1),
        CONSTRAINT PK_fn_organization_position_level PRIMARY KEY NONCLUSTERED (Id)
    );
    CREATE UNIQUE INDEX UX_fn_organization_position_level_Tenant_Code
        ON dbo.fn_organization_position_level(TenantId, Code);
    CREATE INDEX IX_fn_organization_position_level_Tenant_DisplayOrder
        ON dbo.fn_organization_position_level(TenantId, DisplayOrder, Code)
        WHERE IsActive = 1;
    CREATE CLUSTERED INDEX CX_fn_organization_position_level_Tenant_Created
        ON dbo.fn_organization_position_level(TenantId, CreatedAtUtc);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_position_level')
      AND name = N'DF_fn_organization_position_level_IsActive'
)
    ALTER TABLE dbo.fn_organization_position_level
        ADD CONSTRAINT DF_fn_organization_position_level_IsActive DEFAULT (1) FOR IsActive;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_organization_position_level')
      AND name = N'UX_fn_organization_position_level_Tenant_Code'
)
    CREATE UNIQUE INDEX UX_fn_organization_position_level_Tenant_Code
        ON dbo.fn_organization_position_level(TenantId, Code);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_organization_position_level')
      AND name = N'IX_fn_organization_position_level_Tenant_DisplayOrder'
)
    CREATE INDEX IX_fn_organization_position_level_Tenant_DisplayOrder
        ON dbo.fn_organization_position_level(TenantId, DisplayOrder, Code)
        WHERE IsActive = 1;
