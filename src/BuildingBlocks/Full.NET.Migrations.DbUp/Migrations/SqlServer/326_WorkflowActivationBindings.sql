-- 326：当前代次按固定节点绑定真实StepId；禁止从时间或序号重建有歧义的历史绑定。
IF OBJECT_ID(N'dbo.fn_workflow_activation_binding',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_activation_binding (
  Id uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ExecutionEpochId uniqueidentifier NOT NULL,
  NodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  StepId uniqueidentifier NOT NULL,
  SourceEpochId uniqueidentifier NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_activation_binding PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND name=N'ux_fn_workflow_activation_binding_node')
 CREATE UNIQUE INDEX ux_fn_workflow_activation_binding_node ON dbo.fn_workflow_activation_binding(ExecutionEpochId,NodeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND name=N'fk_fn_workflow_activation_binding_epoch')
 ALTER TABLE dbo.fn_workflow_activation_binding ADD CONSTRAINT fk_fn_workflow_activation_binding_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND name=N'fk_fn_workflow_activation_binding_step')
 ALTER TABLE dbo.fn_workflow_activation_binding ADD CONSTRAINT fk_fn_workflow_activation_binding_step FOREIGN KEY (StepId,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND name=N'fk_fn_workflow_activation_binding_source')
 ALTER TABLE dbo.fn_workflow_activation_binding ADD CONSTRAINT fk_fn_workflow_activation_binding_source FOREIGN KEY (StepId,SourceEpochId) REFERENCES dbo.fn_workflow_step(Id,ExecutionEpochId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流有效活动绑定；区域继承保留实际来源步骤身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activation_binding';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activation_binding'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定UUID v7身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activation_binding',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activation_binding'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际根实例身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activation_binding',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activation_binding'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'根实例可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activation_binding',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activation_binding'),N'ExecutionEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'消费此绑定的真实执行代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activation_binding',@level2type=N'COLUMN',@level2name=N'ExecutionEpochId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activation_binding'),N'NodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定定义节点引用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activation_binding',@level2type=N'COLUMN',@level2name=N'NodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activation_binding'),N'StepId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'唯一有效的实际活动步骤',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activation_binding',@level2type=N'COLUMN',@level2name=N'StepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activation_binding'),N'SourceEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际步骤原始执行代次；继承不得改写',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activation_binding',@level2type=N'COLUMN',@level2name=N'SourceEpochId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activation_binding') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activation_binding'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定创建UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activation_binding',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
