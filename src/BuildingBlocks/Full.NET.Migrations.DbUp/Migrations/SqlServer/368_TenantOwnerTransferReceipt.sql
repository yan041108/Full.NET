-- Identity 所有者原子交接的不可变回执；保留历史账号和成员标识，不建立删除级联。
-- SQL Server 创建及索引/注释分别探测，部分执行后重入收敛；回退须保留已提交回执。
-- 账号 Owner 保护按 UserId/MemberRole 做有界存在性查询，不枚举该账号跨企业的成员清单。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_identity_tenant_member') AND name=N'IX_fn_identity_tenant_member_TenantId_MemberRole_Id')
    CREATE INDEX IX_fn_identity_tenant_member_TenantId_MemberRole_Id ON dbo.fn_identity_tenant_member(TenantId,MemberRole,Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_identity_tenant_member') AND name=N'IX_fn_identity_tenant_member_UserId_MemberRole')
    CREATE INDEX IX_fn_identity_tenant_member_UserId_MemberRole ON dbo.fn_identity_tenant_member(UserId,MemberRole);
IF OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_tenant_owner_transfer (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        OperationId uniqueidentifier NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        SourceMemberId uniqueidentifier NULL,
        SourceUserId uniqueidentifier NULL,
        SourcePreviousVersion int NULL,
        SourceVersion int NULL,
        TargetMemberId uniqueidentifier NOT NULL,
        TargetUserId uniqueidentifier NOT NULL,
        TargetPreviousVersion int NOT NULL,
        TargetVersion int NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        RequestDigest char(64) NOT NULL,
        CONSTRAINT PK_fn_identity_tenant_owner_transfer PRIMARY KEY CLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND name=N'UX_fn_identity_tenant_owner_transfer_TenantOperation')
    CREATE UNIQUE INDEX UX_fn_identity_tenant_owner_transfer_TenantOperation ON dbo.fn_identity_tenant_owner_transfer(TenantId,OperationId);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'企业所有者交接不可变回执及领域审计，不保存密码或TOTP', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'Id', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'TenantId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属企业标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'OperationId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'调用方固定的交接操作标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'OperationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'ActorUserId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际交接操作者账号标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'ActorUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'SourceMemberId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'交接前所有者成员标识，首次建立为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'SourceMemberId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'SourceUserId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'交接前所有者账号标识，首次建立为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'SourceUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'SourcePreviousVersion', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'交接前源成员版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'SourcePreviousVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'SourceVersion', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'交接后源成员版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'SourceVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'TargetMemberId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'接收职责的成员标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'TargetMemberId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'TargetUserId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'接收职责的账号标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'TargetUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'TargetPreviousVersion', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'交接前目标成员版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'TargetPreviousVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'TargetVersion', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'交接后目标成员版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'TargetVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'CreatedAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'交接事务提交事实时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'), N'RequestDigest', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'仅绑定非敏感目标与版本的请求摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'RequestDigest';
