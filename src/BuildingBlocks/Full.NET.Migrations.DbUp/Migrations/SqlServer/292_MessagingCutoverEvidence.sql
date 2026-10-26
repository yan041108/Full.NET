-- 独立创建及索引探测确保中断后可重跑。
IF OBJECT_ID(N'dbo.fn_messaging_cutover_evidence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_cutover_evidence
    (
        Id uniqueidentifier NOT NULL,
        EventType nvarchar(256) NOT NULL,
        SchemaVersion int NOT NULL,
        ResourceFingerprint varchar(64) NOT NULL,
        SourceDigest varchar(64) NOT NULL,
        ShadowPositionDigest varchar(64) NOT NULL,
        SampleCount int NOT NULL,
        VerifiedAtUtc datetimeoffset(7) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        OperatorUserId uniqueidentifier NOT NULL,
        Reason nvarchar(512) NOT NULL,
        CONSTRAINT PK_fn_messaging_cutover_evidence PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_messaging_cutover_evidence_SampleCount CHECK (SampleCount BETWEEN 1 AND 10000)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND name = N'IX_fn_messaging_cutover_evidence_VerifiedAtUtc_Id')
    CREATE CLUSTERED INDEX IX_fn_messaging_cutover_evidence_VerifiedAtUtc_Id ON dbo.fn_messaging_cutover_evidence (VerifiedAtUtc, Id);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'切流影子验证与真实目标链路的短期证明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'Id', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用生成的切流证明 UUID v7', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'EventType', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'证明所属事件流的稳定消息类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'EventType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'SchemaVersion', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件流结构版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'ResourceFingerprint', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'环境配置与真实目标实例的 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'ResourceFingerprint';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'SourceDigest', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权威样本完整摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'SourceDigest';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'ShadowPositionDigest', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整影子扫描的固定起止位点摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'ShadowPositionDigest';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'SampleCount', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已完整核对的样本数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'SampleCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'VerifiedAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'验证成功的 UTC 时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'VerifiedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'ExpiresAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'证明失效的 UTC 时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'OperatorUserId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信验证操作者标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'OperatorUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_cutover_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cutover_evidence'), N'Reason', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'请求生成证明的不可变运维理由', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cutover_evidence', @level2type=N'COLUMN', @level2name=N'Reason';
