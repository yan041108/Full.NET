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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证导航菜单表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'Caption';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'组件键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'ComponentKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'DisplayOrder';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'图标', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'Icon';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'IsActive';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否系统内置', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'IsSystem';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'父级标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'ParentId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'路由路径', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'Path';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所需权限码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'RequiredPermission';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'路由名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'RouteName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'Title';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_navigation', @level2type=N'COLUMN', @level2name=N'Version';
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
