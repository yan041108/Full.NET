-- 026：租户用户-职位隶属（非聚集主键 + 租户创建时间聚集索引）。
IF OBJECT_ID(N'dbo.fn_organization_user_position', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_user_position
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        PositionId uniqueidentifier NOT NULL,
        IsPrimary bit NOT NULL,
        IsActive bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_organization_user_position_Version DEFAULT (1),
        CONSTRAINT PK_fn_organization_user_position PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_organization_user_position_Position
            FOREIGN KEY (PositionId) REFERENCES dbo.fn_organization_position(Id)
    );
    CREATE UNIQUE INDEX UX_fn_organization_user_position_Tenant_User_Position
        ON dbo.fn_organization_user_position(TenantId, UserId, PositionId);
    CREATE INDEX IX_fn_organization_user_position_Tenant_User
        ON dbo.fn_organization_user_position(TenantId, UserId, IsPrimary)
        WHERE IsActive = 1;
    CREATE INDEX IX_fn_organization_user_position_Tenant_Position
        ON dbo.fn_organization_user_position(TenantId, PositionId)
        WHERE IsActive = 1;
    CREATE CLUSTERED INDEX CX_fn_organization_user_position_Tenant_Created
        ON dbo.fn_organization_user_position(TenantId, CreatedAtUtc);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_user_position')
      AND name = N'DF_fn_organization_user_position_IsPrimary'
)
    ALTER TABLE dbo.fn_organization_user_position
        ADD CONSTRAINT DF_fn_organization_user_position_IsPrimary DEFAULT (0) FOR IsPrimary;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_user_position')
      AND name = N'DF_fn_organization_user_position_IsActive'
)
    ALTER TABLE dbo.fn_organization_user_position
        ADD CONSTRAINT DF_fn_organization_user_position_IsActive DEFAULT (1) FOR IsActive;
