-- 工作项与成员 Pending 状态同事务提交；不对 Tenancy 表建立外键。
IF OBJECT_ID(N'dbo.fn_identity_member_seat_operation',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_member_seat_operation (
        Id uniqueidentifier NOT NULL,
        RequestKey varchar(128) NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        MemberId uniqueidentifier NOT NULL,
        InvitationId uniqueidentifier NULL,
        PreviousRole varchar(32) NULL,
        PreviousStatus varchar(32) NULL,
        MemberVersion int NOT NULL,
        InvitationVersion int NULL,
        Phase varchar(32) NOT NULL,
        RequestHash varchar(64) NULL,
        ErrorCode varchar(128) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        DueAtUtc datetimeoffset(7) NOT NULL,
        LeaseId uniqueidentifier NULL,
        LeaseUntilUtc datetimeoffset(7) NULL,
        Version int NOT NULL,
        Attempt int NOT NULL,
        CONSTRAINT PK_fn_identity_member_seat_operation PRIMARY KEY NONCLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND name=N'IX_fn_identity_member_seat_operation_CreatedAtUtc_Id')
    CREATE CLUSTERED INDEX IX_fn_identity_member_seat_operation_CreatedAtUtc_Id ON dbo.fn_identity_member_seat_operation(CreatedAtUtc,Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND name=N'UX_fn_identity_member_seat_operation_TenantId_RequestKey')
    CREATE UNIQUE INDEX UX_fn_identity_member_seat_operation_TenantId_RequestKey ON dbo.fn_identity_member_seat_operation(TenantId,RequestKey);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND name=N'IX_fn_identity_member_seat_operation_Phase_DueAtUtc')
    CREATE INDEX IX_fn_identity_member_seat_operation_Phase_DueAtUtc ON dbo.fn_identity_member_seat_operation(Phase,DueAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'成员席位激活与取消的持久恢复工作项',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作项逻辑 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'RequestKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'租户内稳定请求身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'RequestKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信目标租户标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'UserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'待激活账号标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'UserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'MemberId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本模块成员关系标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'MemberId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'InvitationId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可空的原始邀请标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'InvitationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'PreviousRole','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活失败时的原始成员角色',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'PreviousRole';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'PreviousStatus','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活失败时的原始成员状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'PreviousStatus';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'MemberVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'待激活成员的冻结并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'MemberVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'InvitationVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'接受邀请后的冻结并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'InvitationVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'Phase','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'席位恢复阶段',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'Phase';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'RequestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不含密码的创建请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'RequestHash';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'ErrorCode','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'稳定失败机器码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'ErrorCode';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作项创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'DueAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'下次恢复时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'DueAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'LeaseId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前执行租约身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'LeaseId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'LeaseUntilUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前执行租约截止 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'LeaseUntilUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作项乐观并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'Version';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'Attempt','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已取消后的新业务尝试序号，不复用旧配额消费键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'Attempt';