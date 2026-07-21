IF OBJECT_ID(N'dbo.fn_identity_navigation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_navigation
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(64) NOT NULL,
        ParentId uniqueidentifier NULL,
        RouteName varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Path varchar(256) NOT NULL,
        ComponentKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Title nvarchar(128) NOT NULL,
        Caption nvarchar(256) NOT NULL,
        Icon varchar(64) NOT NULL,
        DisplayOrder int NOT NULL,
        RequiredPermission varchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
        IsSystem bit NOT NULL,
        IsActive bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_identity_navigation_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_navigation PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_identity_navigation_Parent
            FOREIGN KEY (ParentId) REFERENCES dbo.fn_identity_navigation(Id)
    );
    CREATE UNIQUE INDEX UX_fn_identity_navigation_Scope_RouteName
        ON dbo.fn_identity_navigation(ScopeKey, RouteName)
        WHERE TenantId IS NULL;
    CREATE INDEX IX_fn_identity_navigation_Parent
        ON dbo.fn_identity_navigation(ParentId, DisplayOrder)
        WHERE IsActive = 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_identity_navigation')
      AND name = N'DF_fn_identity_navigation_IsSystem'
)
    ALTER TABLE dbo.fn_identity_navigation
        ADD CONSTRAINT DF_fn_identity_navigation_IsSystem DEFAULT (0) FOR IsSystem;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_identity_navigation')
      AND name = N'DF_fn_identity_navigation_IsActive'
)
    ALTER TABLE dbo.fn_identity_navigation
        ADD CONSTRAINT DF_fn_identity_navigation_IsActive DEFAULT (1) FOR IsActive;
