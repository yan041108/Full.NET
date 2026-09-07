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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证LDAP 连接表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'BaseDn', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'基础 DN', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'BaseDn';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'BindDn', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'绑定 DN', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'BindDn';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'BindPasswordProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的绑定密码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'BindPasswordProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'DepartmentCodeAttribute', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'部门编码属性', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'DepartmentCodeAttribute';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'EmployeeIdAttribute', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工号属性', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'EmployeeIdAttribute';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'Host', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Host', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'Host';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'Port', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'端口', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'Port';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'SyncSearchBaseDn', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'同步搜索基础 DN', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'SyncSearchBaseDn';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'UseTls', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否使用 TLS', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'UseTls';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'UserAccountAttribute', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户账号属性', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'UserAccountAttribute';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'UserSearchFilter', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户搜索过滤器', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'UserSearchFilter';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_ldap_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_ldap_connection'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_ldap_connection', @level2type=N'COLUMN', @level2name=N'Version';

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
