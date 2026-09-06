-- 149：LDAP 连接配置主表。

IF OBJECT_ID(N'dbo.fn_identity_ldap_connection', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_ldap_connection
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        Name nvarchar(128) NOT NULL,
        Host nvarchar(256) NOT NULL,
        Port int NOT NULL
            CONSTRAINT DF_fn_identity_ldap_connection_Port DEFAULT (389),
        UseTls bit NOT NULL
            CONSTRAINT DF_fn_identity_ldap_connection_UseTls DEFAULT (0),
        BaseDn nvarchar(512) NOT NULL,
        BindDn nvarchar(512) NOT NULL,
        BindPasswordProtected nvarchar(max) NOT NULL,
        UserSearchFilter nvarchar(256) NOT NULL
            CONSTRAINT DF_fn_identity_ldap_connection_UserSearchFilter DEFAULT (N'(sAMAccountName={0})'),
        UserAccountAttribute nvarchar(128) NOT NULL
            CONSTRAINT DF_fn_identity_ldap_connection_UserAccountAttribute DEFAULT (N'sAMAccountName'),
        EmployeeIdAttribute nvarchar(128) NULL,
        DepartmentCodeAttribute nvarchar(128) NULL,
        SyncSearchBaseDn nvarchar(512) NOT NULL,
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_identity_ldap_connection_IsEnabled DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_identity_ldap_connection_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_ldap_connection PRIMARY KEY CLUSTERED (Id)
    );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'LDAP 目录连接配置表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
      AND indexObject.name = N'UX_fn_identity_ldap_connection_TenantId'
)
    CREATE UNIQUE INDEX UX_fn_identity_ldap_connection_TenantId
        ON dbo.fn_identity_ldap_connection(TenantId)
        WHERE TenantId IS NOT NULL;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
      AND indexObject.name = N'IX_fn_identity_ldap_connection_IsEnabled_Name'
)
    CREATE INDEX IX_fn_identity_ldap_connection_IsEnabled_Name
        ON dbo.fn_identity_ldap_connection(IsEnabled, Name, Id);
