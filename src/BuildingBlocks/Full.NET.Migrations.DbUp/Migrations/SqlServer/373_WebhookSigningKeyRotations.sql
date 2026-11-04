-- 同订阅的准备与完成由原版本CAS串行化；不改变现有密钥、事件或投递。
IF COL_LENGTH(N'dbo.fn_webhooks_subscription',N'PreparedRotationId') IS NULL
    ALTER TABLE dbo.fn_webhooks_subscription ADD PreparedRotationId uniqueidentifier NULL;
GO
IF OBJECT_ID(N'dbo.fn_webhooks_key_rotation',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_webhooks_key_rotation (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        OperationId uniqueidentifier NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        ActorSessionId uniqueidentifier NOT NULL,
        SubscriptionId uniqueidentifier NOT NULL,
        PreviousKeyId uniqueidentifier NOT NULL,
        PreviousVersion int NOT NULL,
        PreparedVersion int NOT NULL,
        StateKey varchar(16) NOT NULL,
        ProtectedSecret nvarchar(1024) NULL,
        ProtectedFingerprint nvarchar(1024) NOT NULL,
        PreparedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        CompletedVersion int NULL,
        CompletedByUserId uniqueidentifier NULL,
        CompletedBySessionId uniqueidentifier NULL,
        CONSTRAINT PK_fn_webhooks_key_rotation PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_webhooks_key_rotation_State CHECK (PreviousVersion>=1 AND PreparedVersion=PreviousVersion+1 AND Id<>PreviousKeyId AND ((StateKey='prepared' AND ProtectedSecret IS NOT NULL AND CompletedAtUtc IS NULL AND CompletedVersion IS NULL AND CompletedByUserId IS NULL AND CompletedBySessionId IS NULL) OR (StateKey IN ('activated','cancelled') AND ProtectedSecret IS NULL AND CompletedAtUtc IS NOT NULL AND CompletedVersion>PreparedVersion AND CompletedByUserId IS NOT NULL AND CompletedBySessionId IS NOT NULL)))
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND name=N'CX_fn_webhooks_key_rotation_PreparedId')
    CREATE CLUSTERED INDEX CX_fn_webhooks_key_rotation_PreparedId ON dbo.fn_webhooks_key_rotation(PreparedAtUtc,Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND name=N'UX_fn_webhooks_key_rotation_TenantOperation')
    CREATE UNIQUE INDEX UX_fn_webhooks_key_rotation_TenantOperation ON dbo.fn_webhooks_key_rotation(TenantId,OperationId);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'签名密钥准备及明确激活取消的同事务审计；完成后清除准备秘密',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端UUID v7主键及新公开密钥身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信企业标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'OperationId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定准备操作，未知结果不得替换',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'OperationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准备操作者',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'ActorUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'ActorSessionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准备提交时交互会话',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'ActorSessionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'SubscriptionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原订阅标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'SubscriptionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'PreviousKeyId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准备时原活动密钥身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'PreviousKeyId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'PreviousVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准备前订阅修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'PreviousVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'PreparedVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准备提交后订阅修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'PreparedVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'StateKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'prepared或activated或cancelled',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'StateKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'ProtectedSecret','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准备期间的加密秘密；激活或取消后清除',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'ProtectedSecret';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'ProtectedFingerprint','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'隔离用途加密的原输入摘要，仅用于幂等核对',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'ProtectedFingerprint';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'PreparedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准备提交UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'PreparedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活或取消提交UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'CompletedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'CompletedVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'完成提交后的订阅修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'CompletedVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'CompletedByUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际完成操作者',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'CompletedByUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_key_rotation'),N'CompletedBySessionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'完成提交时交互会话',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_key_rotation',@level2type=N'COLUMN',@level2name=N'CompletedBySessionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_subscription') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'),N'PreparedRotationId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前唯一待处理轮换身份；完成后清空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_subscription',@level2type=N'COLUMN',@level2name=N'PreparedRotationId';
