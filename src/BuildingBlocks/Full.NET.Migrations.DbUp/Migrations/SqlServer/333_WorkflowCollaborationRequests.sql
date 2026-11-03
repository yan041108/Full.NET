-- 333：源请求与本地可靠收件；不跨作用域事务创建目标实例，不开放公共节点20。
IF OBJECT_ID(N'dbo.fn_workflow_collaboration_call',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_collaboration_call (
 Id uniqueidentifier NOT NULL,
 InstanceId uniqueidentifier NOT NULL,
 ExecutionEpochId uniqueidentifier NOT NULL,
 TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 NodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ContinuationNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
 GrantId uniqueidentifier NOT NULL,
 DeliveryId uniqueidentifier NOT NULL,
 BindingJson nvarchar(max) NOT NULL,
 BindingHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 InputJson nvarchar(max) NOT NULL,
 InputHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 InputFormRevision bigint NOT NULL,
 ProjectionJson nvarchar(max) NOT NULL,
 ProjectionHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ReservedActivities int NOT NULL,
 StatusKey varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
 Revision bigint NOT NULL,
 CreatedAtUtc datetime2(6) NOT NULL,
 UpdatedAtUtc datetime2(6) NOT NULL,
 CONSTRAINT pk_fn_workflow_collaboration_call PRIMARY KEY CLUSTERED (Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流源协作调用及固定输入证明',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实协作Step及调用UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地源实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'ExecutionEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际源执行代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'ExecutionEpochId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信源作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'NodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定源节点键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'NodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'ContinuationNodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定唯一后继',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'ContinuationNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'GrantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已接受的双边授权',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'GrantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'DeliveryId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'独立逻辑请求投递UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'DeliveryId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'BindingJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实源活动及固定授权版本证明',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'BindingJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'BindingHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定证明SHA256',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'BindingHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'InputJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变源输入',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'InputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原输入SHA256',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'InputFormRevision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原输入修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'InputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'ProjectionJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'仅含获准映射的固定投影',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'ProjectionJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'ProjectionHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定投影SHA256',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'ProjectionHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'ReservedActivities','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'从源全局预算预留的目标切片',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'ReservedActivities';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'请求生命周期',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地CAS修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源事务提交意图时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_call'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地状态推进时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_call',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
IF OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_collaboration_delivery (
 Id uniqueidentifier NOT NULL,
 CallId uniqueidentifier NOT NULL,
 TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 EnvelopeMessageId uniqueidentifier NOT NULL,
 BindingHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 StatusKey varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
 Revision bigint NOT NULL,
 ReceivedAtUtc datetime2(6) NOT NULL,
 CONSTRAINT pk_fn_workflow_collaboration_delivery PRIMARY KEY CLUSTERED (Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流源信封本地收件事实；目标Relay另建作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_delivery';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'逻辑请求投递身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_delivery',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原本地源调用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_delivery',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'由可信消息信封确定的作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_delivery',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery'),N'EnvelopeMessageId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次接收的物理消息身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_delivery',@level2type=N'COLUMN',@level2name=N'EnvelopeMessageId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery'),N'BindingHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已核对的持久绑定摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_delivery',@level2type=N'COLUMN',@level2name=N'BindingHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'收件与Relay生命周期',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_delivery',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地收件CAS修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_delivery',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery'),N'ReceivedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次收件时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_delivery',@level2type=N'COLUMN',@level2name=N'ReceivedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND name=N'ux_fn_workflow_collaboration_grant_source_id')
 CREATE UNIQUE INDEX ux_fn_workflow_collaboration_grant_source_id ON dbo.fn_workflow_collaboration_grant(Id,SourceTenantScopeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'ux_fn_workflow_collaboration_call_scope')
 CREATE UNIQUE INDEX ux_fn_workflow_collaboration_call_scope ON dbo.fn_workflow_collaboration_call(Id,TenantScopeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'ux_fn_workflow_collaboration_call_epoch_node')
 CREATE UNIQUE INDEX ux_fn_workflow_collaboration_call_epoch_node ON dbo.fn_workflow_collaboration_call(InstanceId,ExecutionEpochId,NodeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'ux_fn_workflow_collaboration_call_delivery')
 CREATE UNIQUE INDEX ux_fn_workflow_collaboration_call_delivery ON dbo.fn_workflow_collaboration_call(DeliveryId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND name=N'ux_fn_workflow_collaboration_delivery_message')
 CREATE UNIQUE INDEX ux_fn_workflow_collaboration_delivery_message ON dbo.fn_workflow_collaboration_delivery(TenantScopeKey,EnvelopeMessageId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND name=N'ix_fn_workflow_collaboration_delivery_pending')
 CREATE INDEX ix_fn_workflow_collaboration_delivery_pending ON dbo.fn_workflow_collaboration_delivery(StatusKey,ReceivedAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'fk_fn_workflow_collaboration_call_instance')
 ALTER TABLE dbo.fn_workflow_collaboration_call ADD CONSTRAINT fk_fn_workflow_collaboration_call_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'fk_fn_workflow_collaboration_call_step')
 ALTER TABLE dbo.fn_workflow_collaboration_call ADD CONSTRAINT fk_fn_workflow_collaboration_call_step FOREIGN KEY (Id,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'fk_fn_workflow_collaboration_call_epoch')
 ALTER TABLE dbo.fn_workflow_collaboration_call ADD CONSTRAINT fk_fn_workflow_collaboration_call_epoch FOREIGN KEY (Id,ExecutionEpochId) REFERENCES dbo.fn_workflow_step(Id,ExecutionEpochId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'fk_fn_workflow_collaboration_call_grant')
 ALTER TABLE dbo.fn_workflow_collaboration_call ADD CONSTRAINT fk_fn_workflow_collaboration_call_grant FOREIGN KEY (GrantId,TenantScopeKey) REFERENCES dbo.fn_workflow_collaboration_grant(Id,SourceTenantScopeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND name=N'fk_fn_workflow_collaboration_delivery_call')
 ALTER TABLE dbo.fn_workflow_collaboration_delivery ADD CONSTRAINT fk_fn_workflow_collaboration_delivery_call FOREIGN KEY (CallId,TenantScopeKey) REFERENCES dbo.fn_workflow_collaboration_call(Id,TenantScopeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'ck_fn_workflow_collaboration_call_state')
 ALTER TABLE dbo.fn_workflow_collaboration_call ADD CONSTRAINT ck_fn_workflow_collaboration_call_state CHECK (Revision>0 AND InputFormRevision>0 AND ReservedActivities BETWEEN 2 AND 4096 AND StatusKey IN ('requested','accepted','result_ready','cancelling','cancelled','denied','completed'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'ck_fn_workflow_collaboration_call_json')
 ALTER TABLE dbo.fn_workflow_collaboration_call ADD CONSTRAINT ck_fn_workflow_collaboration_call_json CHECK (ISJSON(BindingJson)=1 AND ISJSON(InputJson)=1 AND ISJSON(ProjectionJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_delivery') AND name=N'ck_fn_workflow_collaboration_delivery_state')
 ALTER TABLE dbo.fn_workflow_collaboration_delivery ADD CONSTRAINT ck_fn_workflow_collaboration_delivery_state CHECK (Revision>0 AND StatusKey IN ('received','dispatching','dispatched','rejected'));
GO
