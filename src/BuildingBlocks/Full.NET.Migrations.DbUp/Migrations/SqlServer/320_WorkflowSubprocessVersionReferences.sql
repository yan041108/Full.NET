-- 320：固定子版本依赖与历史引用；结构探测、回填和约束均可重跑。
IF OBJECT_ID(N'dbo.fn_workflow_subprocess_ref',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_subprocess_ref (
  ParentVersionId uniqueidentifier NOT NULL,
  ChildVersionId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_subprocess_ref PRIMARY KEY CLUSTERED (ParentVersionId,ChildVersionId)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_ref') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流已发布父子版本依赖；固定子版本删除由同模块外键保护',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_ref';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_ref') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_ref'),N'ParentVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'拥有子调用的固定父版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_ref',@level2type=N'COLUMN',@level2name=N'ParentVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_ref') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_ref'),N'ChildVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'被父版本固定引用的子版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_ref',@level2type=N'COLUMN',@level2name=N'ChildVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_ref') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_ref'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'双方定义共有的可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_ref',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_ref') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_ref'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父版本发布时刻；统一UTC微秒精度',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_ref',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_ref') AND name=N'ix_fn_workflow_subprocess_ref_child')
 CREATE INDEX ix_fn_workflow_subprocess_ref_child ON dbo.fn_workflow_subprocess_ref(ChildVersionId,TenantScopeKey,ParentVersionId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_ref') AND name=N'fk_fn_workflow_subprocess_ref_parent')
 ALTER TABLE dbo.fn_workflow_subprocess_ref ADD CONSTRAINT fk_fn_workflow_subprocess_ref_parent FOREIGN KEY (ParentVersionId) REFERENCES dbo.fn_workflow_definition_version(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_ref') AND name=N'fk_fn_workflow_subprocess_ref_child')
 ALTER TABLE dbo.fn_workflow_subprocess_ref ADD CONSTRAINT fk_fn_workflow_subprocess_ref_child FOREIGN KEY (ChildVersionId) REFERENCES dbo.fn_workflow_definition_version(Id);
GO
IF EXISTS (SELECT 1 FROM dbo.fn_workflow_definition_version AS parentVersion
 INNER JOIN dbo.fn_workflow_definition AS parentDefinition ON parentDefinition.Id=parentVersion.DefinitionId
 CROSS APPLY OPENJSON(parentVersion.CanonicalJson,'$.nodes') AS node
 LEFT JOIN dbo.fn_workflow_definition_version AS childVersion ON childVersion.Id=TRY_CONVERT(uniqueidentifier,JSON_VALUE(node.value,'$.config.subprocessPolicy.definitionVersionId'))
 LEFT JOIN dbo.fn_workflow_definition AS childDefinition ON childDefinition.Id=childVersion.DefinitionId
 WHERE JSON_VALUE(node.value,'$.nodeTypeKey')='process.subprocess' AND (childVersion.Id IS NULL OR childVersion.Id=parentVersion.Id OR childDefinition.TenantScopeKey<>parentDefinition.TenantScopeKey))
BEGIN
 ;THROW 51000,N'Workflow subprocess version dependency is missing or crosses scope.',1;
END;
GO
INSERT INTO dbo.fn_workflow_subprocess_ref (ParentVersionId,ChildVersionId,TenantScopeKey,CreatedAtUtc)
 SELECT DISTINCT parentVersion.Id,childVersion.Id,parentDefinition.TenantScopeKey,parentVersion.PublishedAtUtc
 FROM dbo.fn_workflow_definition_version AS parentVersion
 INNER JOIN dbo.fn_workflow_definition AS parentDefinition ON parentDefinition.Id=parentVersion.DefinitionId
 CROSS APPLY OPENJSON(parentVersion.CanonicalJson,'$.nodes') AS node
 LEFT JOIN dbo.fn_workflow_definition_version AS childVersion ON childVersion.Id=TRY_CONVERT(uniqueidentifier,JSON_VALUE(node.value,'$.config.subprocessPolicy.definitionVersionId'))
 LEFT JOIN dbo.fn_workflow_definition AS childDefinition ON childDefinition.Id=childVersion.DefinitionId
 WHERE JSON_VALUE(node.value,'$.nodeTypeKey')='process.subprocess'
 AND NOT EXISTS (SELECT 1 FROM dbo.fn_workflow_subprocess_ref AS existing WHERE existing.ParentVersionId=parentVersion.Id AND existing.ChildVersionId=childVersion.Id);
GO
