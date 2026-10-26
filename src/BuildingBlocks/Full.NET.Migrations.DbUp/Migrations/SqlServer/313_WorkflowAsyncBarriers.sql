-- 313：依赖绑定原CallId；只有已释放屏障的唯一工作项可以跳过消费者等待。
IF COL_LENGTH(N'dbo.fn_workflow_activity_work',N'ResumeBarrierId') IS NULL
 ALTER TABLE dbo.fn_workflow_activity_work ADD ResumeBarrierId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'),N'ResumeBarrierId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'服务器原屏障续办证明；普通工作项为空，不能由客户端提供',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_work',@level2type=N'COLUMN',@level2name=N'ResumeBarrierId';
GO
IF OBJECT_ID(N'dbo.fn_workflow_async_barrier',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_async_barrier (
  Id uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  NodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  DependencyCallIdsJson nvarchar(max) NOT NULL,
  DependencyHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  InputJson nvarchar(max) NOT NULL,
  InputHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  InputFormRevision bigint NOT NULL,
  ParallelJoinId uniqueidentifier NULL,
  ParallelBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  LeaseGeneration bigint NOT NULL,
  LeaseOwnerKey varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
  LeaseExpiresAtUtc datetime2(6) NULL,
  FailureCount int NOT NULL,
  Revision bigint NOT NULL,
  ResumeWorkId uniqueidentifier NULL,
  OutputJson nvarchar(max) NULL,
  OutputHash varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
  OutputFormRevision bigint NULL,
  DueAtUtc datetime2(6) NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CompletedAtUtc datetime2(6) NULL,
  ErrorKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  CONSTRAINT pk_fn_workflow_async_barrier PRIMARY KEY CLUSTERED (Id),
  CONSTRAINT fk_fn_workflow_async_barrier_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
  CONSTRAINT fk_fn_workflow_async_barrier_step FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_step(Id),
  CONSTRAINT fk_fn_workflow_async_barrier_resume_work FOREIGN KEY (ResumeWorkId) REFERENCES dbo.fn_workflow_activity_work(Id),
  CONSTRAINT ck_fn_workflow_async_barrier_state CHECK (StatusKey IN ('pending','applying','released','cancelled','failed') AND LeaseGeneration>=0 AND FailureCount BETWEEN 0 AND 8 AND Revision>0 AND InputFormRevision>0),
  CONSTRAINT ck_fn_workflow_async_barrier_lease CHECK (((StatusKey='applying' AND LeaseOwnerKey IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL) OR (StatusKey<>'applying' AND LeaseOwnerKey IS NULL AND LeaseExpiresAtUtc IS NULL))),
  CONSTRAINT ck_fn_workflow_async_barrier_release CHECK ((StatusKey<>'released' OR (ResumeWorkId IS NOT NULL AND OutputJson IS NOT NULL AND OutputHash IS NOT NULL AND OutputFormRevision>0 AND CompletedAtUtc IS NOT NULL)))
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流异步结果消费者的耐久等待、租约与唯一续办回执',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'内部耐久等待步骤及屏障标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属原工作流实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信Host或租户作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'NodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'依赖结果的原消费者节点键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'NodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'DependencyCallIdsJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次等待绑定的原异步激活标识集合',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'DependencyCallIdsJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'DependencyHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定依赖集合的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'DependencyHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'InputJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'到达屏障时的原表单或分支快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'InputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原屏障输入的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'InputFormRevision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'到达屏障时原主表单或分支修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'InputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'ParallelJoinId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原并行或包容分叉执行身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'ParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'ParallelBranchKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原分叉中的稳定分支键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'ParallelBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'屏障的耐久闭合状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'LeaseGeneration','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'阻止旧Worker释放屏障的租约世代',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'LeaseGeneration';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'LeaseOwnerKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前接纳Worker的唯一领取身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'LeaseOwnerKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'LeaseExpiresAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前接纳租约的UTC截止',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'LeaseExpiresAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'FailureCount','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'连续准备或提交失败次数，正常等待不计失败',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'FailureCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'屏障乐观修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'ResumeWorkId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已释放屏障唯一允许续办的原工作项',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'ResumeWorkId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'OutputJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'释放时实际当前表单或分支输入',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'OutputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'OutputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'唯一续办工作项输入摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'OutputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'OutputFormRevision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'唯一续办工作项绑定的实际表单或分支修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'OutputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'DueAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'下次有界等待或安全接纳的UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'DueAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原屏障建立时间UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'释放、取消或不可恢复失败时间UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'CompletedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_async_barrier'),N'ErrorKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'稳定错误码，不保存异常或原始载荷',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_async_barrier',@level2type=N'COLUMN',@level2name=N'ErrorKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND name=N'fk_fn_workflow_activity_work_resume_barrier')
 ALTER TABLE dbo.fn_workflow_activity_work ADD CONSTRAINT fk_fn_workflow_activity_work_resume_barrier FOREIGN KEY (ResumeBarrierId) REFERENCES dbo.fn_workflow_async_barrier(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND name=N'ix_fn_workflow_async_barrier_due')
 CREATE INDEX ix_fn_workflow_async_barrier_due ON dbo.fn_workflow_async_barrier(StatusKey,DueAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_async_barrier') AND name=N'ix_fn_workflow_async_barrier_instance')
 CREATE INDEX ix_fn_workflow_async_barrier_instance ON dbo.fn_workflow_async_barrier(TenantScopeKey,InstanceId,CreatedAtUtc,Id);
GO
