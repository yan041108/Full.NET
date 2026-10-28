-- 330：双边同意仅建立共享授权；不得在同一业务事务写入两个作用域。
IF OBJECT_ID(N'dbo.fn_workflow_collaboration_grant',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_collaboration_grant (
 Id uniqueidentifier NOT NULL,
 SourceTenantId uniqueidentifier NULL,
 TargetTenantId uniqueidentifier NULL,
 SourceTenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 TargetTenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 SourceDefinitionId uniqueidentifier NOT NULL,
 SourceFormVersionId uniqueidentifier NOT NULL,
 TargetDefinitionVersionId uniqueidentifier NULL,
 SourceActorId uniqueidentifier NOT NULL,
 TargetActorId uniqueidentifier NULL,
 OfferJson nvarchar(max) NOT NULL,
 AcceptanceJson nvarchar(max) NULL,
 PolicyHash varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
 TargetSummaryJson nvarchar(max) NULL,
 TargetSummaryHash varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
 ProposalKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ProposalHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 Revision bigint NOT NULL,
 StatusKey varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ExpiresAtUtc datetime2(6) NOT NULL,
 CreatedAtUtc datetime2(6) NOT NULL,
 AcceptedAtUtc datetime2(6) NULL,
 RevokedAtUtc datetime2(6) NULL,
 RevokedById uniqueidentifier NULL,
 RevokedByScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
 RevocationReason nvarchar(512) COLLATE Latin1_General_100_BIN2 NULL,
 CONSTRAINT pk_fn_workflow_collaboration_grant PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流双边共享授权控制面；业务执行各在自己租户事务内',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'双边协作授权UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'SourceTenantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源租户；Host为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'SourceTenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'TargetTenantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标租户；Host为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'TargetTenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'SourceTenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源方可信规范作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'SourceTenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'TargetTenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标方可信规范作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'TargetTenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'SourceDefinitionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源方同意的流程定义',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'SourceDefinitionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'SourceFormVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源方同意的固定表单版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'SourceFormVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'TargetDefinitionVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标方接受时固定的已发布版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'TargetDefinitionVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'SourceActorId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源方提出同意的真实操作人',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'SourceActorId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'TargetActorId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标方接受同意的真实操作人',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'TargetActorId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'OfferJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源方固定字段及执行额度政策',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'OfferJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'AcceptanceJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标方固定映射人员及版本政策',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'AcceptanceJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'PolicyHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定双方身份及完整政策的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'PolicyHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'TargetSummaryJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'仅含获准投影的目标版本安全摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'TargetSummaryJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'TargetSummaryHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标版本安全摘要的SHA256',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'TargetSummaryHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'ProposalKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源操作人的提出幂等键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'ProposalKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'ProposalHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源提出请求的规范摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'ProposalHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变同意及撤销的CAS修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'proposed或active或revoked',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'ExpiresAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'授权绝对期限；微秒UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'ExpiresAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源提出时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'AcceptedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标接受时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'AcceptedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'RevokedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'任一参与方撤销时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'RevokedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'RevokedById','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际撤销操作人',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'RevokedById';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'RevokedByScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际撤销操作的可信参与方作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'RevokedByScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant'),N'RevocationReason','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'撤销或拒绝的审计原因',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant',@level2type=N'COLUMN',@level2name=N'RevocationReason';
GO
IF OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_collaboration_grant_action (
 Id uniqueidentifier NOT NULL,
 GrantId uniqueidentifier NOT NULL,
 ActorTenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ActorUserId uniqueidentifier NOT NULL,
 OperationKey varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
 IdempotencyKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
 RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ResponseJson nvarchar(max) NOT NULL,
 ResultRevision bigint NOT NULL,
 ResultStatusKey varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
 CreatedAtUtc datetime2(6) NOT NULL,
 CONSTRAINT pk_fn_workflow_collaboration_grant_action PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流授权同意撤销审计及稳定幂等响应',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'授权操作收据UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'GrantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属双边授权',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'GrantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'ActorTenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际操作人的可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'ActorTenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际操作人',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'OperationKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'propose或accept或revoke',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'OperationKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'IdempotencyKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本授权内操作人的请求幂等键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'IdempotencyKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'RequestHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'包含动作及修订的规范请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'ResponseJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次成功响应原文；重放不读取后续状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'ResponseJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'ResultRevision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次成功响应修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'ResultRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'ResultStatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次成功响应状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'ResultStatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'收据提交时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_collaboration_grant_action',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND name=N'ux_fn_workflow_collaboration_grant_proposal')
 CREATE UNIQUE INDEX ux_fn_workflow_collaboration_grant_proposal ON dbo.fn_workflow_collaboration_grant(SourceTenantScopeKey,SourceActorId,ProposalKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND name=N'ix_fn_workflow_collaboration_grant_source')
 CREATE INDEX ix_fn_workflow_collaboration_grant_source ON dbo.fn_workflow_collaboration_grant(SourceTenantScopeKey,SourceDefinitionId,CreatedAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant') AND name=N'ix_fn_workflow_collaboration_grant_target')
 CREATE INDEX ix_fn_workflow_collaboration_grant_target ON dbo.fn_workflow_collaboration_grant(TargetTenantScopeKey,StatusKey,CreatedAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_grant_action') AND name=N'ux_fn_workflow_collaboration_grant_action_key')
 CREATE UNIQUE INDEX ux_fn_workflow_collaboration_grant_action_key ON dbo.fn_workflow_collaboration_grant_action(GrantId,ActorTenantScopeKey,ActorUserId,IdempotencyKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_collaboration_grant_definition')
 ALTER TABLE dbo.fn_workflow_collaboration_grant ADD CONSTRAINT fk_fn_workflow_collaboration_grant_definition FOREIGN KEY (SourceDefinitionId) REFERENCES dbo.fn_workflow_definition(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_collaboration_grant_form')
 ALTER TABLE dbo.fn_workflow_collaboration_grant ADD CONSTRAINT fk_fn_workflow_collaboration_grant_form FOREIGN KEY (SourceFormVersionId) REFERENCES dbo.fn_workflow_form_version(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_collaboration_grant_target')
 ALTER TABLE dbo.fn_workflow_collaboration_grant ADD CONSTRAINT fk_fn_workflow_collaboration_grant_target FOREIGN KEY (TargetDefinitionVersionId) REFERENCES dbo.fn_workflow_definition_version(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_collaboration_grant_action_grant')
 ALTER TABLE dbo.fn_workflow_collaboration_grant_action ADD CONSTRAINT fk_fn_workflow_collaboration_grant_action_grant FOREIGN KEY (GrantId) REFERENCES dbo.fn_workflow_collaboration_grant(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'ck_fn_workflow_collaboration_grant_scope')
 ALTER TABLE dbo.fn_workflow_collaboration_grant ADD CONSTRAINT ck_fn_workflow_collaboration_grant_scope CHECK (SourceTenantScopeKey<>TargetTenantScopeKey AND (SourceTenantId IS NULL OR TargetTenantId IS NULL OR SourceTenantId<>TargetTenantId) AND ((SourceTenantId IS NULL AND SourceTenantScopeKey='host') OR (SourceTenantId IS NOT NULL AND SourceTenantScopeKey LIKE 'tenant:%')) AND ((TargetTenantId IS NULL AND TargetTenantScopeKey='host') OR (TargetTenantId IS NOT NULL AND TargetTenantScopeKey LIKE 'tenant:%')));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'ck_fn_workflow_collaboration_grant_state')
 ALTER TABLE dbo.fn_workflow_collaboration_grant ADD CONSTRAINT ck_fn_workflow_collaboration_grant_state CHECK (((StatusKey='proposed' AND Revision=1 AND TargetActorId IS NULL AND TargetDefinitionVersionId IS NULL AND AcceptanceJson IS NULL AND PolicyHash IS NULL AND TargetSummaryJson IS NULL AND TargetSummaryHash IS NULL AND AcceptedAtUtc IS NULL AND RevokedAtUtc IS NULL AND RevokedById IS NULL AND RevokedByScopeKey IS NULL AND RevocationReason IS NULL) OR (StatusKey='active' AND Revision=2 AND TargetActorId IS NOT NULL AND TargetDefinitionVersionId IS NOT NULL AND AcceptanceJson IS NOT NULL AND PolicyHash IS NOT NULL AND TargetSummaryJson IS NOT NULL AND TargetSummaryHash IS NOT NULL AND AcceptedAtUtc IS NOT NULL AND RevokedAtUtc IS NULL AND RevokedById IS NULL AND RevokedByScopeKey IS NULL AND RevocationReason IS NULL) OR (StatusKey='revoked' AND RevokedAtUtc IS NOT NULL AND RevokedById IS NOT NULL AND RevokedByScopeKey IN (SourceTenantScopeKey,TargetTenantScopeKey) AND RevocationReason IS NOT NULL AND ((Revision=2 AND TargetActorId IS NULL AND TargetDefinitionVersionId IS NULL AND AcceptanceJson IS NULL AND PolicyHash IS NULL AND TargetSummaryJson IS NULL AND TargetSummaryHash IS NULL AND AcceptedAtUtc IS NULL) OR (Revision=3 AND TargetActorId IS NOT NULL AND TargetDefinitionVersionId IS NOT NULL AND AcceptanceJson IS NOT NULL AND PolicyHash IS NOT NULL AND TargetSummaryJson IS NOT NULL AND TargetSummaryHash IS NOT NULL AND AcceptedAtUtc IS NOT NULL)))));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'ck_fn_workflow_collaboration_grant_expiry')
 ALTER TABLE dbo.fn_workflow_collaboration_grant ADD CONSTRAINT ck_fn_workflow_collaboration_grant_expiry CHECK (ExpiresAtUtc>CreatedAtUtc AND (AcceptedAtUtc IS NULL OR (AcceptedAtUtc>=CreatedAtUtc AND AcceptedAtUtc<ExpiresAtUtc)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'ck_fn_workflow_collaboration_grant_action_state')
 ALTER TABLE dbo.fn_workflow_collaboration_grant_action ADD CONSTRAINT ck_fn_workflow_collaboration_grant_action_state CHECK (OperationKey IN ('propose','accept','revoke') AND ResultRevision>0 AND ResultStatusKey IN ('proposed','active','revoked'));
GO
