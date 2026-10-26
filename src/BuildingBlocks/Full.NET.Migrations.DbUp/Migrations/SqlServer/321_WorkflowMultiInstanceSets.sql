-- 321：多实例来源与首次成功集合固化；复合关系不另造代理Id。
IF OBJECT_ID(N'dbo.fn_workflow_subprocess_set',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_subprocess_set (
  CallId uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ModeKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
  SourceKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
  MaximumCount int NOT NULL,
  RequiredPercentage int NOT NULL,
  ItemCount int NULL,
  RequiredSuccesses int NULL,
  SourceJson nvarchar(max) NULL,
  SourceHash char(64) COLLATE Latin1_General_100_BIN2 NULL,
  SelectionHash char(64) COLLATE Latin1_General_100_BIN2 NULL,
  OutputJson nvarchar(max) NULL,
  OutputHash char(64) COLLATE Latin1_General_100_BIN2 NULL,
  StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
  Revision bigint NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  UpdatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_subprocess_set PRIMARY KEY CLUSTERED (CallId)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流多实例来源快照、完成门槛及固化结果集合',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'复用真实父调用的UUID v7身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'拥有调用的父实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父实例可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'ModeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'serial依次或parallel并行',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'ModeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'SourceKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已发布的闭合项来源类型',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'SourceKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'MaximumCount','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定最大项数及预留额度上限',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'MaximumCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'RequiredPercentage','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定成功百分比；不能使用变化分母',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'RequiredPercentage';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'ItemCount','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际固化项数；准备完成前为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'ItemCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'RequiredSuccesses','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'按固化项数向上取整的成功门槛',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'RequiredSuccesses';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'SourceJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'一次固化的来源项及角色成员依据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'SourceJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'SourceHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'完整来源快照SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'SourceHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'SelectionHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'按ItemKey排序的固化成功项身份摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'SelectionHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'OutputJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'只归约固化成功集合的显式父字段补丁',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'OutputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'OutputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'合并输出SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'OutputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准备、运行、收尾、结果停车及终态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父锁内集合状态CAS修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父调用激活UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_set'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近集合更新UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_set',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'ck_fn_workflow_subprocess_set_state')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_state CHECK (ModeKey IN ('serial','parallel') AND SourceKey IN ('fixed_count','number_field','collection_field','role_members') AND MaximumCount BETWEEN 1 AND 32 AND RequiredPercentage BETWEEN 1 AND 100 AND Revision>=1 AND StatusKey IN ('preparing','running','closing','result_ready','applied','faulted','cancelled'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'ck_fn_workflow_subprocess_set_count')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_count CHECK ((ItemCount IS NULL AND RequiredSuccesses IS NULL) OR (ItemCount IS NOT NULL AND RequiredSuccesses IS NOT NULL AND ItemCount BETWEEN 0 AND MaximumCount AND RequiredSuccesses=FLOOR((ItemCount*RequiredPercentage+99)/100)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'ck_fn_workflow_subprocess_set_source')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_source CHECK ((SourceJson IS NULL AND SourceHash IS NULL) OR (SourceJson IS NOT NULL AND SourceHash IS NOT NULL));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'ck_fn_workflow_subprocess_set_output')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_output CHECK ((OutputJson IS NULL AND OutputHash IS NULL) OR (OutputJson IS NOT NULL AND OutputHash IS NOT NULL));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'ck_fn_workflow_subprocess_set_ready')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_ready CHECK (StatusKey NOT IN ('running','closing','result_ready','applied') OR (ItemCount IS NOT NULL AND SourceJson IS NOT NULL AND SourceHash IS NOT NULL));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'ck_fn_workflow_subprocess_set_selected')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_selected CHECK (StatusKey NOT IN ('closing','result_ready','applied') OR SelectionHash IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'ck_fn_workflow_subprocess_set_result')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_result CHECK (StatusKey NOT IN ('result_ready','applied') OR (OutputJson IS NOT NULL AND OutputHash IS NOT NULL));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'ck_fn_workflow_subprocess_set_sourcejson')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_sourcejson CHECK (SourceJson IS NULL OR ISJSON(SourceJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'ck_fn_workflow_subprocess_set_outputjson')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_outputjson CHECK (OutputJson IS NULL OR ISJSON(OutputJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'ix_fn_workflow_subprocess_set_instance')
 CREATE INDEX ix_fn_workflow_subprocess_set_instance ON dbo.fn_workflow_subprocess_set(InstanceId,TenantScopeKey,StatusKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'fk_fn_workflow_subprocess_set_call')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT fk_fn_workflow_subprocess_set_call FOREIGN KEY (CallId) REFERENCES dbo.fn_workflow_subprocess_call(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_set') AND name=N'fk_fn_workflow_subprocess_set_instance')
 ALTER TABLE dbo.fn_workflow_subprocess_set ADD CONSTRAINT fk_fn_workflow_subprocess_set_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id);
GO
IF OBJECT_ID(N'dbo.fn_workflow_subprocess_choice',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_subprocess_choice (
  CallId uniqueidentifier NOT NULL,
  ItemId uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_subprocess_choice PRIMARY KEY CLUSTERED (CallId,ItemId)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_choice') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流首次达标固化的成功子项；迟到结果不得追加',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_choice';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_choice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_choice'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'归属集合的真实调用身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_choice',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_choice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_choice'),N'ItemId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已成功并被固化选择的真实项身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_choice',@level2type=N'COLUMN',@level2name=N'ItemId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_choice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_choice'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际父实例身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_choice',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_choice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_choice'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父实例可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_choice',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_choice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_choice'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次固化选择UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_choice',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_choice') AND name=N'ux_fn_workflow_subprocess_choice_item')
 CREATE UNIQUE INDEX ux_fn_workflow_subprocess_choice_item ON dbo.fn_workflow_subprocess_choice(ItemId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_choice') AND name=N'fk_fn_workflow_subprocess_choice_set')
 ALTER TABLE dbo.fn_workflow_subprocess_choice ADD CONSTRAINT fk_fn_workflow_subprocess_choice_set FOREIGN KEY (CallId) REFERENCES dbo.fn_workflow_subprocess_set(CallId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_choice') AND name=N'fk_fn_workflow_subprocess_choice_item')
 ALTER TABLE dbo.fn_workflow_subprocess_choice ADD CONSTRAINT fk_fn_workflow_subprocess_choice_item FOREIGN KEY (ItemId) REFERENCES dbo.fn_workflow_subprocess_item(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_choice') AND name=N'fk_fn_workflow_subprocess_choice_instance')
 ALTER TABLE dbo.fn_workflow_subprocess_choice ADD CONSTRAINT fk_fn_workflow_subprocess_choice_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id);
GO
