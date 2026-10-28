-- 324：每次分叉沿实际执行代次绑定；主键同时是不可复用的ForkActivationId。
IF COL_LENGTH(N'dbo.fn_workflow_parallel_join',N'ExecutionEpochId') IS NULL
 ALTER TABLE dbo.fn_workflow_parallel_join ADD ExecutionEpochId uniqueidentifier NULL;
GO
-- 323之前只允许DAG；存量分叉均属于该根的初始代次，非空新事实不重写。
UPDATE dbo.fn_workflow_parallel_join SET ExecutionEpochId=InstanceId WHERE ExecutionEpochId IS NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_parallel_join') AND name=N'ExecutionEpochId' AND is_nullable=1)
 ALTER TABLE dbo.fn_workflow_parallel_join ALTER COLUMN ExecutionEpochId uniqueidentifier NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_parallel_join') AND name=N'fk_fn_workflow_parallel_join_epoch')
 ALTER TABLE dbo.fn_workflow_parallel_join ADD CONSTRAINT fk_fn_workflow_parallel_join_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_parallel_join') AND name=N'ix_fn_workflow_parallel_join_epoch')
 CREATE INDEX ix_fn_workflow_parallel_join_epoch ON dbo.fn_workflow_parallel_join(InstanceId,ExecutionEpochId,StatusKey,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_parallel_join') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'),N'ExecutionEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实分叉执行代次；Id即该次ForkActivationId，旧到达不得跨代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_parallel_join',@level2type=N'COLUMN',@level2name=N'ExecutionEpochId';
GO
