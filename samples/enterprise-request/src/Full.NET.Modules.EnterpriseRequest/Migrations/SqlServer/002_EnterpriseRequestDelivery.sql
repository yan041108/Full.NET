-- 企业申请附件与报表共用的显式交付日记；外部文件 I/O 不加入本地事务。
IF OBJECT_ID(N'dbo.demo_enterprise_request_delivery',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.demo_enterprise_request_delivery (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NOT NULL,
    RequestId uniqueidentifier NOT NULL,
    RequestKey uniqueidentifier NOT NULL,
    Kind varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    RequestedVersion bigint NOT NULL,
    ActorUserId uniqueidentifier NOT NULL,
    SessionId uniqueidentifier NOT NULL,
    SecurityStamp varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ActorScope varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    EffectiveScope varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    SessionKind varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    SourceDigest varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    IncludesAmounts bit NOT NULL,
    LocaleCode varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    FileId uniqueidentifier NULL,
    FileName nvarchar(255) NOT NULL,
    ContentType varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    SizeBytes bigint NULL,
    ContentHash varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    Phase varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    LeaseId uniqueidentifier NULL,
    LeaseUntilUtc datetimeoffset(7) NULL,
    Attempt int NOT NULL,
    DueAtUtc datetimeoffset(7) NOT NULL,
    ExpiresAtUtc datetimeoffset(7) NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    UpdatedAtUtc datetimeoffset(7) NOT NULL,
    Version bigint NOT NULL,
    CONSTRAINT PK_demo_enterprise_request_delivery PRIMARY KEY CLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND name=N'UX_demo_enterprise_request_delivery_TenantId_RequestId__15a99a28')
    CREATE UNIQUE INDEX UX_demo_enterprise_request_delivery_TenantId_RequestId__15a99a28 ON dbo.demo_enterprise_request_delivery(TenantId, RequestId, RequestKey, Kind, ActorUserId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND name=N'IX_demo_enterprise_request_delivery_Phase_DueAtUtc_TenantId')
    CREATE INDEX IX_demo_enterprise_request_delivery_Phase_DueAtUtc_TenantId ON dbo.demo_enterprise_request_delivery(Phase, DueAtUtc, TenantId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND name=N'IX_demo_enterprise_request_delivery_TenantId_RequestId__ee9606bc')
    CREATE INDEX IX_demo_enterprise_request_delivery_TenantId_RequestId__ee9606bc ON dbo.demo_enterprise_request_delivery(TenantId, RequestId, Kind, CreatedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'企业申请附件和报表交付恢复日记',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用生成的交付身份 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'RequestId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属申请单',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'RequestId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'RequestKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'调用方幂等请求标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'RequestKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'Kind','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Attachment 或 Report',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'Kind';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'RequestedVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结主单版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'RequestedVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原始申请主体',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'ActorUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'SessionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原始会话',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'SessionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'SecurityStamp','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结安全戳',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'SecurityStamp';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'ActorScope','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原始主体作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'ActorScope';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'EffectiveScope','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结有效租户作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'EffectiveScope';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'SessionKind','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'刷新或 OIDC 应用会话',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'SessionKind';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'SourceDigest','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'内容或已授权单据快照摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'SourceDigest';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'IncludesAmounts','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'是否包含获授权金额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'IncludesAmounts';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'LocaleCode','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'生成文案的规范语言标签',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'LocaleCode';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'FileId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定的 Files 文件',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'FileId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'FileName','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'安全下载文件名',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'FileName';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'ContentType','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定或经验证的媒体类型',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'ContentType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'SizeBytes','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际文件字节数',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'SizeBytes';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'ContentHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'待绑定产物内容摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'ContentHash';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'Phase','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'持久化交付及清理状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'Phase';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'ErrorCode','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'稳定失败码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'ErrorCode';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'LeaseId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本轮恢复租约',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'LeaseId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'LeaseUntilUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'租约截止时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'LeaseUntilUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'Attempt','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际领取次数',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'Attempt';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'DueAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'下一轮可执行时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'DueAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'ExpiresAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'上传意图或报表结果截止时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'ExpiresAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最后状态更新时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.demo_enterprise_request_delivery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_delivery'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'单调并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'demo_enterprise_request_delivery',@level2type=N'COLUMN',@level2name=N'Version';
