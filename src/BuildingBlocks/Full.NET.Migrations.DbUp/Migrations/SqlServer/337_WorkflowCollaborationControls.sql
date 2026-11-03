-- 337：目标独立事务发布创建/完成/关闭事实；源只接纳认证收件，不跨作用域写业务根。
-- 系统关闭必须指向固定调用；不允许用业务发起人冒充实际操作者。
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor' AND CHARINDEX(N'collaboration_cancel',definition)=0)
 ALTER TABLE dbo.fn_workflow_domain_audit DROP CONSTRAINT ck_fn_workflow_domain_audit_actor;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor')
 ALTER TABLE dbo.fn_workflow_domain_audit WITH CHECK ADD CONSTRAINT ck_fn_workflow_domain_audit_actor CHECK
 (ActorUserId IS NOT NULL OR (OperationKey IN ('assignee.handle','activity.execute') AND ResourceTypeKey='step') OR
  (OperationKey='instance.cancel' AND ResourceTypeKey='instance' AND JSON_VALUE(DetailJson,'$.sourceKey') IS NOT NULL AND
   JSON_VALUE(DetailJson,'$.sourceKey')='parent_subprocess' AND JSON_VALUE(DetailJson,'$.parentInstanceId') IS NOT NULL AND JSON_VALUE(DetailJson,'$.parentStepId') IS NOT NULL) OR
  (OperationKey IN ('instance.start','instance.cancel') AND ResourceTypeKey='instance' AND JSON_VALUE(DetailJson,'$.sourceKey') IS NOT NULL AND
   ((OperationKey='instance.start' AND JSON_VALUE(DetailJson,'$.sourceKey')='collaboration_relay') OR
    (OperationKey='instance.cancel' AND JSON_VALUE(DetailJson,'$.sourceKey')='collaboration_cancel')) AND
   TRY_CONVERT(uniqueidentifier,JSON_VALUE(DetailJson,'$.callId')) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(DetailJson,'$.starterUserId')) IS NOT NULL));
GO
IF OBJECT_ID(N'dbo.fn_workflow_collaboration_control',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_collaboration_control (
 Id uniqueidentifier NOT NULL,
 CallId uniqueidentifier NOT NULL,
 SourceTenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 TargetTenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 KindKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
 BodyJson nvarchar(max) NOT NULL,
 BodyHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 EnvelopeMessageId uniqueidentifier NULL,
 StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
 Revision bigint NOT NULL,
 CreatedAtUtc datetime2(6) NOT NULL,
 ReceivedAtUtc datetime2(6) NULL,
 CONSTRAINT pk_fn_workflow_collaboration_control PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流创建结果取消的最小耐久协议事实',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'协议回执UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'唯一共享调用准入',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'SourceTenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信原源作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'SourceTenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'TargetTenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信目标发布作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'TargetTenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'KindKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建、完成或关闭确认种类',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'KindKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'BodyJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定绑定及最小结果正文',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'BodyJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'BodyHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变正文SHA256',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'BodyHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'EnvelopeMessageId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次认证物理信封身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'EnvelopeMessageId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发布、收件或已接纳状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'协议状态CAS修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'唯一发布UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_control'),N'ReceivedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次收件UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_control',@level2type=N'COLUMN',@level2name=N'ReceivedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND name=N'ux_fn_workflow_collaboration_control_kind')
 CREATE UNIQUE INDEX ux_fn_workflow_collaboration_control_kind ON dbo.fn_workflow_collaboration_control(CallId,KindKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND name=N'ix_fn_workflow_collaboration_control_pending')
 CREATE INDEX ix_fn_workflow_collaboration_control_pending ON dbo.fn_workflow_collaboration_control(StatusKey,CreatedAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND name=N'ux_fn_workflow_collaboration_control_envelope')
 CREATE UNIQUE INDEX ux_fn_workflow_collaboration_control_envelope ON dbo.fn_workflow_collaboration_control(EnvelopeMessageId) WHERE EnvelopeMessageId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND name=N'ix_fn_workflow_collaboration_origin_root')
 CREATE INDEX ix_fn_workflow_collaboration_origin_root ON dbo.fn_workflow_collaboration_origin(RootInstanceId,TenantScopeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'ix_fn_workflow_collaboration_call_instance')
 CREATE INDEX ix_fn_workflow_collaboration_call_instance ON dbo.fn_workflow_collaboration_call(InstanceId,TenantScopeKey,StatusKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND name=N'fk_fn_workflow_collaboration_control_call')
 ALTER TABLE dbo.fn_workflow_collaboration_control ADD CONSTRAINT fk_fn_workflow_collaboration_control_call FOREIGN KEY (CallId) REFERENCES dbo.fn_workflow_collaboration_admission(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND name=N'ck_fn_workflow_collaboration_control_state')
 ALTER TABLE dbo.fn_workflow_collaboration_control ADD CONSTRAINT ck_fn_workflow_collaboration_control_state CHECK (Revision>0 AND SourceTenantScopeKey<>TargetTenantScopeKey AND KindKey IN ('created','completed','cancelled') AND StatusKey IN ('published','received','applied') AND ((StatusKey='published' AND EnvelopeMessageId IS NULL AND ReceivedAtUtc IS NULL) OR (StatusKey IN ('received','applied') AND EnvelopeMessageId IS NOT NULL AND ReceivedAtUtc IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_control') AND name=N'ck_fn_workflow_collaboration_control_json')
 ALTER TABLE dbo.fn_workflow_collaboration_control ADD CONSTRAINT ck_fn_workflow_collaboration_control_json CHECK (ISJSON(BodyJson)=1);
GO
