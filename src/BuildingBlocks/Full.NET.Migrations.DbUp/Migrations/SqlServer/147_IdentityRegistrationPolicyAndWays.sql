-- 147：注册策略单例与租户注册方式主表。

IF OBJECT_ID(N'dbo.fn_identity_registration_policy', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_registration_policy
    (
        Id uniqueidentifier NOT NULL,
        IsPublicRegistrationEnabled bit NOT NULL
            CONSTRAINT DF_fn_identity_registration_policy_IsPublicRegistrationEnabled DEFAULT (0),
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_identity_registration_policy_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_registration_policy PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_policy')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证注册策略表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_policy';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_policy'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_policy', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_policy'), N'IsPublicRegistrationEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否开放公开注册', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_policy', @level2type=N'COLUMN', @level2name=N'IsPublicRegistrationEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_policy'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_policy', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_policy'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_policy', @level2type=N'COLUMN', @level2name=N'Version';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_policy')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户注册策略单例表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_policy';
END;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_registration_policy
    WHERE Id = '00000000-0000-4000-8000-000000000001'
)
    INSERT INTO dbo.fn_identity_registration_policy
        (Id, IsPublicRegistrationEnabled, UpdatedAtUtc, Version)
    VALUES
        ('00000000-0000-4000-8000-000000000001', 0, SYSDATETIMEOFFSET(), 1);

IF OBJECT_ID(N'dbo.fn_identity_user_registration_way', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_user_registration_way
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        Name nvarchar(128) NOT NULL,
        Code nvarchar(64) NOT NULL,
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_identity_user_registration_way_IsEnabled DEFAULT (1),
        RoleId uniqueidentifier NOT NULL,
        OrganizationUnitId uniqueidentifier NOT NULL,
        PositionId uniqueidentifier NULL,
        SortOrder int NOT NULL
            CONSTRAINT DF_fn_identity_user_registration_way_SortOrder DEFAULT (0),
        Remark nvarchar(256) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_identity_user_registration_way_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_user_registration_way PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_identity_user_registration_way_TenantId_Name UNIQUE (TenantId, Name),
        CONSTRAINT UX_fn_identity_user_registration_way_TenantId_Code UNIQUE (TenantId, Code),
        CONSTRAINT FK_fn_identity_user_registration_way_Role
            FOREIGN KEY (RoleId) REFERENCES dbo.fn_identity_role (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证用户注册方式表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'Code', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'Code';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'OrganizationUnitId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'机构单元标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'OrganizationUnitId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'PositionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'岗位标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'PositionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'Remark', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'备注', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'Remark';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'RoleId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'角色标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'RoleId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'SortOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'SortOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_registration_way'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way', @level2type=N'COLUMN', @level2name=N'Version';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户用户注册方式表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_registration_way';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_user_registration_way')
      AND indexObject.name = N'IX_fn_identity_user_registration_way_TenantId_SortOrder'
)
    CREATE INDEX IX_fn_identity_user_registration_way_TenantId_SortOrder
        ON dbo.fn_identity_user_registration_way(TenantId, SortOrder, Name, Id);
