-- 查询方案为偏好事实；不保存业务结果，无跨模块外键。逐表创建包含完整索引，支持未记账重跑。
IF OBJECT_ID(N'dbo.fn_settings_query_scheme',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_settings_query_scheme (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NULL,
    ScopeKey nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
    GridKey nvarchar(128) NOT NULL,
    OwnerUserId uniqueidentifier NOT NULL,
    Name nvarchar(64) NOT NULL,
    SchemaVersion int NOT NULL,
    ConfigurationJson nvarchar(max) NOT NULL,
    IsShared bit NOT NULL,
    CreatedAtUtc datetime2(6) NOT NULL,
    UpdatedAtUtc datetime2(6) NULL,
    Version bigint NOT NULL,
    CONSTRAINT PK_fn_settings_query_scheme PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT CK_fn_settings_query_scheme_Scope CHECK ((TenantId IS NULL AND ScopeKey='host') OR (TenantId IS NOT NULL AND ScopeKey=CONCAT('tenant:',LOWER(REPLACE(CONVERT(char(36),TenantId),'-',''))))),
    CONSTRAINT CK_fn_settings_query_scheme_Version CHECK (Version>0),
    CONSTRAINT CK_fn_settings_query_scheme_Shared CHECK (IsShared=0 OR TenantId IS NOT NULL),
    CONSTRAINT CK_fn_settings_query_scheme_Config CHECK (DATALENGTH(ConfigurationJson)<=32768),
    INDEX IX_fn_settings_query_scheme_Owner_Created NONCLUSTERED (ScopeKey,GridKey,OwnerUserId,CreatedAtUtc DESC,Id DESC),
    INDEX IX_fn_settings_query_scheme_Shared_Created NONCLUSTERED (ScopeKey,GridKey,IsShared,CreatedAtUtc DESC,Id DESC)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'按可信用户和Host或租户上下文隔离的受控查询方案',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户；Host上下文为空',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'ScopeKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信作用域键；Host为host，租户为tenant加冒号及无连字符UUID',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'ScopeKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'GridKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'业务所有者发布的稳定Grid键',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'GridKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'OwnerUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变创建主体',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'OwnerUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'Name','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建者定义的有界方案名',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'Name';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'SchemaVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'保存时的业务查询Schema版本',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'SchemaVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'ConfigurationJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'有界筛选排序和展示配置，不含查询结果',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'ConfigurationJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'IsShared','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'仅当前租户可共享；Host必须为否',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'IsShared';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最后修改UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'正整数并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme',@level2type=N'COLUMN',@level2name=N'Version';
GO
IF OBJECT_ID(N'dbo.fn_settings_query_scheme_default',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_settings_query_scheme_default (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NULL,
    ScopeKey nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
    GridKey nvarchar(128) NOT NULL,
    UserId uniqueidentifier NOT NULL,
    DefaultSchemeId uniqueidentifier NULL,
    Version bigint NOT NULL,
    CreatedAtUtc datetime2(6) NOT NULL,
    CONSTRAINT PK_fn_settings_query_scheme_default PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT CK_fn_settings_query_scheme_default_Scope CHECK ((TenantId IS NULL AND ScopeKey='host') OR (TenantId IS NOT NULL AND ScopeKey=CONCAT('tenant:',LOWER(REPLACE(CONVERT(char(36),TenantId),'-',''))))),
    CONSTRAINT CK_fn_settings_query_scheme_default_Version CHECK (Version>=0),
    CONSTRAINT UX_fn_settings_query_scheme_default_User_Scope_Grid UNIQUE (UserId,ScopeKey,GridKey)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme_default') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'每用户上下文与Grid的查询方案默认及写入互斥记录',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme_default';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme_default') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme_default'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme_default',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme_default') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme_default'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户；Host上下文为空',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme_default',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme_default') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme_default'),N'ScopeKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信作用域键；Host为host，租户为tenant加冒号及无连字符UUID',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme_default',@level2type=N'COLUMN',@level2name=N'ScopeKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme_default') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme_default'),N'GridKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'业务所有者发布的稳定Grid键',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme_default',@level2type=N'COLUMN',@level2name=N'GridKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme_default') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme_default'),N'UserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前用户身份',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme_default',@level2type=N'COLUMN',@level2name=N'UserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme_default') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme_default'),N'DefaultSchemeId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'个人选择；撤销共享或删除后读取时重新核对可见性',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme_default',@level2type=N'COLUMN',@level2name=N'DefaultSchemeId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme_default') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme_default'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'默认选择自己的并发版本；未选择时可为零',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme_default',@level2type=N'COLUMN',@level2name=N'Version';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_settings_query_scheme_default') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_query_scheme_default'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次建立用户上下文记录的UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_settings_query_scheme_default',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
