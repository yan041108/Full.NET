-- 336：共享准入协调两方竞争；目标和后代均保存本地来源，不跨作用域写业务根。
IF OBJECT_ID(N'dbo.fn_workflow_collaboration_admission',N'U') IS NULL
 CREATE TABLE dbo.fn_workflow_collaboration_admission (
 Id uniqueidentifier NOT NULL,
 SourceTenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 TargetTenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 DeliveryId uniqueidentifier NOT NULL,
 GrantId uniqueidentifier NOT NULL,
 BindingJson nvarchar(max) NOT NULL,
 BindingHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ProjectionJson nvarchar(max) NOT NULL,
 ProjectionHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 StatusKey varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
 TargetInstanceId uniqueidentifier NULL,
 Revision bigint NOT NULL,
 CreatedAtUtc datetime2(6) NOT NULL,
 UpdatedAtUtc datetime2(6) NOT NULL,
 CONSTRAINT pk_fn_workflow_collaboration_admission PRIMARY KEY CLUSTERED (Id)
 );
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流双方共享调用准入事实，仅协调创建与关闭',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'调用UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'SourceTenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信源作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'SourceTenantScopeKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'TargetTenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'获准目标作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'TargetTenantScopeKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'DeliveryId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源逻辑投递身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'DeliveryId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'GrantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'双边共享授权',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'GrantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'BindingJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定调用绑定，不含源私有正文',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'BindingJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'BindingHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定绑定SHA256',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'BindingHash';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'ProjectionJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'仅含获准字段的最小投影',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'ProjectionJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'ProjectionHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'投影SHA256',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'ProjectionHash';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建与关闭准入状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'StatusKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'TargetInstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标提交的独立实例身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'TargetInstanceId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'共享准入CAS修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'Revision';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源提交时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_admission'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近推进时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_admission',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
IF OBJECT_ID(N'dbo.fn_workflow_collaboration_origin',N'U') IS NULL
 CREATE TABLE dbo.fn_workflow_collaboration_origin (
 Id uniqueidentifier NOT NULL,
 CallId uniqueidentifier NOT NULL,
 RootInstanceId uniqueidentifier NOT NULL,
 TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 Depth int NOT NULL,
 CONSTRAINT pk_fn_workflow_collaboration_origin PRIMARY KEY CLUSTERED (Id)
 );
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流目标和后代的真实协作来源',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_origin';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_origin'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地实例UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_origin',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_origin'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'共享调用准入身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_origin',@level2type=N'COLUMN',@level2name=N'CallId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_origin'),N'RootInstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地独立目标根',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_origin',@level2type=N'COLUMN',@level2name=N'RootInstanceId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_origin'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信目标作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_origin',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_origin'),N'Depth','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际全局调用深度',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_origin',@level2type=N'COLUMN',@level2name=N'Depth';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND name=N'ix_fn_workflow_collaboration_admission_pending')
 CREATE INDEX ix_fn_workflow_collaboration_admission_pending ON dbo.fn_workflow_collaboration_admission(StatusKey,UpdatedAtUtc,Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND name=N'ix_fn_workflow_collaboration_origin_call')
 CREATE INDEX ix_fn_workflow_collaboration_origin_call ON dbo.fn_workflow_collaboration_origin(CallId,TenantScopeKey);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND name=N'fk_fn_workflow_collaboration_admission_grant')
 ALTER TABLE dbo.fn_workflow_collaboration_admission ADD CONSTRAINT fk_fn_workflow_collaboration_admission_grant FOREIGN KEY (GrantId) REFERENCES dbo.fn_workflow_collaboration_grant(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND name=N'fk_fn_workflow_collaboration_origin_instance')
 ALTER TABLE dbo.fn_workflow_collaboration_origin ADD CONSTRAINT fk_fn_workflow_collaboration_origin_instance FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_instance(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND name=N'fk_fn_workflow_collaboration_origin_root')
 ALTER TABLE dbo.fn_workflow_collaboration_origin ADD CONSTRAINT fk_fn_workflow_collaboration_origin_root FOREIGN KEY (RootInstanceId) REFERENCES dbo.fn_workflow_instance(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND name=N'fk_fn_workflow_collaboration_origin_call')
 ALTER TABLE dbo.fn_workflow_collaboration_origin ADD CONSTRAINT fk_fn_workflow_collaboration_origin_call FOREIGN KEY (CallId) REFERENCES dbo.fn_workflow_collaboration_admission(Id);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND name=N'ck_fn_workflow_collaboration_admission_state')
 ALTER TABLE dbo.fn_workflow_collaboration_admission ADD CONSTRAINT ck_fn_workflow_collaboration_admission_state CHECK (Revision>0 AND SourceTenantScopeKey<>TargetTenantScopeKey AND StatusKey IN ('requested','created','active','cancel_requested','cancelled','completed') AND (StatusKey NOT IN ('created','active') OR TargetInstanceId IS NOT NULL));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_admission') AND name=N'ck_fn_workflow_collaboration_admission_json')
 ALTER TABLE dbo.fn_workflow_collaboration_admission ADD CONSTRAINT ck_fn_workflow_collaboration_admission_json CHECK (ISJSON(BindingJson)=1 AND ISJSON(ProjectionJson)=1);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_origin') AND name=N'ck_fn_workflow_collaboration_origin_depth')
 ALTER TABLE dbo.fn_workflow_collaboration_origin ADD CONSTRAINT ck_fn_workflow_collaboration_origin_depth CHECK (Depth BETWEEN 1 AND 8);
GO
-- Relay 是自动创建者；固定发起人只承担业务身份，不伪装成人工操作人。
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor' AND CHARINDEX(N'collaboration_relay',definition)=0)
 ALTER TABLE dbo.fn_workflow_domain_audit DROP CONSTRAINT ck_fn_workflow_domain_audit_actor;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor')
 ALTER TABLE dbo.fn_workflow_domain_audit WITH CHECK ADD CONSTRAINT ck_fn_workflow_domain_audit_actor CHECK
 (ActorUserId IS NOT NULL OR (OperationKey IN ('assignee.handle','activity.execute') AND ResourceTypeKey='step') OR
  (OperationKey='instance.cancel' AND ResourceTypeKey='instance' AND JSON_VALUE(DetailJson,'$.sourceKey') IS NOT NULL AND
   JSON_VALUE(DetailJson,'$.sourceKey')='parent_subprocess' AND JSON_VALUE(DetailJson,'$.parentInstanceId') IS NOT NULL AND JSON_VALUE(DetailJson,'$.parentStepId') IS NOT NULL) OR
  (OperationKey='instance.start' AND ResourceTypeKey='instance' AND JSON_VALUE(DetailJson,'$.sourceKey') IS NOT NULL AND
   JSON_VALUE(DetailJson,'$.sourceKey')='collaboration_relay' AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(DetailJson,'$.callId')) IS NOT NULL AND
   TRY_CONVERT(uniqueidentifier,JSON_VALUE(DetailJson,'$.starterUserId')) IS NOT NULL));
GO
