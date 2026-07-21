IF COL_LENGTH(N'dbo.fn_identity_role', N'DataScopeKind') IS NULL
    ALTER TABLE dbo.fn_identity_role
        ADD DataScopeKind varchar(64) NOT NULL
            CONSTRAINT DF_fn_identity_role_DataScopeKind
            DEFAULT ('identity.data_scope.all');

IF OBJECT_ID(N'dbo.fn_identity_role_data_scope_unit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_role_data_scope_unit
    (
        RoleId uniqueidentifier NOT NULL,
        UnitId uniqueidentifier NOT NULL,
        CONSTRAINT PK_fn_identity_role_data_scope_unit
            PRIMARY KEY NONCLUSTERED (RoleId, UnitId),
        CONSTRAINT FK_fn_identity_role_data_scope_unit_Role
            FOREIGN KEY (RoleId) REFERENCES dbo.fn_identity_role(Id),
        CONSTRAINT FK_fn_identity_role_data_scope_unit_Unit
            FOREIGN KEY (UnitId) REFERENCES dbo.fn_organization_unit(Id)
    );
    CREATE CLUSTERED INDEX CX_fn_identity_role_data_scope_unit_Role
        ON dbo.fn_identity_role_data_scope_unit(RoleId);
END;

UPDATE dbo.fn_identity_role
SET DataScopeKind = 'identity.data_scope.all'
WHERE DataScopeKind IS NULL OR LTRIM(RTRIM(DataScopeKind)) = '';
