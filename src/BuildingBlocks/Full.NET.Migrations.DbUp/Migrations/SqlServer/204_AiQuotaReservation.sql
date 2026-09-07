-- 204：预留与配额在同一本地事务写入，外部推理不占用事务。
IF OBJECT_ID(N'dbo.fn_ai_quota_reservation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_quota_reservation (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        QuotaMonthKey varchar(7) NOT NULL,
        ReservedTokens bigint NOT NULL,
        IsSettled bit NOT NULL,
        ActualTokens bigint NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        SettledAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_ai_quota_reservation PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_ai_quota_reservation_Tokens CHECK (ReservedTokens > 0 AND (ActualTokens IS NULL OR ActualTokens >= 0))
    );
    CREATE CLUSTERED INDEX IX_fn_ai_quota_reservation_CreatedAtUtc_Id ON dbo.fn_ai_quota_reservation (CreatedAtUtc, Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_quota_reservation') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_quota_reservation'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'生成预留 UUID v7 标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_quota_reservation', @level2type=N'COLUMN', @level2name=N'Id';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_quota_reservation') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_quota_reservation'), N'TenantId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配额所属租户',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_quota_reservation', @level2type=N'COLUMN', @level2name=N'TenantId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_quota_reservation') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_quota_reservation'), N'QuotaMonthKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留所属 UTC 月份',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_quota_reservation', @level2type=N'COLUMN', @level2name=N'QuotaMonthKey';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_quota_reservation') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_quota_reservation'), N'ReservedTokens', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已占用的保守 Token 预算',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_quota_reservation', @level2type=N'COLUMN', @level2name=N'ReservedTokens';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_quota_reservation') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_quota_reservation'), N'IsSettled', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否已领取结算权',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_quota_reservation', @level2type=N'COLUMN', @level2name=N'IsSettled';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_quota_reservation') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_quota_reservation'), N'ActualTokens', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整提供程序计量，未知时为空',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_quota_reservation', @level2type=N'COLUMN', @level2name=N'ActualTokens';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_quota_reservation') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_quota_reservation'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留创建时间 UTC',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_quota_reservation', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_quota_reservation') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_quota_reservation'), N'SettledAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结算完成时间 UTC',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_quota_reservation', @level2type=N'COLUMN', @level2name=N'SettledAtUtc';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_quota_reservation') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户 AI 用量预留与一次性结算凭据',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_quota_reservation';
