-- 个人配置为本模块偏好事实；任务使用独立快照，不建立跨模块外键或配置指针。
-- SQL Server 用索引与作用域行锁支持有界本人查询；独立存在性守卫允许部分 DDL 失败后重入。
-- 仅新增偏好表及可空任务输入列，不回填旧队列授权；删除结构会丢失配置和冻结输入，回退须先停用消费方并保存数据。
IF OBJECT_ID(N'dbo.fn_import_export_profile_scope',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_import_export_profile_scope (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NOT NULL,
    UserId uniqueidentifier NOT NULL,
    CreatedAtUtc datetime2(6) NOT NULL,
    CONSTRAINT PK_fn_import_export_profile_scope PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UX_fn_import_export_profile_scope_Tenant_User UNIQUE (TenantId,UserId)
    );
END;
GO
IF OBJECT_ID(N'dbo.fn_import_export_profile',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_import_export_profile (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NOT NULL,
    OwnerUserId uniqueidentifier NOT NULL,
    SchemaKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    WorksheetKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Name nvarchar(64) NOT NULL,
    HeaderFingerprint char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ConfigurationJson nvarchar(max) NOT NULL,
    CreatedAtUtc datetime2(6) NOT NULL,
    UpdatedAtUtc datetime2(6) NULL,
    Version bigint NOT NULL,
    CONSTRAINT PK_fn_import_export_profile PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT CK_fn_import_export_profile_Version CHECK (Version>0),
    CONSTRAINT CK_fn_import_export_profile_Config CHECK (ISJSON(ConfigurationJson)=1 AND DATALENGTH(ConfigurationJson)<=32768),
    INDEX IX_fn_import_export_profile_Owner_Source NONCLUSTERED (TenantId,OwnerUserId,SchemaKey,WorksheetKey,CreatedAtUtc DESC,Id DESC)
    );
END;
GO
IF COL_LENGTH(N'dbo.fn_import_export_task',N'ColumnMappingJson') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD ColumnMappingJson nvarchar(max) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_import_export_task') AND name=N'CK_fn_import_export_task_ColumnMappingJson')
    ALTER TABLE dbo.fn_import_export_task ADD CONSTRAINT CK_fn_import_export_task_ColumnMappingJson CHECK (ColumnMappingJson IS NULL OR (ISJSON(ColumnMappingJson)=1 AND DATALENGTH(ColumnMappingJson)<=33792));
GO
IF COL_LENGTH(N'dbo.fn_import_export_task',N'ActorSessionBindingJson') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD ActorSessionBindingJson nvarchar(max) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_import_export_task') AND name=N'CK_fn_import_export_task_ActorSessionBindingJson')
    ALTER TABLE dbo.fn_import_export_task ADD CONSTRAINT CK_fn_import_export_task_ActorSessionBindingJson CHECK (ActorSessionBindingJson IS NULL OR (ISJSON(ActorSessionBindingJson)=1 AND DATALENGTH(ActorSessionBindingJson)<=8192));
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile_scope') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'个人导入配置写入互斥作用域，不参与业务模块事务',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile_scope';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile_scope') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile_scope'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端 UUID v7 逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile_scope',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile_scope') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile_scope'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile_scope',@level2type=N'COLUMN',@level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile_scope') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile_scope'),N'UserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信用户标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile_scope',@level2type=N'COLUMN',@level2name=N'UserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile_scope') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile_scope'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'作用域首次创建 UTC 时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile_scope',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'按可信租户用户与静态来源隔离的个人导入列映射配置',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端 UUID v7 逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'OwnerUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变本人配置所有者',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'OwnerUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'SchemaKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'静态导入来源键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'SchemaKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'WorksheetKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'静态工作表键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'WorksheetKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'Name','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本人定义的有界配置名称',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'Name';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'HeaderFingerprint','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'有序目标表头的 SHA256 指纹，不表示业务规则版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'HeaderFingerprint';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'ConfigurationJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'规范化表头与字面列映射，不含业务行数据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'ConfigurationJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建 UTC 时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最后修改 UTC 时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_profile') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_profile'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'正整数并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_profile',@level2type=N'COLUMN',@level2name=N'Version';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'),N'ColumnMappingJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时固化的有界列映射输入，后台不读取当前个人配置',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_task',@level2type=N'COLUMN',@level2name=N'ColumnMappingJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_import_export_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'),N'ActorSessionBindingJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本人显式排队操作绑定的会话，后台每阶段重新授权',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_task',@level2type=N'COLUMN',@level2name=N'ActorSessionBindingJson';
GO
