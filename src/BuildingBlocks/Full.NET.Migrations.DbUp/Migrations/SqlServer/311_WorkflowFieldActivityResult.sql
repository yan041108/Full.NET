-- 311：自动字段活动结果与步骤同事务落库，未记账重跑不覆盖已有事实。
-- 自动字段动作拥有独立系统审计；仍禁止人工动作省略操作人。
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor' AND (CHARINDEX(N'activity.execute',definition)=0 OR CHARINDEX(N'parent_subprocess',definition)=0))
  ALTER TABLE dbo.fn_workflow_domain_audit DROP CONSTRAINT ck_fn_workflow_domain_audit_actor;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor')
  ALTER TABLE dbo.fn_workflow_domain_audit WITH CHECK ADD CONSTRAINT ck_fn_workflow_domain_audit_actor
    CHECK (ActorUserId IS NOT NULL OR (OperationKey IN ('assignee.handle','activity.execute') AND ResourceTypeKey='step') OR
      (OperationKey='instance.cancel' AND ResourceTypeKey='instance' AND JSON_VALUE(DetailJson,'$.sourceKey') IS NOT NULL AND
       JSON_VALUE(DetailJson,'$.sourceKey')='parent_subprocess' AND JSON_VALUE(DetailJson,'$.parentInstanceId') IS NOT NULL AND JSON_VALUE(DetailJson,'$.parentStepId') IS NOT NULL));
GO
IF OBJECT_ID(N'dbo.fn_workflow_activity_result',N'U') IS NULL
BEGIN
  CREATE TABLE dbo.fn_workflow_activity_result (
    Id uniqueidentifier NOT NULL,
    InstanceId uniqueidentifier NOT NULL,
    TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    InputFormRevision bigint NOT NULL,
    OutputFormRevision bigint NOT NULL,
    InputHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    OutputHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    InputJson nvarchar(max) NOT NULL,
    OutputJson nvarchar(max) NOT NULL,
    PatchJson nvarchar(max) NOT NULL,
    OutcomeKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CreatedAtUtc datetime2(6) NOT NULL,
    CONSTRAINT pk_fn_workflow_activity_result PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT fk_fn_workflow_activity_result_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_activity_result_step FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_step(Id),
    CONSTRAINT ck_fn_workflow_activity_result_outcome CHECK ((OutcomeKey='applied' AND OutputFormRevision=InputFormRevision+1) OR (OutcomeKey='skipped' AND OutputFormRevision=InputFormRevision))
  );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=0 AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流自动字段活动的不可变输入、输出与补丁事实',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'Id','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'复用自动字段活动步骤的UUID v7标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'InstanceId','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属Workflow实例标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'活动绑定的可信租户作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'InputFormRevision','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定输入主表单或分支修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'InputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'OutputFormRevision','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'接纳输出后的表单或分支修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'OutputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'InputHash','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定输入的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'OutputHash','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'接纳输出的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'OutputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'InputJson','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次活动不可变输入快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'OutputJson','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次活动接纳的完整输出快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'OutputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'PatchJson','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次活动显式授权的字段补丁',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'PatchJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'OutcomeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'applied已执行或skipped条件未命中',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'OutcomeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_result') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_result'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次活动结果接纳时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_result',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
